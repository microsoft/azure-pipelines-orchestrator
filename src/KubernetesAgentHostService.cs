using Microsoft.Extensions.Configuration;
using k8s;
using k8s.Models;

public class KubernetesAgentHostService : IAgentHostService
{
    private TimeSpan AGENT_PROVISIONING_TIMEOUT = TimeSpan.FromMinutes(5);
    private const string DEFAULT_JOB_PREFIX = "agent-job-";
    private string _namespace;
    private string _jobPrefix;
    private string _jobImage;
    private string _azpUrl;
    private string _azpPat;
    private string _dockerSockPath;
    private string _jobDefFile;
    private V1Job _predefinedJob;
    private Kubernetes _kubectl;
    private string _poolName;


    private List<WorkerAgent> _workers = new List<WorkerAgent>();


    public KubernetesAgentHostService(IConfiguration config, string poolName)
    {
        var kubeConfPath = config.GetValue<string>("KUBECONFIG", null);
        if (kubeConfPath == null) throw new ArgumentNullException("'KUBECONFIG' environment variable required but missing.");
        using var kubeConfigFile = File.OpenRead(kubeConfPath);
        _kubectl = new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeConfigFile));
        _namespace = config.GetValue<string>("JOB_NAMESPACE", "default");
        _jobPrefix = config.GetValue<string>("JOB_PREFIX", DEFAULT_JOB_PREFIX);
        _jobImage = config.GetValue<string>("JOB_IMAGE");
        _azpUrl = config.GetValue<string>("ORG_URL");
        _azpPat = config.GetValue<string>("ORG_PAT");
        _dockerSockPath = config.GetValue<string>("JOB_DOCKER_SOCKET_PATH");
        _jobDefFile = config.GetValue<string>("JOB_DEFINITION_FILE");
        _poolName = poolName;
        if (_jobDefFile != null)
        {
            _predefinedJob = KubernetesYaml.Deserialize<V1Job>(System.IO.File.ReadAllText(_jobDefFile));
            _jobPrefix = _predefinedJob.Metadata.Name ?? _jobPrefix;
        }
    }

    public async Task Initialize()
    {
        if (!(await _kubectl.CoreV1.ListNamespaceAsync()).Items.Any(ns => ns.Name() == _namespace))
        {
            await _kubectl.CoreV1.CreateNamespaceAsync(new V1Namespace()
            {
                Metadata = new V1ObjectMeta()
                {
                    Name = _namespace
                }
            });
        }
    }
    public async Task UpdateDemand(int agentDemand)
    {
        // There appears to be X Job Requests that are not currently assigned to an agent that is idle
        // Let's assess the number of "Idle" or Provisioning agents vs the demand
        var netAgentDemand = agentDemand - _workers.Count(w => w.IsProvisioning || !w.IsBusy);
        if (netAgentDemand > 0)
        {
            // Not enough Agents to meet demand.. lets provision some!
            for (var i = 0; i < netAgentDemand; i++)
            {
                var worker = await StartAgent();
                Console.WriteLine($"Provisioned agent worker {worker.Id} to meet demand of {netAgentDemand}");
            }
        }
        else if (netAgentDemand < 0)
        {
            // We have too many agents in relation to the demand.. lets cut back
            Console.WriteLine($"There are currently over provisioned {netAgentDemand} agents");
        }
    }
    public Task UpdateWorkersState(IEnumerable<WorkerAgent> updatedAgents)
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

    public int ScheduledWorkerCount => _workers.Count;
    private string FormatJobName() => $"{_jobPrefix}-{_poolName}-{Guid.NewGuid().ToString().Substring(0, 4)}".ToLower();


    public async Task<WorkerAgent> StartAgent()
    {
        V1Job job = _predefinedJob;
        var name = FormatJobName();
        if (_predefinedJob != null)
        {
            _predefinedJob.Metadata.Name = name;
        }
        else
        {
            job = new V1Job()
            {
                ApiVersion = "batch/v1",
                Kind = "Job",
                Metadata = new V1ObjectMeta()
                {
                    Name = name,
                    NamespaceProperty = _namespace
                },
                Spec = new V1JobSpec()
                {
                    Template = new V1PodTemplateSpec()
                    {
                        Spec = new V1PodSpec()
                        {
                            RestartPolicy = "Never",
                            Containers = new List<V1Container>(){
                            new V1Container() {
                                Name = name,
                                Image = _jobImage,
                                Env = new List<V1EnvVar>() {
                                    new V1EnvVar() {
                                        Name = "AZP_URL",
                                        Value = _azpUrl
                                    },
                                    new V1EnvVar() {
                                        Name = "AZP_TOKEN",
                                        Value = _azpPat
                                    },
                                    new V1EnvVar() {
                                        Name = "AZP_POOL",
                                        Value = _poolName
                                    }
                                }
                            }
                        }
                        }
                    }
                }

            };


            // Not all K8s environments support the docker.sock
            // This option allows users to opt-in to adding the volume mount
            if (_dockerSockPath != null)
            {
                var podSpec = job.Spec.Template.Spec;
                var volumeName = "docker-volume";

                podSpec.Volumes = new List<V1Volume>() {
                    new V1Volume() {
                        Name = volumeName,
                        HostPath = new V1HostPathVolumeSource() {
                            Path = _dockerSockPath
                        }
                    }
                };

                podSpec.Containers[0].VolumeMounts = new List<V1VolumeMount>() {
                    new V1VolumeMount() {
                        MountPath = _dockerSockPath,
                        Name = volumeName
                    }
                };
            }
        }

        job = await _kubectl.CreateNamespacedJobAsync(job, namespaceParameter: _namespace);

        WorkerAgent worker = null;
        _workers.Add(worker = new WorkerAgent()
        {
            Id = name,
            IsBusy = false,
            IsProvisioning = true,
            ProvisioningStart = DateTime.UtcNow
        });
        return worker;
    }
}