using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Photobooth.Admin;

namespace Photobooth.Tests;

/// <summary>Faux IProcessRunner : enregistre les appels, renvoie un résultat configurable.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<(string File, string[] Args, string? Stdin)> Calls { get; } = new();
    public ProcessResult Result { get; set; } = new(0, "", "", false);
    public Func<string, string[], ProcessResult>? OnRun { get; set; }

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
        string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var args = arguments.ToArray();
        Calls.Add((fileName, args, stdin));
        return Task.FromResult(OnRun?.Invoke(fileName, args) ?? Result);
    }

    public Task<ProcessResult> RunShellAsync(string command,
        string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        Calls.Add(("shell", new[] { command }, stdin));
        return Task.FromResult(OnRun?.Invoke("shell", new[] { command }) ?? Result);
    }
}
