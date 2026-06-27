using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Photobooth.Adapters.GoPro;
using Photobooth.Core.Workflow;
using Xunit;

namespace Photobooth.Tests;

public class WorkflowTests
{
    private static FakeGoProClient NewFake() =>
        new(new[] { new byte[] { 1, 2, 3 } }, NullLogger<FakeGoProClient>.Instance);

    [Fact]
    public async Task Photo_capture_runs_countdown_shows_photo_and_returns_to_idle()
    {
        var rig = TestHarness.Build(NewFake());
        await rig.Workflow.StartAsync();
        try
        {
            rig.Workflow.Submit(new BoothCommand.PhotoRequested());

            Assert.True(await TestHarness.WaitForAsync(
                () => rig.Display.PhotoCount >= 1 && rig.Workflow.State == BoothState.Idle));

            Assert.True(rig.Display.SawMessage("Prenez la pose"));
            Assert.True(rig.Display.SawMessage("3"));
            Assert.True(rig.Display.SawMessage("Souriez"));
            Assert.False(rig.Light.IsOn); // light must be OFF at the end
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }

    [Fact]
    public async Task Photo_capture_downloads_the_new_file_never_a_stale_one()
    {
        var scripted = new ScriptedGoProClient();
        var rig = TestHarness.Build(scripted);
        await rig.Workflow.StartAsync();
        try
        {
            rig.Workflow.Submit(new BoothCommand.PhotoRequested());

            Assert.True(await TestHarness.WaitForAsync(
                () => scripted.LastDownloadedFile != null && rig.Workflow.State == BoothState.Idle));

            // The new file produced by the trigger — not the pre-existing OLD0001.JPG, and never the .MP4.
            Assert.Equal("NEW0001.JPG", scripted.LastDownloadedFile);
            Assert.Equal("100GOPRO", scripted.LastDownloadedDir);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }

    [Fact]
    public async Task No_new_media_before_deadline_enters_Degraded()
    {
        var scripted = new ScriptedGoProClient { NeverProduceNewMedia = true };
        var rig = TestHarness.Build(scripted, tuneGoPro: g => g.CaptureDeadlineSeconds = 1);
        await rig.Workflow.StartAsync();
        try
        {
            rig.Workflow.Submit(new BoothCommand.PhotoRequested());

            Assert.True(await TestHarness.WaitForAsync(
                () => rig.Workflow.State == BoothState.Degraded, 5000));
            Assert.Equal("GoPro indisponible", rig.Display.Status);
            Assert.False(rig.Light.IsOn);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }

    [Fact]
    public async Task GoPro_absent_enters_Degraded_without_leaving_light_on()
    {
        var scripted = new ScriptedGoProClient { ThrowOnList = true };
        var rig = TestHarness.Build(scripted);
        await rig.Workflow.StartAsync();
        try
        {
            rig.Workflow.Submit(new BoothCommand.PhotoRequested());

            Assert.True(await TestHarness.WaitForAsync(
                () => rig.Workflow.State == BoothState.Degraded, 5000));
            Assert.False(rig.Light.IsOn);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }

    [Fact]
    public async Task Slideshow_prepares_next_photo_before_visible_tick()
    {
        var scripted = new ScriptedGoProClient();
        var rig = TestHarness.Build(scripted, tuneTimings: t => t.SlideshowIntervalSeconds = 1);
        await rig.Workflow.StartAsync();
        try
        {
            Assert.True(await TestHarness.WaitForAsync(
                () => rig.Display.PreparedShowCount >= 1 && rig.Display.PreparedPhotoCount >= 2,
                4000));
            Assert.Equal(1, rig.Display.PhotoCount);
            Assert.True(rig.Display.PreparedPhotoCount > rig.Display.PreparedShowCount);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }

    [Fact]
    public async Task Degraded_auto_recovers_when_GoPro_returns()
    {
        var scripted = new ScriptedGoProClient { ThrowOnList = true };
        var rig = TestHarness.Build(scripted, tuneTimings: t => t.SlideshowIntervalSeconds = 1);
        await rig.Workflow.StartAsync();
        try
        {
            rig.Workflow.Submit(new BoothCommand.PhotoRequested());
            Assert.True(await TestHarness.WaitForAsync(
                () => rig.Workflow.State == BoothState.Degraded, 5000));

            scripted.ThrowOnList = false; // camera comes back

            Assert.True(await TestHarness.WaitForAsync(
                () => rig.Workflow.State == BoothState.Idle, 6000));
            Assert.Null(rig.Display.Status);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }

    [Fact]
    public async Task Video_toggle_starts_and_stops_recording()
    {
        var rig = TestHarness.Build(NewFake(), tuneTimings: t => t.VideoMaxSeconds = 60);
        await rig.Workflow.StartAsync();
        try
        {
            rig.Workflow.Submit(new BoothCommand.VideoToggleRequested());
            Assert.True(await TestHarness.WaitForAsync(() => rig.Workflow.State == BoothState.Recording));
            Assert.True(rig.Display.Recording);

            rig.Workflow.Submit(new BoothCommand.VideoToggleRequested());
            Assert.True(await TestHarness.WaitForAsync(() => rig.Workflow.State == BoothState.Idle));
            Assert.False(rig.Display.Recording);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }

    [Fact]
    public async Task Video_runs_clapperboard_count_in_then_records_with_take_length()
    {
        var rig = TestHarness.Build(NewFake(), tuneTimings: t => { t.VideoMaxSeconds = 30; t.VideoCountdownSeconds = 3; });
        await rig.Workflow.StartAsync();
        try
        {
            rig.Workflow.Submit(new BoothCommand.VideoToggleRequested());
            Assert.True(await TestHarness.WaitForAsync(() => rig.Workflow.State == BoothState.Recording));

            Assert.Equal(new[] { 3, 2, 1 }, rig.Display.VideoCountdowns); // count-in beats, in order
            Assert.Equal(30, rig.Display.RecordingTotalSeconds);          // UI gets the take length for its countdown
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }

    [Fact]
    public async Task Double_press_during_count_in_does_not_immediately_stop_the_take()
    {
        var rig = TestHarness.Build(NewFake(), tuneTimings: t => { t.VideoMaxSeconds = 30; t.VideoCountdownSeconds = 3; t.CountdownStepMs = 80; });
        await rig.Workflow.StartAsync();
        try
        {
            rig.Workflow.Submit(new BoothCommand.VideoToggleRequested());
            Assert.True(await TestHarness.WaitForAsync(() => rig.Workflow.State == BoothState.Capturing));

            rig.Workflow.Submit(new BoothCommand.VideoToggleRequested()); // mashed during the count-in -> must be ignored

            Assert.True(await TestHarness.WaitForAsync(() => rig.Workflow.State == BoothState.Recording));
            await Task.Delay(150); // give a stray stop a chance to (wrongly) fire
            Assert.Equal(BoothState.Recording, rig.Workflow.State); // still filming
            Assert.True(rig.Display.Recording);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }

    [Fact]
    public async Task Video_auto_stops_after_max_seconds()
    {
        var rig = TestHarness.Build(NewFake(), tuneTimings: t => t.VideoMaxSeconds = 1);
        await rig.Workflow.StartAsync();
        try
        {
            rig.Workflow.Submit(new BoothCommand.VideoToggleRequested());
            Assert.True(await TestHarness.WaitForAsync(() => rig.Workflow.State == BoothState.Recording));

            Assert.True(await TestHarness.WaitForAsync(() => rig.Workflow.State == BoothState.Idle, 4000));
            Assert.False(rig.Display.Recording);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }

    [Fact]
    public async Task Double_press_during_capture_yields_exactly_one_photo()
    {
        var fake = NewFake();
        var rig = TestHarness.Build(fake, tuneTimings: t => t.PhotoDisplayMs = 300);
        await rig.Workflow.StartAsync();
        try
        {
            rig.Workflow.Submit(new BoothCommand.PhotoRequested());
            Assert.True(await TestHarness.WaitForAsync(() => rig.Workflow.State == BoothState.Capturing));

            rig.Workflow.Submit(new BoothCommand.PhotoRequested()); // arrives mid-sequence -> must be ignored

            Assert.True(await TestHarness.WaitForAsync(() => rig.Workflow.State == BoothState.Idle, 5000));
            await Task.Delay(200); // ensure no second capture sneaks in

            var media = await fake.ListMediaAsync();
            Assert.Equal(4, media.Media[0].FileSystem.Count); // 3 seeded + exactly 1 captured
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }

    [Fact]
    public async Task Print_command_prints_last_captured_photo_when_enabled()
    {
        var rig = TestHarness.Build(NewFake(), printerEnabled: true);
        await rig.Workflow.StartAsync();
        try
        {
            rig.Workflow.Submit(new BoothCommand.PhotoRequested());
            Assert.True(await TestHarness.WaitForAsync(() => rig.Display.PhotoCount >= 1 && rig.Workflow.State == BoothState.Idle));

            rig.Workflow.Submit(new BoothCommand.PrintRequested());

            Assert.True(await TestHarness.WaitForAsync(() => rig.Printer.PrintCount == 1));
            Assert.NotNull(rig.Printer.LastPrinted);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }

    [Fact]
    public async Task Photo_button_print_window_prints_instead_of_recapturing()
    {
        var scripted = new ScriptedGoProClient();
        var rig = TestHarness.Build(
            scripted,
            printerEnabled: true,
            tunePrinter: p =>
            {
                p.Type = "file";
                p.TriggerMode = "photo-button-window";
                p.PhotoButtonPrintWindowSeconds = 15;
            });
        await rig.Workflow.StartAsync();
        try
        {
            rig.Workflow.Submit(new BoothCommand.PhotoRequested());
            Assert.True(await TestHarness.WaitForAsync(() => rig.Display.PhotoCount >= 1 && rig.Workflow.State == BoothState.Idle));
            Assert.Equal(1, scripted.TriggerCount);

            rig.Workflow.Submit(new BoothCommand.PhotoRequested());

            Assert.True(await TestHarness.WaitForAsync(() => rig.Printer.PrintCount == 1));
            Assert.Equal(1, scripted.TriggerCount);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }

    [Fact]
    public async Task Photo_button_print_window_drops_represses_queued_during_print()
    {
        var scripted = new ScriptedGoProClient();
        var rig = TestHarness.Build(
            scripted,
            printerEnabled: true,
            tunePrinter: p =>
            {
                p.Type = "file";
                p.TriggerMode = "photo-button-window";
                p.PhotoButtonPrintWindowSeconds = 15;
            });
        rig.Printer.DelayMs = 150;

        await rig.Workflow.StartAsync();
        try
        {
            rig.Workflow.Submit(new BoothCommand.PhotoRequested());
            Assert.True(await TestHarness.WaitForAsync(() => rig.Display.PhotoCount >= 1 && rig.Workflow.State == BoothState.Idle));
            Assert.Equal(1, scripted.TriggerCount);

            rig.Workflow.Submit(new BoothCommand.PhotoRequested());
            Assert.True(await TestHarness.WaitForAsync(() => rig.Printer.PrintStartedCount == 1));

            rig.Workflow.Submit(new BoothCommand.PhotoRequested());

            Assert.True(await TestHarness.WaitForAsync(() => rig.Printer.PrintCount == 1));
            await Task.Delay(250);
            Assert.Equal(1, scripted.TriggerCount);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }

    [Fact]
    public async Task Auto_print_submits_job_after_capture()
    {
        var rig = TestHarness.Build(
            NewFake(),
            printerEnabled: true,
            tunePrinter: p =>
            {
                p.Type = "file";
                p.TriggerMode = "auto";
            });
        await rig.Workflow.StartAsync();
        try
        {
            rig.Workflow.Submit(new BoothCommand.PhotoRequested());

            Assert.True(await TestHarness.WaitForAsync(() => rig.Printer.PrintCount == 1));
            Assert.NotNull(rig.Printer.LastPrinted);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }
}
