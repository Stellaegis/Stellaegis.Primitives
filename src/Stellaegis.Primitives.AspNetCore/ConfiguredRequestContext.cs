using Microsoft.AspNetCore.Http;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// The request context read from the sources a <see cref="RequestContextBuilder"/> declared, each one once
/// per request.
/// </summary>
internal sealed class ConfiguredRequestContext(IHttpContextAccessor accessor, RequestContextSources sources) : IRequestContext
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
                _compartment = sources.Compartment(HttpContext);
                _compartmentRead = true;
            }
            return _compartment;
        }
    }

    public Actor Actor => _actor ??= sources.Actor(HttpContext) ?? sources.Anonymous;

    private HttpContext HttpContext => accessor.HttpContext
        ?? throw new InvalidOperationException(
            "The request context is read outside an HTTP request. Work done outside a request takes the compartment "
            + "and the actor as parameters, and a singleton never receives IRequestContext.");
}
