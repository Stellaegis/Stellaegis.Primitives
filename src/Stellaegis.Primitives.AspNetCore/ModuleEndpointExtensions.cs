using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
    /// A controller declares its module with <see cref="BelongsToAttribute{TModule}"/> instead.
    /// </remarks>
    /// <returns><paramref name="group"/>.</returns>
    /// <exception cref="ArgumentException">The name of <typeparamref name="TModule"/> is not a valid module name.</exception>
    /// <exception cref="InvalidOperationException">The startup check is not registered.</exception>
    public static RouteGroupBuilder BelongsTo<TModule>(this RouteGroupBuilder group)
        where TModule : IStellaegisModule
    {
        Guard.NotNull(group);
        var module = new BelongsToAttribute<TModule>();

        IEndpointRouteBuilder routes = group;
        if (routes.ServiceProvider.GetService<IServiceProviderIsService>() is { } services
            && !services.IsService(typeof(EndpointStartupCheck)))
        {
            throw new InvalidOperationException(
                $"The endpoints of the {module.Name} module are checked at startup, and the check is not registered. "
                + "Register it before mapping them: builder.Services.AddStellaegisStartupCheck(), "
                + "or AddStellaegisRequestContext(...), which registers it too.");
        }
        return group.WithMetadata(module);
    }

    /// <summary>The module a route belongs to — the declaration closest to it —, or <see langword="null"/>.</summary>
    internal static ModuleMetadata? GetModule(this Endpoint endpoint)
        => endpoint.Metadata.GetMetadata<IModuleMetadata>() is { } module ? new ModuleMetadata(module.Type, module.Name) : null;
}

/// <summary>
/// Declares that the actions of a controller, or one action, belong to <typeparamref name="TModule"/> — what
/// <see cref="ModuleEndpointExtensions.BelongsTo{TModule}"/> declares on a group of routes:
/// <code>
/// [ApiController, Route("notes"), BelongsTo&lt;NotesModule&gt;]
/// public sealed class NotesController : ControllerBase { /* ... */ }
/// </code>
/// </summary>
/// <remarks>
/// The host maps every controller with a single <c>MapControllers()</c>, and each carries its own module. The
/// declaration is checked by the startup check, which <c>AddStellaegisStartupCheck()</c> or
/// <c>AddStellaegisRequestContext()</c> registers: without it, nothing reads the attribute.
/// </remarks>
/// <exception cref="ArgumentException">The name of <typeparamref name="TModule"/> is not a valid module name.</exception>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class BelongsToAttribute<TModule> : Attribute, IModuleMetadata
    where TModule : IStellaegisModule
{
    /// <summary>The module type, which carries its identity.</summary>
    public Type Type { get; } = typeof(TModule);

    /// <summary>The module name.</summary>
    public string Name { get; } = NameRules.ModuleNameOf<TModule>();
}

/// <summary>What a route carries to say which module it belongs to, from a group or from an attribute.</summary>
internal interface IModuleMetadata
{
    Type Type { get; }

    string Name { get; }
}

/// <summary>The module of a route, compared by value.</summary>
internal sealed record ModuleMetadata(Type Type, string Name);
