using Microsoft.Extensions.DependencyInjection;

namespace Stellaegis.Primitives.DependencyInjection;

/// <summary>
/// Registers an application's frame and what its modules declare, each by its own author: the application its
/// frame and its parameters, a module its declaration, an adapter the capabilities it holds.
/// <code>
/// builder.Services.AddStellaegisFrame(AppFrame.Value);                                           // the application
/// builder.Services.AddStellaegisModule(ModuleDeclaration.Compartmented&lt;DocumentsModule&gt;());          // the module
/// builder.Services.AddStellaegisModuleCapability&lt;DocumentsModule&gt;(ModuleCapability.OwnerIsolation); // its adapter
/// </code>
/// <see cref="StellaegisServiceProviderExtensions.ConfrontStellaegisModules"/> reads them together.
/// </summary>
public static class StellaegisServiceCollectionExtensions
{
    /// <summary>
    /// Registers the application's frame, readable as an <see cref="ApplicationFrame"/> singleton. Without one,
    /// the application is <see cref="ApplicationFrame.WithoutCompartments"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A frame is already registered.</exception>
    public static IServiceCollection AddStellaegisFrame(this IServiceCollection services, ApplicationFrame frame)
    {
        Guard.NotNull(services);
        Guard.NotNull(frame);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ApplicationFrame)))
        {
            throw new InvalidOperationException(
                "An application frame is already registered: an application declares its frame once, in code its hosts share.");
        }
        services.AddSingleton(frame);
        return services;
    }

    /// <summary>
    /// Registers what a module declares. A module calls it from its own registration, once, whatever the
    /// adapter the application chooses.
    /// </summary>
    public static IServiceCollection AddStellaegisModule(this IServiceCollection services, ModuleDeclaration declaration)
    {
        Guard.NotNull(services);
        Guard.NotNull(declaration);
        services.AddSingleton(new ModuleContribution(declaration.Module, declaration.Name, declaration, null, null));
        return services;
    }

    /// <summary>
    /// Registers a capability of <typeparamref name="TModule"/> that its adapter holds — an isolation by owner,
    /// held by a row-level security rule written for one engine. An adapter calls it from its own registration.
    /// </summary>
    /// <exception cref="ArgumentException">The name of <typeparamref name="TModule"/> is not a valid module name.</exception>
    public static IServiceCollection AddStellaegisModuleCapability<TModule>(this IServiceCollection services, ModuleCapability capability)
        where TModule : IStellaegisModule
    {
        Guard.NotNull(services);
        Guard.NotNull(capability);
        services.AddSingleton(new ModuleContribution(typeof(TModule), NameRules.ModuleNameOf<TModule>(), null, capability, null));
        return services;
    }

    /// <summary>
    /// Turns on a capability of <typeparamref name="TModule"/>, as the application's choice. The confrontation
    /// refuses a parameter outside the module's capabilities.
    /// </summary>
    /// <exception cref="ArgumentException">The name of <typeparamref name="TModule"/> is not a valid module name.</exception>
    public static IServiceCollection AddStellaegisModuleParameter<TModule>(this IServiceCollection services, ModuleCapability parameter)
        where TModule : IStellaegisModule
    {
        Guard.NotNull(services);
        Guard.NotNull(parameter);
        services.AddSingleton(new ModuleContribution(typeof(TModule), NameRules.ModuleNameOf<TModule>(), null, null, parameter));
        return services;
    }
}

/// <summary>One registration about a module: its declaration, a capability of its adapter, or a parameter of the application.</summary>
internal sealed record ModuleContribution(
    Type Module, string Name, ModuleDeclaration? Declaration, ModuleCapability? Capability, ModuleCapability? Parameter);
