namespace MouseKeyProxy.Integration.Tests;

/// <summary>
/// Lab/VM two-machine harness placeholder. Structural gate for PLAN-MKP-006 Integration project.
/// </summary>
public class IntegrationHarnessTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void Integration_Project_Exists_And_References_Common_Commands()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var proj = Path.Combine(root, "tests", "MouseKeyProxy.Integration", "MouseKeyProxy.Integration.csproj");
        Assert.True(File.Exists(proj));
        var text = File.ReadAllText(proj);
        Assert.Contains("MouseKeyProxy.Commands", text);
        Assert.Contains("MouseKeyProxy.Common", text);
    }
}