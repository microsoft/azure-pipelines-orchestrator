
using k8s;
using k8s.Models;

static class KubeHelper
{
    public static async Task<V1Namespace> SetupNamespace(this Kubernetes k8) 
    {
        var nameSpace = Environment.GetEnvironmentVariable("JOB_NAMESPACE") ?? "default";
        return await k8.CreateNamespaceAsync(new V1Namespace(){
            Metadata = new V1ObjectMeta() {
                Name = nameSpace
            }
        });
    }
    public static async Task<V1Job> StartJob(this Kubernetes k8, long requestId, string poolName)
    {
        var jobPrefix = Environment.GetEnvironmentVariable("JOB_PREFIX") ?? "agent-job-";
        var nameSpace = Environment.GetEnvironmentVariable("JOB_NAMESPACE") ?? "default";
        var jobImage = Environment.GetEnvironmentVariable("JOB_IMAGE");
        var dockerSocketPath = Environment.GetEnvironmentVariable("JOB_DOCKER_SOCKET_PATH");

        var job = new V1Job()
        {
            ApiVersion = "batch/v1",
            Kind = "Job",
            Metadata = new V1ObjectMeta()
            {
                Name = $"{jobPrefix}{requestId}",
                NamespaceProperty = nameSpace
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
                                Name = $"{jobPrefix}{requestId}",
                                Image = jobImage,
                                Env = new List<V1EnvVar>() {
                                    new V1EnvVar() {
                                        Name = "AZP_URL",
                                        Value = Environment.GetEnvironmentVariable("ORG_URL")
                                    },
                                    new V1EnvVar() {
                                        Name = "AZP_TOKEN",
                                        Value = Environment.GetEnvironmentVariable("ORG_PAT")
                                    },
                                    new V1EnvVar() {
                                        Name = "AZP_POOL",
                                        Value = poolName
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
        if(dockerSocketPath != null)
        {
            var podSpec = job.Spec.Template.Spec;
            var volumeName = "docker-volume";
            
            podSpec.Volumes =  new List<V1Volume>(){
                new V1Volume() {
                    Name = volumeName,
                    HostPath = new V1HostPathVolumeSource() {
                        Path = dockerSocketPath
                    }
                }
            };

            podSpec.Containers[0].VolumeMounts = new List<V1VolumeMount>() {
                new V1VolumeMount() {
                    MountPath = dockerSocketPath,
                    Name = volumeName
                }
            };
        }

        return await k8.CreateNamespacedJobAsync(job, namespaceParameter: nameSpace);
    }
}