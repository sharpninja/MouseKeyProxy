namespace MouseKeyProxy.Compliance.Tests;

public class TestDoubleComplianceTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    [Trait("Category", "DependencyCompliance")]
    public void TEST_MKP_022_Banned_Mocking_Framework_Is_Not_Used()
    {
        var bannedPackage = "M" + "oq";
        var disallowedPatterns = new[]
        {
            bannedPackage,
            "using " + bannedPackage,
            "PackageReference Include=\"" + bannedPackage + "\"",
            "Mock" + "<",
            "new " + "Mock",
            "Mock" + "Behavior",
            "Times" + "."
        };

        var violations = Directory
            .EnumerateFiles(RepoRoot, "*", SearchOption.AllDirectories)
            .Where(IsScannedFile)
            .SelectMany(file => FindViolations(file, disallowedPatterns))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(violations.Length == 0, "Banned test double framework usage found:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static bool IsScannedFile(string file)
    {
        var relative = Path.GetRelativePath(RepoRoot, file);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(part => part is ".git" or "bin" or "obj"))
        {
            return false;
        }

        var extension = Path.GetExtension(file);
        return extension is ".cs" or ".csproj" or ".props" or ".targets"
            || string.Equals(Path.GetFileName(file), "packages.lock.json", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FindViolations(string file, IReadOnlyCollection<string> disallowedPatterns)
    {
        var text = File.ReadAllText(file);
        foreach (var pattern in disallowedPatterns)
        {
            if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{Path.GetRelativePath(RepoRoot, file)} contains {pattern}";
            }
        }
    }
}
