using Microsoft.AspNetCore.Http;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// The only <see cref="IRequestContext"/>: the shared space on a route marked for it, without reading the source;
/// the frame's compartment in an application with a single one, without reading the source either; the compartment
/// of the source in an application with many.
/// </summary>
internal sealed class RequestContext(IHttpContextAccessor accessor, IRequestContextSource source, ApplicationFrame frame)
    : IRequestContext
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
                    : frame.Compartments switch
                    {
                        CompartmentCount.Single => frame.FixedCompartment,
                        CompartmentCount.Many => source.Compartment,
                        _ => throw new InvalidOperationException(
                            "The request context is read in an application without compartments: it has no space to give. "
                            + "Declare the application's compartments with AddStellaegisFrame(...)."),
                    };
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
