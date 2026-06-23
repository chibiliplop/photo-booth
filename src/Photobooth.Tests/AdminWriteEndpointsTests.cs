using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Photobooth.Admin;
using Photobooth.Core.Options;
using Xunit;

namespace Photobooth.Tests;

public sealed class AdminWriteEndpointsTests
{
    private static WebApplication BuildPrinterApp(FakeProcessRunner runner)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IProcessRunner>(runner);
        builder.Services.AddSingleton(new PrinterControl(runner,
            Options.Create(new PrinterOptions { Name = "photobooth-printer", LpCommand = "lp" })));
        var app = builder.Build();
        AdminEndpoints.MapPrinter(app);
        return app;
    }

    [Fact]
    public async Task Get_printer_returns_detail()
    {
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, "printer photobooth-printer is idle. enabled since x\nphotobooth-printer accepting requests", "", false)
        };
        await using var app = BuildPrinterApp(runner);
        await app.StartAsync();
        var client = app.GetTestClient();

        var d = await client.GetFromJsonAsync<PrinterDetail>("/api/printer");
        Assert.NotNull(d);
        Assert.True(d!.Enabled);
    }

    [Fact]
    public async Task Post_enable_invokes_sudo_cupsenable()
    {
        var runner = new FakeProcessRunner();
        await using var app = BuildPrinterApp(runner);
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.PostAsync("/api/printer/enable", null);
        res.EnsureSuccessStatusCode();
        Assert.Contains(runner.Calls, c => c.File == "sudo" && c.Args.Length == 2 && c.Args[0] == "cupsenable");
    }
}
