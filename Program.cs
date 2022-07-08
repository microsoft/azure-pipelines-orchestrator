using Microsoft.Extensions.Configuration;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using k8s;

var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();
    
var orgUrl = config.GetValue<string>("ORG_URL");
var orgPat = config.GetValue<string>("ORG_PAT");
var agentPools = config.GetValue<string>("AGENT_POOLS").Split(',');
var k8sConfig = config.GetValue<string>("KUBE_CONFIG");
using var kubeConfigFile = File.OpenRead(k8sConfig);
using var kubectl = new Kubernetes(await KubernetesClientConfiguration.BuildConfigFromConfigFileAsync(kubeConfigFile));

Console.WriteLine("Starting Agent Orchestrator Balancing..");
Console.WriteLine($"ORG_URL: {orgUrl}");

var creds = new VssBasicCredential(string.Empty, orgPat);

// Connect to Azure DevOps Services
var connection = new VssConnection(new Uri(orgUrl), creds);

var DistributedTask = connection.GetClient<TaskAgentHttpClient>();

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
        var reservedAgent = agents.FirstOrDefault(x => x.Id == jobRequest.ReservedAgent.Id);
        var agentIsBusy = reservedAgent.AssignedRequest != null;

        if (agentIsBusy)
        {
            // Todo: Provision new agent in K8s - need to in future add logic to avoid double 
            // provisioning if this block gets executed before k8s has a chance to spin up the agent
            
        }
        else
        {
            Console.WriteLine($"Job request [{jobRequest.RequestId}] is queued and an agent is available in pool [{agentPoolName}]. ADO will pick it up shortly.");
        }

    }
}
