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

    [Fact]
    [Trait("Category", "ReleaseContract")]
    public void Apache_2_License_And_GitVersion_Metadata_Are_Configured()
    {
        var license = File.ReadAllText(Path.Combine(RepoRoot, "LICENSE"));
        Assert.Contains("Apache License", license);
        Assert.Contains("Version 2.0", license);
        Assert.Contains("Copyright 2026 SharpNinja", license);

        var readme = File.ReadAllText(Path.Combine(RepoRoot, "README.md"));
        Assert.Contains("Apache-2.0", readme);

        var props = File.ReadAllText(Path.Combine(RepoRoot, "Directory.Build.props"));
        Assert.Contains("<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>", props);
        Assert.Contains("GitVersion.MsBuild", props);

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
}
