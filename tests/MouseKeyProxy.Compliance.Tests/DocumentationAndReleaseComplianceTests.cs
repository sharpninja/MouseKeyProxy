namespace MouseKeyProxy.Compliance.Tests;

public class DocumentationAndReleaseComplianceTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    [Trait("Category", "Documentation")]
    public void User_And_Security_Admin_Guides_Are_Registered_In_Wiki_Manifest()
    {
        var userGuide = Path.Combine(RepoRoot, "docs", "USER-GUIDE.md");
        var securityGuide = Path.Combine(RepoRoot, "docs", "SECURITY-ADMIN-GUIDE.md");
        Assert.True(File.Exists(userGuide), "docs/USER-GUIDE.md missing");
        Assert.True(File.Exists(securityGuide), "docs/SECURITY-ADMIN-GUIDE.md missing");

        var userGuideText = File.ReadAllText(userGuide);
        Assert.Contains("Emergency Release", userGuideText);
        Assert.Contains("Windows Event Log", userGuideText);
        Assert.Contains("The CLI/REPL is the canonical implementation", userGuideText);

        var securityGuideText = File.ReadAllText(securityGuide);
        Assert.Contains("Trust Boundaries", securityGuideText);
        Assert.Contains("Exclusive Input Control", securityGuideText);
        Assert.Contains("NUGET_API_KEY", securityGuideText);

        var wiki = File.ReadAllText(Path.Combine(RepoRoot, "wiki.yaml"));
        Assert.Contains("docs/USER-GUIDE.md", wiki);
        Assert.Contains("User-Guide.md", wiki);
        Assert.Contains("docs/SECURITY-ADMIN-GUIDE.md", wiki);
        Assert.Contains("Security-Administration-Guide.md", wiki);
    }

    /// <summary>
    /// Release contract: project LICENSE is source-available with commercial royalty
    /// reservation; NuGet metadata uses PackageLicenseFile; GitVersion tooling is wired.
    /// </summary>
    [Fact]
    [Trait("Category", "ReleaseContract")]
    public void Project_License_Royalty_And_GitVersion_Metadata_Are_Configured()
    {
        var license = File.ReadAllText(Path.Combine(RepoRoot, "LICENSE"));
        Assert.Contains("MouseKeyProxy License", license);
        Assert.Contains("Commercial Use", license);
        Assert.Contains("royalty agreement", license, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ninja@thesharp.ninja", license);
        Assert.Contains("Copyright (c) 2026 SharpNinja", license);

        var readme = File.ReadAllText(Path.Combine(RepoRoot, "README.md"));
        Assert.Contains("royalty agreement", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ninja@thesharp.ninja", readme);

        var props = File.ReadAllText(Path.Combine(RepoRoot, "Directory.Build.props"));
        Assert.Contains("<PackageLicenseFile>LICENSE</PackageLicenseFile>", props);
        Assert.Contains("<PackageRequireLicenseAcceptance>true</PackageRequireLicenseAcceptance>", props);
        Assert.Contains("GitVersion.MsBuild", props);

        var replProject = File.ReadAllText(Path.Combine(RepoRoot, "src", "MouseKeyProxy.Repl", "MouseKeyProxy.Repl.csproj"));
        Assert.Contains(@"LICENSE"" Pack=""true""", replProject);

        Assert.True(File.Exists(Path.Combine(RepoRoot, "GitVersion.yml")), "GitVersion.yml missing");
        var tools = File.ReadAllText(Path.Combine(RepoRoot, ".config", "dotnet-tools.json"));
        Assert.Contains("gitversion.tool", tools, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "ReleaseContract")]
    public void Repl_Package_Publishes_Payloads_With_GitVersion_Properties()
    {
        var replProject = File.ReadAllText(Path.Combine(RepoRoot, "src", "MouseKeyProxy.Repl", "MouseKeyProxy.Repl.csproj"));
        Assert.Contains("PayloadVersionProperties", replProject);
        Assert.Contains("-p:Version=$(Version)", replProject);
        Assert.Contains("-p:PackageVersion=$(PackageVersion)", replProject);
        Assert.Contains("-p:AssemblyVersion=$(AssemblyVersion)", replProject);
        Assert.Contains("-p:FileVersion=$(FileVersion)", replProject);

        var replProgram = File.ReadAllText(Path.Combine(RepoRoot, "src", "MouseKeyProxy.Repl", "Program.cs"));
        Assert.Contains("--version", replProgram);
        Assert.Contains("AssemblyInformationalVersionAttribute", replProgram);
    }
    [Fact]
    [Trait("Category", "ReleaseContract")]
    public void Azure_Pipeline_Uses_Default_Pool_And_Nuke_Tool_Publish_Targets()
    {
        var pipeline = File.ReadAllText(Path.Combine(RepoRoot, "azure-pipelines.yml"));
        Assert.Contains("name: Default", pipeline);
        Assert.Contains("--target Test", pipeline);
        Assert.Contains("--target PackRepl", pipeline);
        Assert.Contains("--target PublishToolToNuGet", pipeline);
        Assert.Contains("NUGET_API_KEY: $(NUGET_API_KEY)", pipeline);
        Assert.Contains("fetchDepth: 0", pipeline);
        Assert.Contains("fetchTags: true", pipeline);
        Assert.Contains("refs/tags/v", pipeline);
    }
}
