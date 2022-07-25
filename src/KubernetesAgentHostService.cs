// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Configuration;
using k8s;
using k8s.Models;

public interface IK8s : ICoreV1Operations
{
    Task<V1NamespaceList> ListNamespaceAsync();
    Task<V1Job> CreateNamespacedJobAsync(V1Job job, string namespaceParameter)
        => this.CreateNamespacedJobAsync(job, namespaceParameter);
    Task<V1Namespace> CreateNamespaceAsync(V1Namespace body);

}
public class KubernetesWrapper : k8s.Kubernetes, IK8s
{
    public KubernetesWrapper(KubernetesClientConfiguration config) : base(config) { }
    public virtual Task<V1NamespaceList> ListNamespaceAsync()
        => (this as k8s.Kubernetes).CoreV1.ListNamespaceAsync();
    public virtual Task<V1Job> CreateNamespacedJobAsync(V1Job job, string namespaceParameter)
        => (this as k8s.Kubernetes).CreateNamespacedJobAsync(job, namespaceParameter);
    public virtual Task<V1Namespace> CreateNamespaceAsync(V1Namespace body)
        => (this as k8s.Kubernetes).CreateNamespaceAsync(body);
}

public class KubernetesAgentHostService : BaseHostService, IAgentHostService
{
    private const string DEFAULT_JOB_PREFIX = "agent-job";
    private string _namespace;
    private string _jobImage;
    private string _azpUrl;
    private string _azpPat;
    private string _dockerSockPath;
    private string _jobDefFile;
    private bool _initializeNamespace;
    private V1Job _predefinedJob;
    public virtual KubernetesWrapper K8s { get; private set; }

    public KubernetesAgentHostService(IConfiguration config, IFileSystem fs, string poolName)
    {
        // The default config will either check the well known KUBECONFIG env
        // or, if within the cluster, checks /var/run/secrets/kubernetes.io/serviceaccount
        K8s = new KubernetesWrapper(KubernetesClientConfiguration.BuildDefaultConfig());
        _namespace = config.GetValue<string>("JOB_NAMESPACE", "default");
        _jobPrefix = config.GetValue<string>("JOB_PREFIX", DEFAULT_JOB_PREFIX);
        _jobImage = config.GetValue<string>("JOB_IMAGE");
        _azpUrl = config.GetValue<string>("ORG_URL");
        _azpPat = config.GetValue<string>("ORG_PAT");
        _initializeNamespace = config.GetValue<bool>("INITIALIZE_NAMESPACE", true);
        _dockerSockPath = config.GetValue<string>("JOB_DOCKER_SOCKET_PATH");
        _jobDefFile = config.GetValue<string>("JOB_DEFINITION_FILE");
        _minimumAgentCount = config.GetValue<int>("MINIMUM_AGENT_COUNT", 1);
        _minimumIdleAgentCount = config.GetValue<int>("MINIMUM_IDLE_AGENT_COUNT", 0);
        _poolName = poolName;
        if (_jobDefFile != null)
        {
            _predefinedJob = KubernetesYaml.Deserialize<V1Job>(fs.ReadAllText(_jobDefFile));
            _jobPrefix = _predefinedJob.Metadata.Name ?? _jobPrefix;
        }
    }

    public virtual async Task Initialize()
    {
        if (_initializeNamespace)
        {
            var namespaces = (await K8s.ListNamespaceAsync()).Items;
            if (namespaces == null || !namespaces.Any(ns => ns.Name() == _namespace))
            {
                await K8s.CreateNamespaceAsync(new V1Namespace()
                {
                    Metadata = new V1ObjectMeta()
                    {
                        Name = _namespace
                    }
                });
            }
        }
    }

    public override async Task<WorkerAgent> StartAgent()
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

        job = await K8s.CreateNamespacedJobAsync(job, namespaceParameter: _namespace);

        return new WorkerAgent()
        {
            Id = name,
            IsBusy = false,
            ProvisioningStart = DateTime.UtcNow
        };
    }
}