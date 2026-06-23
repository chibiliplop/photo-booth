using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Photobooth.Admin;
using Xunit;

namespace Photobooth.Tests;

public sealed class ConfigStoreTests
{
    private static (ConfigStore store, string path) Build()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pb-cfg-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "photobooth.json");
        var store = new ConfigStore(new AdminConfigTarget(path), new FakeProcessRunner(),
            NullLogger<ConfigStore>.Instance);
        return (store, path);
    }

    [Fact]
    public async Task Read_returns_empty_object_when_absent()
    {
        var (store, _) = Build();
        Assert.Equal("{}", (await store.ReadAsync()).Trim());
    }

    [Fact]
    public void Validate_rejects_invalid_printer_section()
    {
        var (store, _) = Build();
        var err = store.Validate("{ \"Printer\": { \"Type\": \"cups\", \"Copies\": 0 } }");
        Assert.NotNull(err);
        Assert.Contains("Copies", err);
    }

    [Fact]
    public void Validate_rejects_malformed_json()
    {
        var (store, _) = Build();
        Assert.NotNull(store.Validate("{ not json"));
    }

    [Fact]
    public void Validate_accepts_a_valid_document()
    {
        var (store, _) = Build();
        Assert.Null(store.Validate("{ \"Printer\": { \"Type\": \"cups\", \"Copies\": 1 } }"));
    }

    [Fact]
    public async Task Write_persists_atomically_to_the_target()
    {
        var (store, path) = Build();
        await store.WriteAsync("{ \"Printer\": { \"Copies\": 2 } }");
        Assert.True(File.Exists(path));
        Assert.Contains("Copies", await File.ReadAllTextAsync(path));
    }
}
