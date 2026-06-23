using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Photobooth.Admin;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Diagnostics;
using Photobooth.Core.Options;
using Photobooth.Core.Workflow;
using Serilog;
using Xunit;

namespace Photobooth.Tests;

public sealed class AdminEndpointsTests
{
    // Imprimante de test minimale (lecture de IsEnabled uniquement).
    private sealed class StubPrinter : IPrinterAdapter
    {
        public bool IsEnabled { get; init; }
        public System.Threading.Tasks.Task PrintAsync(byte[] imageData, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    // Monte un WebApplication en mémoire (TestServer) avec les fakes fournis, puis MapApi.
    private static WebApplication BuildApp(BoothTelemetry tel, InMemoryLogSink sink, bool printerEnabled)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(tel);
        builder.Services.AddSingleton(sink);
        builder.Services.AddSingleton<IPrinterAdapter>(new StubPrinter { IsEnabled = printerEnabled });
        builder.Services.AddSingleton(Options.Create(new PrinterOptions { Type = "cups" }));
        var app = builder.Build();
        AdminEndpoints.MapApi(app);
        return app;
    }

    [Fact]
    public async Task Status_reports_state_printer_and_last_print()
    {
        var tel = new BoothTelemetry();
        tel.RecordState(BoothState.Degraded);
        tel.RecordGoProReachable(true);
        tel.RecordPrintFailure("lp failed with exit code 1: unknown destination");
        var sink = new InMemoryLogSink();

        await using var app = BuildApp(tel, sink, printerEnabled: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        var status = await client.GetFromJsonAsync<AdminStatus>("/api/status");

        Assert.NotNull(status);
        Assert.Equal("Degraded", status!.State);
        Assert.Equal(true, status.GoProReachable);
        Assert.True(status.Printer.Enabled);
        Assert.Equal("cups", status.Printer.Type);
        Assert.NotNull(status.LastPrint);
        Assert.False(status.LastPrint!.Succeeded);
        Assert.Contains("unknown destination", status.LastPrint.Reason);
    }

    [Fact]
    public async Task Logs_returns_buffered_lines()
    {
        var tel = new BoothTelemetry();
        var sink = new InMemoryLogSink();
        using (var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger())
            logger.Information("hello {Who}", "admin");

        await using var app = BuildApp(tel, sink, printerEnabled: false);
        await app.StartAsync();
        var client = app.GetTestClient();

        var lines = await client.GetFromJsonAsync<LogLine[]>("/api/logs");

        Assert.NotNull(lines);
        Assert.Single(lines!);
        Assert.Contains("admin", lines![0].Message);
    }
}
