using System.Threading;
using System.Threading.Tasks;

namespace Photobooth.Core.Abstractions;

/// <summary>Prints an encoded JPEG photo received from the GoPro.</summary>
public interface IPrinterAdapter
{
    bool IsEnabled { get; }

    Task PrintAsync(byte[] imageData, CancellationToken ct = default);
}
