using System.Linq;
using Microsoft.Extensions.Configuration;

public interface IAgentHostService
{
    Task UpdateDemand(int agentDemand);
    Task<WorkerAgent> StartAgent();
    Task Initialize();
    int ScheduledWorkerCount {get;}
    Task UpdateWorkersState(IEnumerable<WorkerAgent> updatedAgents);
}

public class WorkerAgent 
{
    public string Id {get;set;}
    public bool IsBusy {get;set;}
    public bool IsProvisioning => ProvisioningStart.HasValue;
    public DateTime? ProvisioningStart { get; set; }
}
