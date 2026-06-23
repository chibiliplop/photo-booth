using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Diagnostics;
using Photobooth.Core.Options;

namespace Photobooth.Admin;

/// <summary>
/// Hôte web Kestrel embarqué (lecture seule). Démarré seulement si Admin.Enabled. Tout échec de
/// démarrage (bind, port occupé, adresse invalide) est loggé puis avalé : la borne n'est jamais
/// dégradée par le mode debug. Lit BoothTelemetry / InMemoryLogSink ; ne dépend pas du workflow.
/// </summary>
public sealed class AdminWebHost : IAsyncDisposable
{
    private readonly BoothTelemetry _telemetry;
    private readonly InMemoryLogSink _logSink;
    private readonly IPrinterAdapter _printer;
    private readonly IOptions<PrinterOptions> _printerOptions;
    private readonly AdminOptions _opt;
    private readonly ILogger<AdminWebHost> _log;
    private readonly string _authToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private WebApplication? _app;

    public AdminWebHost(
        BoothTelemetry telemetry,
        InMemoryLogSink logSink,
        IPrinterAdapter printer,
        IOptions<PrinterOptions> printerOptions,
        IOptions<AdminOptions> options,
        ILogger<AdminWebHost> log)
    {
        _telemetry = telemetry;
        _logSink = logSink;
        _printer = printer;
        _printerOptions = printerOptions;
        _opt = options.Value;
        _log = log;
    }

    /// <summary>URL effectivement écoutée après StartAsync, ou null si désactivé/échec.</summary>
    public string? BoundUrl { get; private set; }

    public async Task StartAsync()
    {
        if (!_opt.Enabled)
            return;

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders(); // Serilog gère déjà les logs applicatifs.
            builder.Services.AddSingleton(_telemetry);
            builder.Services.AddSingleton(_logSink);
            builder.Services.AddSingleton(_printer);
            builder.Services.AddSingleton(_printerOptions);
            builder.WebHost.UseUrls($"http://{_opt.ListenAddress}:{_opt.Port}");

            var app = builder.Build();
            AdminEndpoints.UseAuth(app, _opt, _authToken);
            AdminEndpoints.MapApi(app);
            AdminEndpoints.MapPage(app);
            await app.StartAsync();

            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
            BoundUrl = addresses?.FirstOrDefault();
            _app = app;
            _log.LogInformation("Hôte admin à l'écoute sur {Url}.", BoundUrl);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Démarrage de l'hôte admin échoué ; la borne continue sans lui.");
            BoundUrl = null;
            _app = null;
        }
    }

    public async Task StopAsync()
    {
        if (_app is null) return;
        var app = _app;
        _app = null;
        BoundUrl = null;
        try
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Arrêt de l'hôte admin échoué (ignoré)."); }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
