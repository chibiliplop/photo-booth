using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Photobooth.Admin;
using Xunit;

namespace Photobooth.Tests;

public sealed class PrivilegedActionsTests
{
    [Fact]
    public async Task Restart_uses_sudo_systemctl_restart_photobooth()
    {
        var runner = new FakeProcessRunner();
        var pa = new PrivilegedActions(runner, NullLogger<PrivilegedActions>.Instance);

        await pa.RestartAppAsync();

        var call = runner.Calls.Single();
        Assert.Equal("sudo", call.File);
        Assert.Equal(new[] { "systemctl", "restart", "photobooth" }, call.Args);
    }

    [Fact]
    public async Task Reboot_uses_sudo_systemctl_reboot()
    {
        var runner = new FakeProcessRunner();
        var pa = new PrivilegedActions(runner, NullLogger<PrivilegedActions>.Instance);

        await pa.RebootAsync();

        Assert.Equal(new[] { "systemctl", "reboot" }, runner.Calls.Single().Args);
    }
}
