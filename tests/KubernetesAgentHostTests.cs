namespace ADOAgentOrchestrator.Tests;

[TestClass]
public class KubernetesAgentHostTests
{
    [TestMethod]
    public void RequireKubeConfig()
    {
        var config = new ConfigurationBuilder().Build();
        Assert.ThrowsException<ArgumentNullException>(() => {
            var _ =  new KubernetesAgentHostService(config, string.Empty);
        });
    }
}