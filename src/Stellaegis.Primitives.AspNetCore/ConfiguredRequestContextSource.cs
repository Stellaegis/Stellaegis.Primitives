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

    public Actor Actor => _actor ??= settings.Actor(RequestContext.Current(accessor)) ?? anonymous;
}
