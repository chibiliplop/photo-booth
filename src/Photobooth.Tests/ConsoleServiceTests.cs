using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Photobooth.Admin;
using Xunit;

namespace Photobooth.Tests;

public sealed class ConsoleServiceTests
{
    [Fact]
    public async Task Run_passes_command_to_the_shell()
    {
        var runner = new FakeProcessRunner { Result = new ProcessResult(0, "hi", "", false) };
        var svc = new ConsoleService(runner, NullLogger<ConsoleService>.Instance);

        var r = await svc.RunAsync("echo hi");

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("shell", runner.Calls.Single().File);
        Assert.Equal("echo hi", runner.Calls.Single().Args[0]);
    }
}
