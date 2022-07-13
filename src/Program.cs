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
    case "aci":
    case "azurecontainerinstance":
    case "azurecontainerinstances":
        hostService = new ACIAgentHostService(config);
    break;
    default:
        throw new Exception($"Host type [{agentHostType}] is not valid.");
}

await hostService.Initialize();

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
        // A JobRequest object with no Result means it is either currently running or hasn't started running yet and hasn't completed

        if (!jobRequests.Any())
        {
            Console.WriteLine($"No jobs for agent pool [{agentPoolName}].");
            continue;
        }

        foreach (var jobRequest in jobRequests)
        {
            // ADO populates a "ReservedAgent" property on a given JobRequest when it has identified which job agent it expects to run a job
            var reservedAgent = agents.FirstOrDefault(x => x.Id == jobRequest.ReservedAgent?.Id);
            // If there is a reserved agent.. and that reservedAgent happens to already have an assigned request.. this means there are not 
            // enough agents running to meet the demand.. so we should introduce a new agent to the pool
            var reservedAgentIsBusy = reservedAgent?.AssignedRequest != null;

            // Has a job agent already been provisioned for this specific request? Here is where things can get tricky. A given job agent is
            // not actually provisioned to satisfy a specific Job Request. It is added to the pool where it will satisfy requests in the pool 
            // in general.. so its possible that a Job Agent is provisioned on behalf of JobRequest #1 but ends up taking up JobRequest #2
            var jobAlreadyProvisioned = await hostService.IsJobProvisioned(jobRequest.RequestId);
            
            
            if (jobAlreadyProvisioned)
            {
                Console.WriteLine($"Pipelines agent job already provisioned for request id #{jobRequest.RequestId}.. skipping");
            }
            else if (reservedAgent == null || reservedAgentIsBusy)
            {
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
