using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Photobooth.Admin;
using Xunit;

namespace Photobooth.Tests;

public sealed class AdminCsrfTests
{
    private static WebApplication BuildApp(string csrf)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        AdminEndpoints.UseCsrf(app, csrf);
        AdminEndpoints.MapCsrf(app, csrf);
        app.MapPost("/api/echo", () => Results.Json(new { ok = true }));
        return app;
    }

    [Fact]
    public async Task Mutating_request_without_csrf_header_is_rejected()
    {
        await using var app = BuildApp("CSRFTOKEN");
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.PostAsync("/api/echo", new StringContent(""));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Mutating_request_with_correct_csrf_header_passes()
    {
        await using var app = BuildApp("CSRFTOKEN");
        await app.StartAsync();
        var client = app.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/echo") { Content = new StringContent("") };
        req.Headers.Add("X-Admin-CSRF", "CSRFTOKEN");
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Csrf_endpoint_returns_the_token()
    {
        await using var app = BuildApp("CSRFTOKEN");
        await app.StartAsync();
        var client = app.GetTestClient();

        var json = await client.GetStringAsync("/api/csrf");
        Assert.Contains("CSRFTOKEN", json);
    }
}
