using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Photobooth.Core.GoPro;

/// <summary>Root of the GoPro <c>/gp/gpMediaList</c> response. Ported to System.Text.Json.</summary>
public sealed class GoProMedia
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("media")]
    public List<GoProMediaDirectory> Media { get; set; } = new();

    /// <summary>All filenames across all directories (used for snapshot/diff "wait for new photo").</summary>
    public IEnumerable<string> AllFileNames() => Media.SelectMany(m => m.FileSystem).Select(f => f.FileName);
}

public sealed class GoProMediaDirectory
{
    [JsonPropertyName("d")]
    public string Directory { get; set; } = string.Empty;

    [JsonPropertyName("fs")]
    public List<GoProMediaFile> FileSystem { get; set; } = new();
}

public sealed class GoProMediaFile
{
    [JsonPropertyName("n")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("ls")]
    public string? Ls { get; set; }

    [JsonPropertyName("s")]
    public string? Size { get; set; }

    public bool IsVideo => FileName.EndsWith(".MP4", StringComparison.OrdinalIgnoreCase);
}
