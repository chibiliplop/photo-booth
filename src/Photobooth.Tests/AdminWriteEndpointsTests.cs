using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Photobooth.Admin;
using Photobooth.Core.Options;
using Photobooth.Core.Workflow;
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

    private sealed class RecordingSink : Photobooth.Core.Workflow.IBoothCommandSink
    {
        public int Count { get; private set; }
        public void Submit(Photobooth.Core.Workflow.BoothCommand command) => Count++;
    }

    [Fact]
    public async Task Recover_gopro_submits_a_recovered_command()
    {
        var sink = new RecordingSink();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<Photobooth.Core.Workflow.IBoothCommandSink>(sink);
        builder.Services.AddSingleton(new PrivilegedActions(new FakeProcessRunner(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PrivilegedActions>.Instance));
        var app = builder.Build();
        AdminEndpoints.MapActions(app);
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.PostAsync("/api/actions/recover-gopro", null);
        res.EnsureSuccessStatusCode();
        Assert.Equal(1, sink.Count);
    }

    private static (WebApplication app, string path) BuildConfigApp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pb-cfg-ep-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "photobooth.json");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var runner = new FakeProcessRunner();
        builder.Services.AddSingleton(new ConfigStore(new AdminConfigTarget(path), runner,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigStore>.Instance));
        builder.Services.AddSingleton(new PrivilegedActions(runner,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PrivilegedActions>.Instance));
        var app = builder.Build();
        AdminEndpoints.MapConfig(app);
        return (app, path);
    }

    [Fact]
    public async Task Put_invalid_config_is_rejected_400()
    {
        var (app, _) = BuildConfigApp();
        await using var _app = app;
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.PutAsync("/api/config",
            new StringContent("{ \"Printer\": { \"Type\": \"cups\", \"Copies\": 0 } }"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Put_valid_config_writes_and_requests_restart()
    {
        var (app, path) = BuildConfigApp();
        await using var _app = app;
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.PutAsync("/api/config",
            new StringContent("{ \"Printer\": { \"Copies\": 3 } }"));
        res.EnsureSuccessStatusCode();
        Assert.True(File.Exists(path));
        Assert.Contains("Copies", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Get_config_returns_json_content_type_and_empty_object_when_absent()
    {
        var (app, _) = BuildConfigApp();
        await using var _app = app;
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.GetAsync("/api/config");
        res.EnsureSuccessStatusCode();
        Assert.Equal("application/json", res.Content.Headers.ContentType?.MediaType);
        Assert.Equal("{}", (await res.Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task Post_console_runs_command_and_returns_output()
    {
        var runner = new FakeProcessRunner { Result = new ProcessResult(0, "pong", "", false) };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new ConsoleService(runner,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConsoleService>.Instance));
        var app = builder.Build();
        AdminEndpoints.MapConsole(app);
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.PostAsJsonAsync("/api/console", new ConsoleRequest("echo pong"));
        res.EnsureSuccessStatusCode();
        var result = await res.Content.ReadFromJsonAsync<ProcessResult>();
        Assert.NotNull(result);
        Assert.Contains("pong", result!.Stdout);
        Assert.Equal("echo pong", runner.Calls.Single().Args[0]);
    }

    [Fact]
    public async Task Post_console_rejects_empty_command_400()
    {
        var runner = new FakeProcessRunner();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new ConsoleService(runner,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConsoleService>.Instance));
        var app = builder.Build();
        AdminEndpoints.MapConsole(app);
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.PostAsJsonAsync("/api/console", new ConsoleRequest("   "));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
