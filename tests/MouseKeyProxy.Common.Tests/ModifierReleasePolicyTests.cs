using MouseKeyProxy.Common;

namespace MouseKeyProxy.Common.Tests;

public class ModifierReleasePolicyTests
{
    [Fact]
    [Trait("Category", "ModifierCleanup")]
    [Trait("Category", "InputRegression")]
    public void CreateKeyUpEvents_Includes_Left_Right_And_Generic_Modifiers()
    {
        var releases = ModifierReleasePolicy.CreateKeyUpEvents(tsMs: 1234);
        var vks = releases.Select(e => e.Vk).ToArray();

        Assert.All(releases, e =>
        {
            Assert.Equal(InputKind.KEY_UP, e.Kind);
            Assert.Equal(1234UL, e.TsMs);
        });
        Assert.Contains(ModifierReleasePolicy.VK_SHIFT, vks);
        Assert.Contains(ModifierReleasePolicy.VK_LSHIFT, vks);
        Assert.Contains(ModifierReleasePolicy.VK_RSHIFT, vks);
        Assert.Contains(ModifierReleasePolicy.VK_CONTROL, vks);
        Assert.Contains(ModifierReleasePolicy.VK_LCONTROL, vks);
        Assert.Contains(ModifierReleasePolicy.VK_RCONTROL, vks);
        Assert.Contains(ModifierReleasePolicy.VK_MENU, vks);
        Assert.Contains(ModifierReleasePolicy.VK_LMENU, vks);
        Assert.Contains(ModifierReleasePolicy.VK_RMENU, vks);
        Assert.Contains(ModifierReleasePolicy.VK_LWIN, vks);
        Assert.Contains(ModifierReleasePolicy.VK_RWIN, vks);
    }

    [Fact]
    [Trait("Category", "ModifierCleanup")]
    public void CreateKeyUpEvents_Filters_Non_Modifier_Keys()
    {
        var releases = ModifierReleasePolicy.CreateKeyUpEvents(new[] { ModifierReleasePolicy.VK_LWIN, 0x41u, ModifierReleasePolicy.VK_LWIN }, 99);

        var release = Assert.Single(releases);
        Assert.Equal(ModifierReleasePolicy.VK_LWIN, release.Vk);
        Assert.Equal(InputKind.KEY_UP, release.Kind);
    }
}