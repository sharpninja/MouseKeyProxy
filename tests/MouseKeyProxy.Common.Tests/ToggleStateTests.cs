using MouseKeyProxy.Common;
using Xunit;

namespace MouseKeyProxy.Common.Tests;

public class ToggleStateTests
{
    [Fact]
    [Trait("Category", "Hotkey")]
    public void Toggle_Flips_Active_And_Records_Peer()
    {
        var sm = new ToggleStateMachine();
        var r1 = sm.ApplyToggle("remote1");
        Assert.True(r1.Changed);
        Assert.True(r1.NewActive);
        Assert.Equal("remote1", sm.ActivePeerId);

        var r2 = sm.ApplyToggle("local");
        Assert.True(r2.Changed);
        Assert.False(r2.NewActive);
    }

    [Fact]
    [Trait("Category", "Hotkey")]
    public void NoEdge_Defaults()
    {
        var sm = new ToggleStateMachine();
        Assert.False(sm.IsActive);
    }
}
