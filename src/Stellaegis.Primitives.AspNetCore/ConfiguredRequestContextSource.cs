using Microsoft.AspNetCore.Http;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// The request context source read from what a <see cref="RequestContextBuilder"/> declared, each source once
/// per request.
/// </summary>
internal sealed class ConfiguredRequestContextSource(IHttpContextAccessor accessor, RequestContextSettings settings)
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
                _compartment = settings.Compartment(RequestContext.Current(accessor));
                _compartmentRead = true;
            }
            return _compartment;
        }
    }

    public Actor Actor => _actor ??= settings.Actor(RequestContext.Current(accessor)) ?? settings.Anonymous;
}
