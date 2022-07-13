using Microsoft.Extensions.Configuration;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

var agentHostType = config.GetValue<string>("HOST_TYPE", "kubernetes").ToLower();
var orgUrl = config.GetValue<string>("ORG_URL");
var orgPat = config.GetValue<string>("ORG_PAT");
var agentPools = config.GetValue<string>("AGENT_POOLS").Split(',');

Dictionary<string, IAgentHostService> hostServices = new Dictionary<string, IAgentHostService>();

Console.WriteLine("Starting Agent Orchestrator ..");
Console.WriteLine($"ORG_URL: {orgUrl}");

var creds = new VssBasicCredential(string.Empty, orgPat);

// Connect to Azure DevOps Services
var connection = new VssConnection(new Uri(orgUrl), creds);

var DistributedTask = connection.GetClient<TaskAgentHttpClient>();

Int32.TryParse(config.GetValue<string>("POLLING_DELAY") ?? "1000", out var pollingDelay);


var jobImage = config.GetValue<string>("JOB_IMAGE");
if (jobImage == null) throw new Exception("No Job image specified. Cannot continue. Make sure to configure JOB_IMAGE.");

while (true)
{
    foreach (var agentPoolName in agentPools)
    {
        IAgentHostService hostService = hostServices.ContainsKey(agentPoolName) ? hostServices[agentPoolName] : null;
        if (hostService == null)
        {
            switch (agentHostType)
            {
                case "kubernetes":
                case "k8s":
                    hostService = new KubernetesAgentHostService(config, agentPoolName);
                    hostServices.Add(agentPoolName, hostService);
                    break;
                    
                case "aci":
                case "azurecontainerinstance":
                case "azurecontainerinstances":
                    hostService = new ACIAgentHostService(config, agentPoolName);
                    break;

                default:
                    throw new Exception($"Host type [{agentHostType}] is not valid.");
            }

            await hostService.Initialize();
        }

        var agentPool = (await DistributedTask.GetAgentPoolsAsync(agentPoolName)).FirstOrDefault();
        if (agentPool == null)
        {
            Console.WriteLine($"Could not locate agent pool named [{agentPoolName}].");
            continue;
        }
        var agents = (await DistributedTask.GetAgentsAsync(agentPool.Id, includeAssignedRequest: true))
            .Where(a => a.Status == TaskAgentStatus.Online && a.Enabled == true);

        // Let our host service know which agents are currently online.. it will trim out any workers that have gone offline
        await hostService.UpdateWorkersState(agents.Select(a => new WorkerAgent()
        {
            Id = a.Name,
            IsBusy = a.AssignedRequest != null
        }));

        var jobRequests = (await DistributedTask.GetAgentRequestsAsync(agentPool.Id, 999))
            .Where(x => !x.Result.HasValue);

        // A JobRequest object with no Result means it is either currently running and hasn't completed or hasn't started running yet 
        if (!jobRequests.Any())
        {
            Console.WriteLine($"No jobs for agent pool [{agentPoolName}].");
            continue;
        }

        int agentDemand = 0;

        foreach (var jobRequest in jobRequests)
        {
            // ADO populates a "ReservedAgent" property on a given JobRequest when it has identified which job agent it expects to run a job
            var reservedAgent = agents.FirstOrDefault(x => x.Id == jobRequest.ReservedAgent?.Id);
            // If there is a reserved agent.. and that reservedAgent happens to already have an assigned request.. this means there are not 
            // enough agents running to meet the demand.. so we should introduce a new agent to the pool
            var reservedAgentIsBusy = reservedAgent?.AssignedRequest != null && reservedAgent.AssignedRequest.RequestId != jobRequest.RequestId;
            if (reservedAgent == null || reservedAgentIsBusy)
            {
                agentDemand++;
            }
        }

        // Host Service will assess it's current scheduled workers and add more as needed if there are not enough idle agents
        await hostService.UpdateDemand(agentDemand);
    }

    if (config.GetValue<int>("RUN_ONCE", 0) == 1) break;

    Console.WriteLine($"Delaying [{pollingDelay / 1000}] seconds.");
    await Task.Delay(pollingDelay);
}
