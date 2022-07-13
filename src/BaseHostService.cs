public class BaseHostService
{
    protected List<WorkerAgent> _workers = new List<WorkerAgent>();
    protected TimeSpan AGENT_PROVISIONING_TIMEOUT = TimeSpan.FromMinutes(5);
    protected string _poolName;
    protected string _jobPrefix;

    public int ScheduledWorkerCount => _workers.Count();
    protected string FormatJobName() => $"{_jobPrefix}-{_poolName}-{Guid.NewGuid().ToString().Substring(0, 4)}".ToLower();
    public virtual async Task UpdateDemand(int agentDemand)
    {
        // There appears to be X Job Requests that are not currently assigned to an agent that is idle
        // Let's assess the number of "Idle" or Provisioning agents vs the demand
        var netAgentDemand = agentDemand - _workers.Count(w => w.IsProvisioning || !w.IsBusy);
        if (netAgentDemand > 0)
        {
            // Not enough Agents to meet demand.. lets provision some!
            for (var i = 0; i < netAgentDemand; i++)
            {
                try
                {
                    var worker = await StartAgent();
                    Console.WriteLine($"Provisioned agent worker {worker.Id} to meet demand of {netAgentDemand}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to provision agent." + Environment.NewLine + ex.ToString());
                }
            }
        }
        else if (netAgentDemand < 0)
        {
            // We have too many agents in relation to the demand.. lets cut back
            Console.WriteLine($"There are currently over provisioned {netAgentDemand} agents");
        }
    }

    public virtual Task UpdateWorkersState(IEnumerable<WorkerAgent> updatedAgents)
    {
        // Remove any agents that have left the pool (as long as they're not provisioning and they're not present in updatedAgents)
        // We exclude provisioning because sometimes it takes multiple seconds to provision an agent
        List<WorkerAgent> toDelete = new List<WorkerAgent>();
        foreach (var existingWorker in _workers)
        {
            // Check if Provisioning has taken too long
            if (existingWorker.IsProvisioning && (DateTime.UtcNow - existingWorker.ProvisioningStart).TotalMinutes >= AGENT_PROVISIONING_TIMEOUT.TotalMinutes)
            {
                toDelete.Add(existingWorker);
                continue;
            }

            // Check if agent no longer exists in the pool
            var updatedAgent = updatedAgents.FirstOrDefault(a => a.Id.StartsWith(existingWorker.Id));
            if (updatedAgent == null) // We use StartsWith here b/c K8s job pods have a suffix appended
            {
                toDelete.Add(existingWorker);
                continue;
            }

            // Update existing WorkerAgent with updated state info
            existingWorker.Id = updatedAgent.Id;
            existingWorker.IsBusy = updatedAgent.IsBusy;
            updatedAgent.IsProvisioning = false;
        }
        // Add any new workers that made their way into the pool
        _workers.AddRange(updatedAgents.Where(u => !_workers.Any(w => w.Id.StartsWith(u.Id))));

        return Task.CompletedTask;
    }
    public virtual Task<WorkerAgent> StartAgent() => throw new NotImplementedException();
}
