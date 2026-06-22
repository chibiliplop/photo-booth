using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Options;

namespace Photobooth.Adapters.Printing;

public sealed class FilePrinterAdapter : IPrinterAdapter
{
    private readonly PrinterOptions _options;
    private readonly ILogger<FilePrinterAdapter> _log;

    public FilePrinterAdapter(IOptions<PrinterOptions> options, ILogger<FilePrinterAdapter> log)
    {
        _options = options.Value;
        _log = log;
    }

    public bool IsEnabled => true;

    public async Task PrintAsync(byte[] imageData, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_options.OutputPath);
        var fileName = $"print-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.jpg";
        var path = Path.Combine(_options.OutputPath, fileName);
        await File.WriteAllBytesAsync(path, imageData, ct);
        _log.LogInformation("Photo exported for printing: {Path}", Path.GetFullPath(path));
    }
}
