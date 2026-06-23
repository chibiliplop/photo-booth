using Photobooth.Core.Diagnostics;
using Photobooth.Core.Workflow;
using Xunit;

namespace Photobooth.Tests;

public class BoothTelemetryTests
{
    [Fact]
    public void LastPrint_is_null_before_any_attempt()
    {
        var telemetry = new BoothTelemetry();
        Assert.Null(telemetry.LastPrint);
    }

    [Fact]
    public void RecordPrintFailure_captures_the_real_reason()
    {
        var telemetry = new BoothTelemetry();
        telemetry.RecordPrintFailure("lp failed with exit code 1: unknown destination");

        Assert.NotNull(telemetry.LastPrint);
        Assert.False(telemetry.LastPrint!.Succeeded);
        Assert.Equal("lp failed with exit code 1: unknown destination", telemetry.LastPrint.Reason);
    }

    [Fact]
    public void RecordPrintSuccess_overwrites_a_previous_failure_and_clears_reason()
    {
        var telemetry = new BoothTelemetry();
        telemetry.RecordPrintFailure("boom");
        telemetry.RecordPrintSuccess();

        Assert.NotNull(telemetry.LastPrint);
        Assert.True(telemetry.LastPrint!.Succeeded);
        Assert.Null(telemetry.LastPrint.Reason);
    }

    [Fact]
    public void State_defaults_to_Idle_and_GoProReachable_is_null()
    {
        var t = new BoothTelemetry();
        Assert.Equal(BoothState.Idle, t.State);
        Assert.Null(t.GoProReachable);
    }

    [Fact]
    public void RecordState_and_RecordGoProReachable_are_reflected()
    {
        var t = new BoothTelemetry();
        t.RecordState(BoothState.Degraded);
        t.RecordGoProReachable(false);

        Assert.Equal(BoothState.Degraded, t.State);
        Assert.Equal(false, t.GoProReachable);
    }
}
