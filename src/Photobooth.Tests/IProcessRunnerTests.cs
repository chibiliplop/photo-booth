using System;
using System.Threading.Tasks;
using Photobooth.Admin;
using Xunit;

namespace Photobooth.Tests;

public sealed class IProcessRunnerTests
{
    private static readonly IProcessRunner Runner = new ProcessRunner();

    [Fact]
    public async Task Captures_stdout_and_zero_exit()
    {
        var r = await Runner.RunAsync("/bin/echo", new[] { "hello" });
        Assert.Equal(0, r.ExitCode);
        Assert.False(r.TimedOut);
        Assert.Contains("hello", r.Stdout);
    }

    [Fact]
    public async Task Propagates_nonzero_exit_code()
    {
        var r = await Runner.RunAsync("/bin/sh", new[] { "-c", "exit 3" });
        Assert.Equal(3, r.ExitCode);
        Assert.False(r.TimedOut);
    }

    [Fact]
    public async Task Writes_stdin_to_the_process()
    {
        var r = await Runner.RunAsync("/bin/cat", Array.Empty<string>(), stdin: "abc");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("abc", r.Stdout);
    }

    [Fact]
    public async Task Kills_on_timeout_and_flags_it()
    {
        var r = await Runner.RunAsync("/bin/sleep", new[] { "5" }, timeout: TimeSpan.FromMilliseconds(200));
        Assert.True(r.TimedOut);
    }

    [Fact]
    public async Task RunShell_executes_a_command_line()
    {
        var r = await Runner.RunShellAsync("echo from-shell");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("from-shell", r.Stdout);
    }
}
