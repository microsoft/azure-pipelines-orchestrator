public class BaseHostService
{
    protected int _minimumAgentCount = 1;
    protected List<WorkerAgent> _workers = new List<WorkerAgent>();
    protected TimeSpan AGENT_PROVISIONING_TIMEOUT = TimeSpan.FromMinutes(5);
    protected string _poolName;
    protected string _jobPrefix;

    public virtual int ScheduledWorkerCount => _workers.Count();
    protected string FormatJobName() => $"{_jobPrefix}-{_poolName}-{Guid.NewGuid().ToString().Substring(0, 4)}".ToLower().Replace(" ", "-");
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
                    _workers.Add(worker);
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

    public async virtual Task UpdateWorkersState(IEnumerable<WorkerAgent> realAgents)
    {
        // Remove any agents that have left the pool (as long as they're not provisioning and they're not present in updatedAgents)
        // We exclude provisioning because sometimes it takes multiple seconds to provision an agent

        // Update workers adding ones that made their way into the pool in reality and updating existing ones
        foreach (var realAgent in realAgents)
        {
            var knownWorker = _workers.FirstOrDefault(w => w.Id.StartsWith(w.Id));
            if (knownWorker == null)
            {
                _workers.Add(realAgent);
            }
            else
            {
                knownWorker.Id = realAgent.Id;
                knownWorker.IsBusy = realAgent.IsBusy;
                knownWorker.ProvisioningStart = null;
            }
        }

        List<WorkerAgent> toDelete = new List<WorkerAgent>();
        foreach (var existingWorker in _workers)
        {
            // Check if Provisioning has taken too long on existing known worker
            if (existingWorker.IsProvisioning && (DateTime.UtcNow - existingWorker.ProvisioningStart.Value).TotalMinutes >= AGENT_PROVISIONING_TIMEOUT.TotalMinutes)
            {
                toDelete.Add(existingWorker);
                continue;
            }

            // Check if agent no longer exists in the pool
            var realAgent = realAgents.FirstOrDefault(a => a.Id.StartsWith(existingWorker.Id));
            var doesntExist = realAgent == null;
            if (doesntExist && !existingWorker.IsProvisioning) // We use StartsWith here b/c K8s job pods have a suffix appended
            {
                toDelete.Add(existingWorker);
            }
        }

        // Remove any workers that have left the pool
        foreach (var worker in toDelete) _workers.Remove(worker);


        if (_workers.Count < _minimumAgentCount)
        {
            // Check to see if the agent pool has no existing agents if so, we need to start one as a baseline
            // Otherwise ADO will not be able to queue the pipelines jobs
            for (var i = 0; i < _minimumAgentCount; i++)
            {
                _workers.Add(await StartAgent());
            }
        }

    }
    public virtual Task<WorkerAgent> StartAgent() => throw new NotImplementedException();
}
