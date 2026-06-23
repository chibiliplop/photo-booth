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
/// Hôte web Kestrel embarqué. Démarré seulement si Admin.Enabled. Tout échec de démarrage (bind, port
/// occupé, adresse invalide) est loggé puis avalé : la borne n'est jamais dégradée par le mode debug.
/// Récupère les singletons partagés depuis le conteneur racine de l'app et les re-déclare dans le
/// conteneur interne du WebApplication (injection minimal-API). Read-write : auth PIN + CSRF.
/// </summary>
public sealed class AdminWebHost : IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly AdminOptions _opt;
    private readonly ILogger<AdminWebHost> _log;
    private readonly string _authToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private readonly string _csrfToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private WebApplication? _app;

    public AdminWebHost(
        IServiceProvider services,
        IOptions<AdminOptions> options,
        ILogger<AdminWebHost> log)
    {
        _services = services;
        _opt = options.Value;
        _log = log;
    }

    /// <summary>URL effectivement écoutée après StartAsync, ou null si désactivé/échec.</summary>
    public string? BoundUrl { get; private set; }

    public async Task StartAsync()
    {
        if (!_opt.Enabled)
            return;

        if (string.IsNullOrEmpty(_opt.Pin))
            _log.LogWarning(
                "ADMIN ACTIVÉ SANS PIN : surface complète (console root via sudo) exposée sur {Addr}:{Port} " +
                "sans authentification. Définissez Admin.Pin pour fermer cette porte.",
                _opt.ListenAddress, _opt.Port);

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders(); // Serilog gère déjà les logs applicatifs.

            // Forward des singletons partagés (lecture) vers le conteneur de l'hôte.
            Forward<BoothTelemetry>(builder.Services);
            Forward<InMemoryLogSink>(builder.Services);
            Forward<IPrinterAdapter>(builder.Services);
            Forward<IOptions<PrinterOptions>>(builder.Services);
            // Forward write services (Plan 3/3). No-op if absent (degraded mode / tests read-only).
            Forward<PrinterControl>(builder.Services);
            Forward<PrivilegedActions>(builder.Services);
            Forward<ConsoleService>(builder.Services);
            Forward<ConfigStore>(builder.Services);
            Forward<Photobooth.Core.Workflow.IBoothCommandSink>(builder.Services);
            Forward<IProcessRunner>(builder.Services);

            builder.WebHost.UseUrls($"http://{_opt.ListenAddress}:{_opt.Port}");

            var app = builder.Build();
            AdminEndpoints.UseAuth(app, _opt, _authToken);
            AdminEndpoints.UseCsrf(app, _csrfToken);
            AdminEndpoints.MapApi(app);
            AdminEndpoints.MapCsrf(app, _csrfToken);
            // Endpoints mappés inconditionnellement. [FromServices] dans AdminEndpoints empêche
            // l'inférence de body param sur les GETs pour les services non enregistrés ; un service
            // absent surface en 500 à l'appel plutôt que de casser la table de routes.
            AdminEndpoints.MapPrinter(app);
            AdminEndpoints.MapActions(app);
            AdminEndpoints.MapConfig(app);
            AdminEndpoints.MapConsole(app);
            AdminEndpoints.MapLogStream(app);
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

    // Récupère un singleton du conteneur racine et le re-déclare dans l'hôte.
    // No-op si le service est absent (mode dégradé / tests read-only).
    private void Forward<T>(IServiceCollection dst) where T : class
    {
        var instance = _services.GetService<T>();
        if (instance is not null)
            dst.AddSingleton(instance);
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
