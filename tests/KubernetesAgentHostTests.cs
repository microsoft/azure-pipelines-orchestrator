namespace ADOAgentOrchestrator.Tests;
using Moq;


[TestClass]
public class KubernetesAgentHostTests
{

    [TestMethod]
    public void ShouldReadFromJobDefinition()
    {
        var jobDefFileName = "./JobDef.yaml";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>() {
                {"JOB_DEFINITION_FILE", jobDefFileName},
            }.ToList())
            .Build();
        var mockFs = new Mock<FileSystem>();
        mockFs.Setup(f => f.ReadAllText(jobDefFileName)).Returns(JobDefYaml);

        var svcMock =  new Mock<KubernetesAgentHostService>(MockBehavior.Strict, config, mockFs.Object, string.Empty);
        var svcConcrete = svcMock.Object;

        mockFs.Verify(x => x.ReadAllText(jobDefFileName), Times.Once, "Should fetch jod definition from disk when provided.");
    }

    [TestMethod]
    public async Task ShouldAddNewAgentsToMeetMinimumCount()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>() {
                {"MINIMUM_AGENT_COUNT", "2"},
            }.ToList()).Build();

        var svcMock =  new Mock<KubernetesAgentHostService>(config, new FileSystem(), "agentPool");
        svcMock.SetupGet(x => x.ScheduledWorkerCount).CallBase();
        svcMock.Setup(x => x.UpdateWorkersState(It.IsAny<List<WorkerAgent>>())).CallBase();
        svcMock.Setup(x => x.StartAgent()).Returns(Task.FromResult(new WorkerAgent() {
            Id = "Worker" + Guid.NewGuid().ToString(),
            ProvisioningStart = DateTime.UtcNow
        }));
        var svcConcrete = svcMock.Object;

        await svcConcrete.UpdateWorkersState(new List<WorkerAgent>() { });
        Assert.AreEqual(2, svcConcrete.ScheduledWorkerCount, "Should maintain minimum agent count");
    }

    [TestMethod]
    public async Task ShouldUpdateWorkerState()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>() {
                {"MINIMUM_AGENT_COUNT", "0"},
            }.ToList()).Build();

        var svcMock =  new Mock<KubernetesAgentHostService>(config, new FileSystem(), "agentPool");
        svcMock.SetupGet(x => x.ScheduledWorkerCount).CallBase();
        svcMock.Setup(x => x.UpdateWorkersState(It.IsAny<List<WorkerAgent>>())).CallBase();
        svcMock.Setup(x => x.StartAgent()).Returns(Task.FromResult(new WorkerAgent() {
            Id = "Worker" + Guid.NewGuid().ToString(),
            ProvisioningStart = DateTime.UtcNow
        }));
        var svcConcrete = svcMock.Object;
        await svcConcrete.UpdateWorkersState(new List<WorkerAgent>() {
            new WorkerAgent() {
                Id = "Worker1"
            }
        });
        
        Assert.AreEqual(1, svcConcrete.ScheduledWorkerCount, "Should add new workers");

        await svcConcrete.UpdateWorkersState(new List<WorkerAgent>() {
            new WorkerAgent() {
                Id = "Worker1",
                IsBusy = true
            }
        });
        Assert.AreEqual(1, svcConcrete.ScheduledWorkerCount, "Should not add duplicate new workers");

        await svcConcrete.UpdateWorkersState(new List<WorkerAgent>() { });
        Assert.AreEqual(0, svcConcrete.ScheduledWorkerCount, "Should remove agents from tracking when they leave the real pool");
    }
    
    [TestMethod]
    public async Task ShouldProvisionNewAgent_AsNeeded()
    {
        var config = new ConfigurationBuilder().Build();

        var svcMock =  new Mock<KubernetesAgentHostService>(config, new FileSystem(), "agentPool");
        svcMock.SetupGet(x => x.ScheduledWorkerCount).CallBase();
        svcMock.Setup(x => x.UpdateDemand(It.IsAny<int>())).CallBase();
        svcMock.Setup(x => x.StartAgent()).Returns(Task.FromResult(new WorkerAgent() {
            Id = "Worker2",
            ProvisioningStart = DateTime.UtcNow
        }));
        var svcConcrete = svcMock.Object;
        
        await svcConcrete.UpdateDemand(1);

        Assert.AreEqual(1, svcConcrete.ScheduledWorkerCount, "Should add new workers");
    }

    [TestMethod]
    public async Task ShouldProvisionNewAgent_IfExistingAreBusy()
    {
        var config = new ConfigurationBuilder().Build();

        var svcMock =  new Mock<KubernetesAgentHostService>(config, new FileSystem(), "agentPool");
        svcMock.SetupGet(x => x.ScheduledWorkerCount).CallBase();
        svcMock.Setup(x => x.UpdateDemand(It.IsAny<int>())).CallBase();
        svcMock.Setup(x => x.UpdateWorkersState(It.IsAny<List<WorkerAgent>>())).CallBase();
        svcMock.Setup(x => x.StartAgent()).Returns(Task.FromResult(new WorkerAgent() {
            Id = "Worker2",
            ProvisioningStart = DateTime.UtcNow
        }));
        var svcConcrete = svcMock.Object;
        // Place an existing worker into agents
        await svcConcrete.UpdateWorkersState(new List<WorkerAgent>() {
            new WorkerAgent() {
                Id = "Worker1",
                IsBusy = true
            }
        });
        
        await svcConcrete.UpdateDemand(1);

        Assert.AreEqual(2, svcConcrete.ScheduledWorkerCount, "Should add new workers if existing are busy");
    }

    [TestMethod]
    public async Task ShouldNotProvisionNewAgent_IfExistingAreProvisioning()
    {
        var config = new ConfigurationBuilder().Build();

        var svcMock =  new Mock<KubernetesAgentHostService>(config, new FileSystem(), "agentPool");
        svcMock.SetupGet(x => x.ScheduledWorkerCount).CallBase();
        svcMock.Setup(x => x.UpdateDemand(It.IsAny<int>())).CallBase();
        svcMock.Setup(x => x.UpdateWorkersState(It.IsAny<List<WorkerAgent>>())).CallBase();
        svcMock.Setup(x => x.StartAgent()).Returns(Task.FromResult(new WorkerAgent() {
            Id = "Worker2",
            ProvisioningStart = DateTime.UtcNow
        }));
        
        var svcConcrete = svcMock.Object;
        // Place an existing worker into agents
        await svcConcrete.UpdateWorkersState(new List<WorkerAgent>() {
            new WorkerAgent() {
                Id = "Worker1",
                IsBusy = false,
                ProvisioningStart = DateTime.UtcNow
            }
        });
        
        await svcConcrete.UpdateDemand(1);

        Assert.AreEqual(1, svcConcrete.ScheduledWorkerCount, "Should NOT add new workers if existing is provisioning");
    }


    private string JobDefYaml = $@"
apiVersion: batch/v1
kind: Job
metadata:
  name: custom-job
spec:
  template:
    spec:
      containers:
      - name: custom-job
        image: ghcr.io/akanieski/ado-pipelines-linux:0.0.1-preview
        volumeMounts:
          - mountPath: /var/run/docker.sock
            name: docker-volume
        resources:
          requests:
            memory: ""1000Mi""
            cpu: ""1000m""
          limits:
            memory: ""2000Mi""
            cpu: ""2000m""
        env:
          - name: AZP_URL
            value: https://dev.azure.com/devops-collab
          - name: AZP_TOKEN
            value: some_token
          - name: AZP_POOL
            value: Default
      restartPolicy: Never
      volumes:
        - name: docker-volume
          hostPath:
            path: /var/run/docker.sock
";
}