using System.Linq;

public interface IAgentHostService
{
    Task<bool> IsJobProvisioned(long requestId);
    Task StartAgent(long requestId, string agentPool);
    Task Initialize();
}

