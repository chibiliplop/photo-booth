using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Photobooth.Admin;
using Photobooth.Core.Options;
using Xunit;

namespace Photobooth.Tests;

public sealed class PrinterControlTests
{
    private static PrinterControl Build(FakeProcessRunner runner, string queue = "photobooth-printer") =>
        new(runner, Options.Create(new PrinterOptions { Name = queue, LpCommand = "lp" }));

    [Fact]
    public async Task Enable_uses_sudo_cupsenable_on_the_queue()
    {
        var runner = new FakeProcessRunner();
        await Build(runner).EnableAsync();

        var call = runner.Calls.Single();
        Assert.Equal("sudo", call.File);
        Assert.Equal(new[] { "cupsenable", "photobooth-printer" }, call.Args);
    }

    [Fact]
    public async Task TestPrint_sends_to_the_queue_with_stdin()
    {
        var runner = new FakeProcessRunner();
        await Build(runner).TestPrintAsync();

        var call = runner.Calls.Single();
        Assert.Equal("lp", call.File);
        Assert.Equal(new[] { "-d", "photobooth-printer" }, call.Args);
        Assert.False(string.IsNullOrEmpty(call.Stdin));
    }

    [Fact]
    public async Task DetectUsb_uses_sudo_lpinfo_v()
    {
        var runner = new FakeProcessRunner();
        await Build(runner).DetectUsbAsync();

        var call = runner.Calls.Single();
        Assert.Equal("sudo", call.File);
        Assert.Equal(new[] { "lpinfo", "-v" }, call.Args);
    }

    [Fact]
    public async Task Status_parses_enabled_and_accepting()
    {
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0,
                "printer photobooth-printer is idle.  enabled since ...\n" +
                "photobooth-printer accepting requests since ...", "", false)
        };
        var d = await Build(runner).StatusAsync();

        Assert.True(d.Enabled);
        Assert.True(d.Accepting);
        Assert.Contains("photobooth-printer", d.Raw);
    }

    [Fact]
    public async Task Status_detects_disabled_and_rejecting()
    {
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0,
                "printer photobooth-printer disabled since ...\n" +
                "photobooth-printer not accepting requests", "", false)
        };
        var d = await Build(runner).StatusAsync();

        Assert.False(d.Enabled);
        Assert.False(d.Accepting);
    }
}
