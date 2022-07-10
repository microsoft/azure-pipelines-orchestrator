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

IAgentHostService hostService = null;

switch (agentHostType)
{
    case "kubernetes":
    case "k8s":
        hostService = new KubernetesAgentHostService(config);
    break;
    default:
        throw new Exception($"Host type [{agentHostType}] is not valid.");
}

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
        var agentPool = (await DistributedTask.GetAgentPoolsAsync(agentPoolName)).FirstOrDefault();
        if (agentPool == null)
        {
            Console.WriteLine($"Could not locate agent pool named [{agentPoolName}].");
            continue;
        }
        var agents = await DistributedTask.GetAgentsAsync(agentPool.Id, includeAssignedRequest: true);
        var jobRequests = (await DistributedTask.GetAgentRequestsAsync(agentPool.Id, 999))
            .Where(x => !x.Result.HasValue);

        if (!jobRequests.Any())
        {
            Console.WriteLine($"No jobs for agent pool [{agentPoolName}].");
            continue;
        }

        foreach (var jobRequest in jobRequests)
        {
            var reservedAgent = agents.FirstOrDefault(x => x.Id == jobRequest.ReservedAgent?.Id);
            var reservedAgentIsBusy = reservedAgent?.AssignedRequest != null;
            var jobAlreadyProvisioned = await hostService.IsJobProvisioned(jobRequest.RequestId);

            if (jobAlreadyProvisioned)
            {
                Console.WriteLine($"Pipelines agent job already provisioned for request id #{jobRequest.RequestId}.. skipping");
            }
            else if (reservedAgent == null || reservedAgentIsBusy)
            {
                // Todo: Provision new agent in K8s - need to in future add logic to avoid double 
                // provisioning if this block gets executed before k8s has a chance to spin up the agent
                try
                {
                    await hostService.StartAgent(jobRequest.RequestId, agentPoolName);
                    Console.WriteLine($"Pipelines agent job provisioned for request id #{jobRequest.RequestId}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Failed to provision agent job for request id #{jobRequest.RequestId}.");
                    Console.WriteLine($"{ex.ToString()}");
                }
            }
            else
            {
                Console.WriteLine($"Job request [{jobRequest.RequestId}] is queued and an agent is available in pool [{agentPoolName}]. ADO will pick it up shortly.");
            }

        }
    }

    if (config.GetValue<int>("RUN_ONCE", 0) == 1) break;

    Console.WriteLine($"Delaying [{pollingDelay / 1000}] seconds.");
    await Task.Delay(pollingDelay);
}
