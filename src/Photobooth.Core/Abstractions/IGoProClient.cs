using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Photobooth.Core.GoPro;

namespace Photobooth.Core.Abstractions;

/// <summary>
/// Neutral, UI-free GoPro contract. Implementations may talk to a real camera,
/// the local Python simulator, or be a no-network fake. Returns raw bytes (never UWP image types).
/// </summary>
public interface IGoProClient
{
    Task SetSinglePhotoModeAsync(CancellationToken ct = default);
    Task SetVideoModeAsync(CancellationToken ct = default);
    Task TriggerAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task<GoProMedia> ListMediaAsync(CancellationToken ct = default);
    Task<byte[]> DownloadMediaAsync(string directory, string fileName, CancellationToken ct = default);

    /// <summary>Send the GoPro UDP keepalive packet (no-op for fakes).</summary>
    Task SendKeepAliveAsync(CancellationToken ct = default);

    /// <summary>Cheap reachability probe; never throws.</summary>
    Task<bool> IsReachableAsync(CancellationToken ct = default);
}
