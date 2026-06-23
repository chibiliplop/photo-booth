using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Photobooth.Admin;
using Serilog;
using Xunit;

namespace Photobooth.Tests;

public sealed class LogStreamTests
{
    [Fact]
    public async Task Stream_emits_the_initial_snapshot_as_sse()
    {
        var sink = new InMemoryLogSink();
        using (var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger())
            logger.Information("snapshot {Who}", "live");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(sink);
        await using var app = builder.Build();
        AdminEndpoints.MapLogStream(app);
        await app.StartAsync();
        var client = app.GetTestClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var res = await client.GetAsync("/api/logs/stream", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal("text/event-stream", res.Content.Headers.ContentType?.MediaType);

        await using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
        var buffer = new byte[4096];
        var read = await stream.ReadAsync(buffer, cts.Token);
        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        Assert.Contains("live", text);
    }
}
