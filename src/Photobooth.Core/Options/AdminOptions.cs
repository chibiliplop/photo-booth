namespace Photobooth.Core.Options;

/// <summary>
/// Configuration de l'interface web d'admin/debug embarquée (section "Admin").
/// Opt-in : tant que <see cref="Enabled"/> est false, aucun hôte web n'écoute (zéro surface d'attaque).
/// </summary>
public sealed class AdminOptions
{
    public const string Section = "Admin";

    /// <summary>Active l'hôte web d'admin. Défaut false (rien n'écoute).</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Interface d'écoute Kestrel. Sur le Pi terrain la seule iface est le WiFi GoPro.</summary>
    public string ListenAddress { get; set; } = "0.0.0.0";

    /// <summary>Port Kestrel sur l'IP du Pi (distinct du 8080 de la GoPro 10.5.5.9).</summary>
    public int Port { get; set; } = 8080;

    /// <summary>PIN d'accès optionnel : vide = pas d'authentification.</summary>
    public string Pin { get; set; } = "";

    public string? Validate()
    {
        if (Port is < 1 or > 65535)
            return "Admin.Port doit etre compris entre 1 et 65535.";
        if (string.IsNullOrWhiteSpace(ListenAddress))
            return "Admin.ListenAddress ne doit pas etre vide (ex: 0.0.0.0).";
        return null;
    }
}
