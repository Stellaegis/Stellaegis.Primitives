using Microsoft.AspNetCore.Http;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// The only <see cref="IRequestContext"/>: the shared space on a route marked for it, without reading the source;
/// the compartment of the source elsewhere.
/// </summary>
internal sealed class RequestContext(IHttpContextAccessor accessor, IRequestContextSource source) : IRequestContext
{
    private bool _spaceRead;
    private Space? _space;

    public Space? Space
    {
        get
        {
            if (!_spaceRead)
            {
                _space = Current(accessor).GetEndpoint()?.Metadata.GetMetadata<SharedSpaceMetadata>() is not null
                    ? Primitives.Space.Shared
                    : source.Compartment;
                _spaceRead = true;
            }
            return _space;
        }
    }

    public Actor Actor => source.Actor;

    internal static HttpContext Current(IHttpContextAccessor accessor) => accessor.HttpContext
        ?? throw new InvalidOperationException(
            "The request context is read outside an HTTP request. Work done outside a request takes the space "
            + "and the actor as parameters, and a singleton never receives IRequestContext.");
}
