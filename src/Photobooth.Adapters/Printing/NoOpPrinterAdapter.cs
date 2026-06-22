using System.Threading;
using System.Threading.Tasks;
using Photobooth.Core.Abstractions;

namespace Photobooth.Adapters.Printing;

public sealed class NoOpPrinterAdapter : IPrinterAdapter
{
    public bool IsEnabled => false;

    public Task PrintAsync(byte[] imageData, CancellationToken ct = default) => Task.CompletedTask;
}
