using Microsoft.Extensions.DependencyInjection;

namespace Stellaegis.Primitives.DependencyInjection;

/// <summary>Reads an application's frame against what its modules declare.</summary>
public static class StellaegisServiceProviderExtensions
{
    /// <summary>
    /// Confronts the registered frame with the registered modules, and fails naming every module at fault
    /// (<see cref="ApplicationFrame.Confront"/>). An application without ASP.NET Core calls it once, right after
    /// building its services and before anything runs; an ASP.NET Core host calls it at startup by itself.
    /// <code>
    /// var services = new ServiceCollection().AddStellaegisFrame(AppFrame.Value)/* ... */.BuildServiceProvider();
    /// services.ConfrontStellaegisModules();
    /// </code>
    /// </summary>
    /// <remarks>
    /// A capability or a parameter registered for a module that nothing declares fails first: it means the
    /// module's own registration is missing, and the confrontation would otherwise not see the module at all.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The frame and the modules do not hold together.</exception>
    public static void ConfrontStellaegisModules(this IServiceProvider services)
    {
        Guard.NotNull(services);
        var frame = services.GetService<ApplicationFrame>() ?? ApplicationFrame.WithoutCompartments;
        var contributions = services.GetServices<ModuleContribution>().ToList();

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

        frame.Confront(contributions
            .GroupBy(contribution => contribution.Module)
            .SelectMany(group => group
                .Where(contribution => contribution.Declaration is not null)
                .Select(contribution => Complete(contribution.Declaration!, group))));
    }

    // Every declaration of a module receives the capabilities and parameters registered for it: a module declared
    // twice is then refused by the confrontation, with the others' faults.
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
