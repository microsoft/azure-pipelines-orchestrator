namespace ADOAgentOrchestrator.Tests;



[TestClass]
public class KubernetesAgentHostTests
{
    static string testYaml = $@"
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
    private class MockFileSystem : IFileSystem
    {
        public int CallCount = 0;
        public string ReadAllText(string path) { 
            CallCount++;
            return testYaml;
        }
    }
    [TestMethod]
    public void RequireKubeConfig()
    {
        var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string>() {
            {"JOB_DEFINITION_FILE", "./JobDef.yaml"},
        }.ToList())
        .Build();

        var mockFs = new MockFileSystem();
        var svc =  new KubernetesAgentHostService(config, mockFs, string.Empty);

        Assert.AreEqual(1, mockFs.CallCount, "Should fetch jod definition from disk when provided.");
    }
}