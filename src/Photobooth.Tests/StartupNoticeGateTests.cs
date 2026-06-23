using Photobooth.App;
using Xunit;

namespace Photobooth.Tests;

public sealed class StartupNoticeGateTests
{
    [Fact]
    public void Not_pending_by_default()
    {
        Assert.False(new StartupNoticeGate().Pending);
    }

    [Fact]
    public void Arm_then_first_press_is_consumed_once()
    {
        var gate = new StartupNoticeGate();
        gate.Arm();
        Assert.True(gate.Pending);
        Assert.True(gate.ConsumePress());   // 1er appui ferme l_ecran
        Assert.False(gate.Pending);
        Assert.False(gate.ConsumePress());  // appuis suivants = capture normale
    }

    [Fact]
    public void Consume_without_arm_returns_false()
    {
        Assert.False(new StartupNoticeGate().ConsumePress());
    }
}
