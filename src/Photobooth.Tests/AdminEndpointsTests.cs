using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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

    private static WebApplication BuildAppWithAuth(string pin, string token)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new BoothTelemetry());
        builder.Services.AddSingleton(new InMemoryLogSink());
        builder.Services.AddSingleton<IPrinterAdapter>(new StubPrinter { IsEnabled = false });
        builder.Services.AddSingleton(Options.Create(new PrinterOptions()));
        var app = builder.Build();
        AdminEndpoints.UseAuth(app, new AdminOptions { Pin = pin }, token);
        AdminEndpoints.MapApi(app);
        return app;
    }

    [Fact]
    public async Task Pin_gate_accepts_correct_cookie_only()
    {
        await using var app = BuildAppWithAuth(pin: "1234", token: "TESTTOKEN");
        await app.StartAsync();
        var client = app.GetTestClient();

        // Sans cookie -> 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/status")).StatusCode);

        // Mauvais cookie -> 401.
        var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/status");
        wrong.Headers.Add("Cookie", "padmin=WRONG");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(wrong)).StatusCode);

        // Bon cookie (jeton attendu) -> 200.
        var good = new HttpRequestMessage(HttpMethod.Get, "/api/status");
        good.Headers.Add("Cookie", "padmin=TESTTOKEN");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(good)).StatusCode);
    }

    [Fact]
    public async Task Login_sets_cookie_on_correct_pin_only()
    {
        await using var app = BuildAppWithAuth(pin: "1234", token: "TESTTOKEN");
        await app.StartAsync();
        var client = app.GetTestClient(); // handler TestServer : ne suit pas les redirections.

        // Bon PIN -> 302 vers / + Set-Cookie portant le jeton.
        var ok = await client.PostAsync("/login",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["pin"] = "1234" }));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Contains(ok.Headers.GetValues("Set-Cookie"),
            v => v.Contains("padmin=TESTTOKEN") && v.ToLowerInvariant().Contains("httponly"));

        // Mauvais PIN -> aucun Set-Cookie.
        var bad = await client.PostAsync("/login",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["pin"] = "9999" }));
        Assert.False(bad.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Empty_pin_means_no_auth()
    {
        await using var app = BuildAppWithAuth(pin: "", token: "TESTTOKEN");
        await app.StartAsync();
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/status")).StatusCode);
    }

    [Fact]
    public async Task Root_serves_html_page_when_no_pin()
    {
        await using var app = BuildApp(new BoothTelemetry(), new InMemoryLogSink(), printerEnabled: false);
        // BuildApp ne mappe que l'API ; on ajoute la page comme l'hôte réel.
        AdminEndpoints.MapPage(app);
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Borne photo", html);
    }
}
