using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Photobooth.Admin;

/// <summary>
/// Resout les URLs d'admin a afficher a l'ecran au demarrage (http://ip:port).
/// </summary>
public static class AdminAddress
{
    /// <summary>
    /// Pur/testable : ne garde que les IPv4, exclut le loopback (127.0.0.0/8), formate en
    /// http://ip:port et deduplique. Pas de tri ni de priorisation.
    /// </summary>
    public static IReadOnlyList<string> BuildUrls(IEnumerable<IPAddress> addresses, int port)
    {
        return addresses
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork) // IPv4 uniquement
            .Where(a => !IPAddress.IsLoopback(a))
            .Select(a => a.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ip => $"http://{ip}:{port}")
            .ToList();
    }

    /// <summary>
    /// Enumere les interfaces reelles (Up, hors loopback) puis delegue a <see cref="BuildUrls"/>.
    /// Best-effort : toute exception -> liste vide (le mode debug ne doit jamais casser la borne).
    /// </summary>
    public static IReadOnlyList<string> LocalUrls(int port)
    {
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Select(u => u.Address);
            return BuildUrls(addresses, port);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
