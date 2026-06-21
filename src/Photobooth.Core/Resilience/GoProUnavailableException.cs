using System;

namespace Photobooth.Core.Resilience;

/// <summary>
/// Thrown when the GoPro cannot satisfy a request within the bounded retry budget:
/// either it is unreachable, or it kept returning 503 / empty media past the deadline.
/// The workflow treats this as a transition into the Degraded state — never as "show stale photo".
/// </summary>
public sealed class GoProUnavailableException : Exception
{
    public GoProUnavailableException(string message) : base(message) { }
    public GoProUnavailableException(string message, Exception inner) : base(message, inner) { }
}
