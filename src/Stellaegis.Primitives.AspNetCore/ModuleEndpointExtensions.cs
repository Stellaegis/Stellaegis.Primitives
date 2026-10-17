using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>What a module declares about the routes it maps.</summary>
public static class ModuleEndpointExtensions
{
    /// <summary>
    /// Declares that the endpoints of <paramref name="group"/> belong to <typeparamref name="TModule"/>. A module
    /// calls it on the group it maps onto, whether or not it has compartments:
    /// <code>
    /// public static RouteGroupBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder routes)
    /// {
    ///     var group = routes.MapGroup("/documents").BelongsTo&lt;DocumentsModule&gt;();
    ///     // ...
    /// }
    /// </code>
    /// </summary>
    /// <remarks>
    /// Every route labelled with an <see cref="Operation"/> belongs to a module: the startup check fails on one
    /// that does not, and on two module types carrying the same name. The declaration closest to a route counts.
    /// </remarks>
    /// <returns><paramref name="group"/>.</returns>
    /// <exception cref="ArgumentException">The name of <typeparamref name="TModule"/> is not a valid module name.</exception>
    /// <exception cref="InvalidOperationException">The startup check is not registered.</exception>
    public static RouteGroupBuilder BelongsTo<TModule>(this RouteGroupBuilder group)
        where TModule : IStellaegisModule
    {
        Guard.NotNull(group);
        var name = TModule.Name;
        if (!Slug.IsValid(name))
        {
            throw new ArgumentException(
                $"'{name}', the name of {typeof(TModule).Name}, is not a valid module name: {Slug.Rule}.",
                nameof(TModule));
        }

        IEndpointRouteBuilder routes = group;
        if (routes.ServiceProvider.GetService<IServiceProviderIsService>() is { } services
            && !services.IsService(typeof(EndpointStartupCheck)))
        {
            throw new InvalidOperationException(
                $"The endpoints of the {name} module are checked at startup, and the check is not registered. "
                + "Register it before mapping them: builder.Services.AddStellaegisStartupCheck(), "
                + "or AddStellaegisRequestContext(...), which registers it too.");
        }
        return group.WithMetadata(new ModuleMetadata(typeof(TModule), name));
    }
}

/// <summary>The metadata <see cref="ModuleEndpointExtensions.BelongsTo{TModule}"/> places on a route.</summary>
internal sealed record ModuleMetadata(Type Type, string Name);
