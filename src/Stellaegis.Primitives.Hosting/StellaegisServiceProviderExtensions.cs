using Microsoft.Extensions.DependencyInjection;

namespace Stellaegis.Primitives.Hosting;

/// <summary>Reads an application's frame and what its modules declare, and validates the two.</summary>
public static class StellaegisServiceProviderExtensions
{
    /// <summary>
    /// The registered frame, or <see cref="ApplicationFrame.WithoutCompartments"/> when the application registers
    /// none.
    /// </summary>
    public static ApplicationFrame GetStellaegisFrame(this IServiceProvider services)
        => Guard.NotNull(services).GetService<ApplicationFrame>() ?? ApplicationFrame.WithoutCompartments;

    /// <summary>
    /// What each registered module declares, completed with the capabilities its adapter registers and the
    /// parameters the application chooses. A module registered twice appears twice, so that the validation
    /// refuses it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A capability or a parameter is registered for a module that nothing declares: the module's own registration
    /// is missing, and nothing would otherwise see the module at all.
    /// </exception>
    public static IReadOnlyList<ModuleDeclaration> GetStellaegisModules(this IServiceProvider services)
    {
        var contributions = Guard.NotNull(services).GetServices<ModuleContribution>().ToList();

        var undeclared = contributions
            .GroupBy(contribution => contribution.Module)
            .Where(group => group.All(contribution => contribution.Declaration is null))
            .ToList();
        if (undeclared.Count > 0)
        {
            var described = undeclared.Select(group =>
                $"{group.First().Name} ({string.Join(", ", group.Select(Describe))})");
            throw new InvalidOperationException(
                $"Capabilities or parameters are registered for modules nothing declares: {string.Join("; ", described)}. "
                + "Register each module itself, with AddStellaegisModule(ModuleDeclaration.Compartmented<TModule>()) "
                + "or ModuleDeclaration.WithoutCompartments<TModule>() — usually the module's own registration does.");
        }

        return contributions
            .GroupBy(contribution => contribution.Module)
            .SelectMany(group => group
                .Where(contribution => contribution.Declaration is not null)
                .Select(contribution => Complete(contribution.Declaration!, group)))
            .ToList();
    }

    /// <summary>
    /// The declaration of <typeparamref name="TModule"/>, completed with the capabilities and parameters registered
    /// for it, or <see langword="null"/> when nothing declares it. A module reads there the parameters the
    /// application turned on:
    /// <code>
    /// var inherits = services.GetStellaegisModule&lt;MessagingModule&gt;()?.Parameters.Contains(InheritSharedSpace) == true;
    /// </code>
    /// </summary>
    /// <exception cref="InvalidOperationException">As <see cref="GetStellaegisModules"/>.</exception>
    public static ModuleDeclaration? GetStellaegisModule<TModule>(this IServiceProvider services)
        where TModule : IStellaegisModule
        => services.GetStellaegisModules().SingleOrDefault(module => module.Module == typeof(TModule));

    /// <summary>
    /// Validates the registered frame against the registered modules, and fails naming every module at fault
    /// (<see cref="ApplicationFrame.Validate"/>). An application without ASP.NET Core calls it once, right after
    /// building its services and before anything runs; an ASP.NET Core host calls it at startup by itself.
    /// <code>
    /// var services = new ServiceCollection().AddStellaegisFrame(AppFrame.Value)/* ... */.BuildServiceProvider();
    /// services.ValidateStellaegisModules();
    /// </code>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The frame and the modules do not hold together, or the compartment directory does not hold with the frame:
    /// one registered by the application beside the frame's single compartment, or in an application without
    /// compartments.
    /// </exception>
    public static void ValidateStellaegisModules(this IServiceProvider services)
    {
        var frame = services.GetStellaegisFrame();
        frame.Validate(services.GetStellaegisModules());
        CheckCompartmentDirectory(services, frame);
    }

    // The directory says which compartments exist, and so does the frame: the two cannot disagree.
    private static void CheckCompartmentDirectory(IServiceProvider services, ApplicationFrame frame)
    {
        var directories = services.GetServices<ICompartmentDirectory>().ToList();
        switch (frame.Compartments)
        {
            case CompartmentCount.None when directories.Count > 0:
                throw new InvalidOperationException(
                    $"A compartment directory is registered, and the application frame ({frame}) has no compartments to list: "
                    + "remove it, or declare the application's compartments.");
            case CompartmentCount.Single when directories.Count > 1 || directories[0] is not FixedCompartmentDirectory:
                throw new InvalidOperationException(
                    $"A compartment directory is registered beside the one of the application frame ({frame}): with a single "
                    + "compartment the frame lists it itself, and nothing else does. Remove the registration, or declare "
                    + "ApplicationFrame.ManyCompartments() and keep the directory of the application.");
            case CompartmentCount.Many when directories.Count > 1:
                throw new InvalidOperationException(
                    $"{directories.Count} compartment directories are registered: an application lists its compartments once.");
        }
    }

    // Every declaration of a module receives the capabilities and parameters registered for it.
    private static ModuleDeclaration Complete(ModuleDeclaration declaration, IEnumerable<ModuleContribution> contributions)
    {
        foreach (var contribution in contributions)
        {
            if (contribution.Capability is { } capability)
                declaration = declaration.WithCapability(capability);
            if (contribution.Parameter is { } parameter)
                declaration = declaration.WithParameter(parameter);
        }
        return declaration;
    }

    private static string Describe(ModuleContribution contribution)
        => contribution.Capability is { } capability ? $"capability {capability}" : $"parameter {contribution.Parameter}";
}
