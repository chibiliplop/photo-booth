using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Photobooth.Admin;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Diagnostics;
using Photobooth.Core.Options;
using Xunit;

namespace Photobooth.Tests;

public sealed class AdminWebHostTests
{
    private sealed class StubPrinter : IPrinterAdapter
    {
        public bool IsEnabled => false;
        public Task PrintAsync(byte[] imageData, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
    }

    private static AdminWebHost Build(AdminOptions opt) => new(
        new BoothTelemetry(),
        new InMemoryLogSink(),
        new StubPrinter(),
        Options.Create(new PrinterOptions()),
        Options.Create(opt),
        NullLogger<AdminWebHost>.Instance);

    [Fact]
    public async Task Disabled_does_not_listen()
    {
        await using var host = Build(new AdminOptions { Enabled = false });
        await host.StartAsync();
        Assert.Null(host.BoundUrl);
    }

    [Fact]
    public async Task Enabled_serves_logs_endpoint_on_loopback()
    {
        await using var host = Build(new AdminOptions { Enabled = true, ListenAddress = "127.0.0.1", Port = 0 });
        await host.StartAsync();

        Assert.NotNull(host.BoundUrl);
        using var client = new HttpClient();
        var res = await client.GetAsync($"{host.BoundUrl}/api/logs");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await host.StopAsync();
    }

    [Fact]
    public async Task Bind_failure_is_degraded_not_fatal()
    {
        // Adresse non assignable -> le bind échoue ; StartAsync doit avaler et ne pas lever.
        await using var host = Build(new AdminOptions { Enabled = true, ListenAddress = "203.0.113.1", Port = 8080 });
        await host.StartAsync(); // ne doit pas lever
        Assert.Null(host.BoundUrl);
    }
}
