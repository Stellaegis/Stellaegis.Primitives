using Microsoft.AspNetCore.Http;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// The request context source read from what a <see cref="RequestContextBuilder"/> declared, each source once
/// per request. Without a compartment source, it resolves no compartment: the frame's single compartment is
/// read by <see cref="RequestContext"/>, never by a source.
/// </summary>
internal sealed class ConfiguredRequestContextSource(IHttpContextAccessor accessor, RequestContextSettings settings, Actor anonymous)
    : IRequestContextSource
{
    private bool _compartmentRead;
    private Compartment? _compartment;
    private Actor? _actor;

    public Compartment? Compartment
    {
        get
        {
            if (!_compartmentRead)
            {
                _compartment = settings.Compartment?.Invoke(RequestContext.Current(accessor));
                _compartmentRead = true;
            }
            return _compartment;
        }
    }

    public Actor Actor => _actor ??= Read();

    private Actor Read()
    {
        var http = RequestContext.Current(accessor);
        var authenticated = http.User.Identity?.IsAuthenticated == true;
        var actor = settings.Actor(http);
        if (authenticated && actor is null)
            throw new InvalidOperationException("The actor source names no actor for the authenticated user: the request would be recorded as nobody's.");
        if (authenticated && string.Equals(actor!.Value, anonymous.Value, StringComparison.Ordinal))
            throw new InvalidOperationException($"The authenticated user is named '{anonymous}', the anonymous actor of the frame: the request would be taken for an anonymous one.");
        actor ??= anonymous;
        return settings.Via?.Invoke(http) is { } via ? actor.Through(via) : actor;
    }
}
