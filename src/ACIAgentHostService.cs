using Microsoft.Azure.Management.ContainerInstance.Fluent;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Extensions.Configuration;
using Azure.Core;

public class ACIAgentHostService : IAgentHostService
{
    private IAzure _az;
    public ACIAgentHostService(IConfiguration config)
    {
        var _azSub = config.GetValue<string>("AZ_SUBSCRIPTION_ID");
        
    }
    public Task<bool> IsJobProvisioned(long requestId)
    {
        throw new NotImplementedException();
    }
    public Task StartAgent(long requestId, string agentPool)
    {
        throw new NotImplementedException();
    }
    public Task Initialize()
    {
        throw new NotImplementedException();
    }
}