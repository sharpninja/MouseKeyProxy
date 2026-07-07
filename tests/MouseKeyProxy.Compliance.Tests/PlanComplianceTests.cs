using System.Text.RegularExpressions;

namespace MouseKeyProxy.Compliance.Tests;

public class PlanComplianceTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    [Trait("Category", "NukePayload")]
    public void TEST_MKP_009_Nuke_Build_And_Repl_Payloads_Present()
    {
        var buildCs = Path.Combine(RepoRoot, "build", "Build.cs");
        var replCsproj = Path.Combine(RepoRoot, "src", "MouseKeyProxy.Repl", "MouseKeyProxy.Repl.csproj");
        Assert.True(File.Exists(buildCs), "build/Build.cs missing");
        var buildText = File.ReadAllText(buildCs);
        Assert.Contains("PackRepl", buildText);
        Assert.Contains("PublishService", buildText);
        Assert.Contains("PublishAgent", buildText);
        var replText = File.ReadAllText(replCsproj);
        Assert.Contains("payloads", replText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "WireframeUI")]
    public void TEST_MKP_010_Wireframes_Assets_And_Menu_Spec_Present()
    {
        var wireDir = Path.Combine(RepoRoot, "docs", "wireframes");
        foreach (var name in new[] { "01-tray-icon-menu.svg", "02-inject-form.svg", "04-status.svg" })
        {
            Assert.True(File.Exists(Path.Combine(wireDir, name)), $"missing wireframe {name}");
        }
        var logo = Path.Combine(RepoRoot, "assets", "logo.png");
        Assert.True(File.Exists(logo), "assets/logo.png missing");
        var agentSrc = File.ReadAllText(Path.Combine(RepoRoot, "src", "MouseKeyProxy.Agent", "Program.cs"));
        Assert.Contains("Toggle Active", agentSrc);
        Assert.Contains("Inject Text to Remote", agentSrc);
        Assert.Contains("Emergency release", agentSrc);
        Assert.DoesNotContain("Start Mirror Mode", agentSrc);
        Assert.DoesNotContain("ShowMirrorForm", agentSrc);
        Assert.False(File.Exists(Path.Combine(wireDir, "03-mirror-mode.svg")), "mirror mode wireframe should be removed");
        Assert.DoesNotContain("SystemIcons.Application", agentSrc);
    }

    [Fact]
    [Trait("Category", "EmergencyRelease")]
    public void TEST_MKP_013_Emergency_Release_Rpc_Contract_Present()
    {
        var proto = File.ReadAllText(Path.Combine(RepoRoot, "src", "MouseKeyProxy.Network", "mousekeyproxy.proto"));

        Assert.Contains("rpc EmergencyRelease (EmergencyReleaseRequest) returns (CommandResult);", proto);
        Assert.Contains("message EmergencyReleaseRequest", proto);
    }

    [Fact]
    [Trait("Category", "MCPCompliance")]
    public void TEST_MKP_011_Mcp_Artifacts_And_Verify_Script_Present()
    {
        var verify = Path.Combine(RepoRoot, "scripts", "verify-goal.ps1");
        Assert.True(File.Exists(verify));
        var text = File.ReadAllText(verify);
        Assert.Contains("VERIFICATION SCRIPT COMPLETE", text);
        Assert.Contains("full-test-output.log", text);
        Assert.Contains("repl-run.log", text);
        Assert.Contains("toggle", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("git-visibility.log", text);
        Assert.Contains("repl-install.log", text);
        Assert.Contains("dotnet tool install", text);
        Assert.Contains("git diff", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("git status", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("build.log", text);
        Assert.Contains("repl-run.log", text);
        Assert.Contains("full-test-output.log", text);
    }

    [Fact]
    [Trait("Category", "HarnessContract")]
    public void TEST_MKP_012_Visibility_Script_And_Error_Path_Contract()
    {
        var transportSrc = File.ReadAllText(Path.Combine(RepoRoot, "src", "MouseKeyProxy.Commands", "BidiSessionTransport.cs"));
        Assert.Contains("SentFrames", transportSrc);
        var replSrc = File.ReadAllText(Path.Combine(RepoRoot, "src", "MouseKeyProxy.Repl", "Program.cs"));
        Assert.Contains("toggle FAILED", replSrc);
        Assert.Matches(new Regex(@"nullTransport|null!"), replSrc);
        var verify = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "verify-goal.ps1"));
        Assert.Contains("test-", verify);
        Assert.Contains("verif-", verify);
    }
}
