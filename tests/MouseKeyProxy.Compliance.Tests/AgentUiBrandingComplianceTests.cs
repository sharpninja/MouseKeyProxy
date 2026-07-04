using System.Buffers.Binary;

namespace MouseKeyProxy.Compliance.Tests;

public class AgentUiBrandingComplianceTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    [Trait("Category", "Branding")]
    public void TEST_MKP_016_Primary_Logo_Is_Inspectable_Hacker_Mouse_Brand_Asset()
    {
        var logoPath = Path.Combine(RepoRoot, "assets", "logo.png");
        Assert.True(File.Exists(logoPath), "assets/logo.png missing");

        var (width, height) = ReadPngDimensions(logoPath);
        Assert.True(width >= 512 && height >= 512,
            $"assets/logo.png is only {width}x{height}; the hacker mouse workstation brand asset must be at least 512x512.");

        var size = new FileInfo(logoPath).Length;
        Assert.True(size >= 4096,
            $"assets/logo.png is only {size} bytes; the brand asset is too small to be an inspectable hacker mouse workstation render.");

        var contractPath = Path.Combine(RepoRoot, "assets", "logo.branding.md");
        Assert.True(File.Exists(contractPath), "assets/logo.branding.md missing");
        var contract = File.ReadAllText(contractPath);
        foreach (var required in new[] { "hacker mouse", "keyboard", "desk", "monitors", "typing" })
        {
            Assert.Contains(required, contract, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    [Trait("Category", "WireframeUI")]
    public void TEST_MKP_015_Agent_UI_Is_Dashboard_Not_Placeholder_Menu()
    {
        var sourcePath = Path.Combine(RepoRoot, "src", "MouseKeyProxy.Agent", "Program.cs");
        var source = File.ReadAllText(sourcePath);

        foreach (var forbidden in new[]
        {
            "peer-via-repl",
            "Remote A",
            "Connected: (pair via REPL)",
            "Console.WriteLine(\"[TRAY]"
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var required in new[]
        {
            "MouseKeyProxy dashboard",
            "Pairing",
            "Active peer",
            "Service",
            "Clipboard",
            "Recent errors",
            "Emergency release",
            "Reconnect",
            "Open logs"
        })
        {
            Assert.Contains(required, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 24, "PNG is too small to contain an IHDR chunk.");
        var signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        Assert.True(bytes.Take(signature.Length).SequenceEqual(signature), "assets/logo.png is not a PNG file.");
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        return (width, height);
    }
}
