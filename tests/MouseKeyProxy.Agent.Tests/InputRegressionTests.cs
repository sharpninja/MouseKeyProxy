using MouseKeyProxy.Agent;
using MouseKeyProxy.Common;

namespace MouseKeyProxy.Agent.Tests;

public class InputRegressionTests
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    [Fact]
    [Trait("Category", "InputRegression")]
    public void AltSpace_Chord_Builds_One_Ordered_Keyboard_Batch()
    {
        var descriptors = Win32InputInjector.BuildInputDescriptors(new[]
        {
            new InputEvent(InputKind.KEY_DOWN, Vk: 0x12, Scan: 0x38),
            new InputEvent(InputKind.KEY_DOWN, Vk: 0x20, Scan: 0x39),
            new InputEvent(InputKind.KEY_UP, Vk: 0x20, Scan: 0x39),
            new InputEvent(InputKind.KEY_UP, Vk: 0x12, Scan: 0x38)
        });

        Assert.Equal(4, descriptors.Count);
        Assert.All(descriptors, d => Assert.Equal(INPUT_KEYBOARD, d.Type));
        Assert.Equal(0x38, descriptors[0].Scan);
        Assert.Equal(0x39, descriptors[1].Scan);
        Assert.Equal(KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, descriptors[2].Flags);
        Assert.Equal(KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, descriptors[3].Flags);
    }

    [Fact]
    [Trait("Category", "InputRegression")]
    public void WinArrow_Chord_Preserves_Extended_Arrow_And_KeyUp_Flags()
    {
        var descriptors = Win32InputInjector.BuildInputDescriptors(new[]
        {
            new InputEvent(InputKind.KEY_DOWN, Vk: ModifierReleasePolicy.VK_LWIN),
            new InputEvent(InputKind.KEY_DOWN, Vk: 0x25, Scan: 0x4B, Flags: 0x01),
            new InputEvent(InputKind.KEY_UP, Vk: 0x25, Scan: 0x4B, Flags: 0x01),
            new InputEvent(InputKind.KEY_UP, Vk: ModifierReleasePolicy.VK_LWIN)
        });

        Assert.Equal(KEYEVENTF_EXTENDEDKEY, descriptors[0].Flags);
        Assert.Equal(KEYEVENTF_SCANCODE | KEYEVENTF_EXTENDEDKEY, descriptors[1].Flags);
        Assert.Equal(KEYEVENTF_SCANCODE | KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, descriptors[2].Flags);
        Assert.Equal(KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, descriptors[3].Flags);
    }
}