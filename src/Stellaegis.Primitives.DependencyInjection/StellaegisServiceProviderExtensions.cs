using Microsoft.Extensions.DependencyInjection;

namespace Stellaegis.Primitives.DependencyInjection;

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
    /// Validates the registered frame against the registered modules, and fails naming every module at fault
    /// (<see cref="ApplicationFrame.Validate"/>). An application without ASP.NET Core calls it once, right after
    /// building its services and before anything runs; an ASP.NET Core host calls it at startup by itself.
    /// <code>
    /// var services = new ServiceCollection().AddStellaegisFrame(AppFrame.Value)/* ... */.BuildServiceProvider();
    /// services.ValidateStellaegisModules();
    /// </code>
    /// </summary>
    /// <exception cref="InvalidOperationException">The frame and the modules do not hold together.</exception>
    public static void ValidateStellaegisModules(this IServiceProvider services)
        => services.GetStellaegisFrame().Validate(services.GetStellaegisModules());

    /// <summary>The former name of <see cref="ValidateStellaegisModules"/>, kept for 4.0 code.</summary>
    [Obsolete("Use ValidateStellaegisModules, its new name.")]
    public static void ConfrontStellaegisModules(this IServiceProvider services) => services.ValidateStellaegisModules();

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
