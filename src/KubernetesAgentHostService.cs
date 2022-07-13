using Microsoft.Extensions.Configuration;
using k8s;
using k8s.Models;

public class KubernetesAgentHostService : BaseHostService, IAgentHostService
{
    private const string DEFAULT_JOB_PREFIX = "agent-job";
    private string _namespace;
    private string _jobImage;
    private string _azpUrl;
    private string _azpPat;
    private string _dockerSockPath;
    private string _jobDefFile;
    private V1Job _predefinedJob;
    private Kubernetes _kubectl;

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