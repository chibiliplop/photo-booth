using Photobooth.Admin;
using Serilog;
using Xunit;

namespace Photobooth.Tests;

public class InMemoryLogSinkTests
{
    [Fact]
    public void Emitted_events_are_buffered_with_level_and_message()
    {
        var sink = new InMemoryLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        logger.Information("hello {Name}", "world");

        var snap = sink.Snapshot();
        Assert.Single(snap);
        Assert.Equal("Information", snap[0].Level);
        Assert.Contains("world", snap[0].Message);
        Assert.Null(snap[0].Exception);
    }

    [Fact]
    public void Exception_is_captured_as_text()
    {
        var sink = new InMemoryLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        logger.Error(new System.InvalidOperationException("kaboom"), "print broke");

        var snap = sink.Snapshot();
        Assert.Single(snap);
        Assert.NotNull(snap[0].Exception);
        Assert.Contains("kaboom", snap[0].Exception!);
    }

    [Fact]
    public void Buffer_keeps_only_the_most_recent_entries()
    {
        var sink = new InMemoryLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        for (var i = 0; i < InMemoryLogSink.Capacity + 50; i++)
            logger.Information("line {N}", i);

        var snap = sink.Snapshot();
        Assert.Equal(InMemoryLogSink.Capacity, snap.Count);
        Assert.Contains((InMemoryLogSink.Capacity + 49).ToString(), snap[^1].Message); // le plus récent est conservé
    }

    [Fact]
    public void Emitted_event_fires_for_each_log()
    {
        var sink = new InMemoryLogSink();
        var received = 0;
        sink.Emitted += _ => received++;
        using (var logger = new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger())
            logger.Information("ping");
        Assert.Equal(1, received);
    }
}
