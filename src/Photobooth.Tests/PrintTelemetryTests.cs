using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Photobooth.Adapters.GoPro;
using Photobooth.Core.Workflow;
using Xunit;

namespace Photobooth.Tests;

public class PrintTelemetryTests
{
    private static FakeGoProClient NewFake() =>
        new(new[] { new byte[] { 1, 2, 3 } }, NullLogger<FakeGoProClient>.Instance);

    [Fact]
    public async Task Print_failure_reason_is_captured_in_telemetry_instead_of_being_swallowed()
    {
        var rig = TestHarness.Build(NewFake(), printerEnabled: true);
        rig.Printer.ThrowOnPrint = true;
        await rig.Workflow.StartAsync();
        try
        {
            // Capturer une photo pour qu'il y ait quelque chose à imprimer.
            rig.Workflow.Submit(new BoothCommand.PhotoRequested());
            Assert.True(await TestHarness.WaitForAsync(
                () => rig.Display.PhotoCount >= 1 && rig.Workflow.State == BoothState.Idle));

            // Demander l'impression : l'imprimante lève, le workflow doit enregistrer la vraie raison.
            rig.Workflow.Submit(new BoothCommand.PrintRequested());
            Assert.True(await TestHarness.WaitForAsync(() => rig.Telemetry.LastPrint is not null));

            Assert.False(rig.Telemetry.LastPrint!.Succeeded);
            Assert.Contains("unknown destination", rig.Telemetry.LastPrint.Reason);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }
}
