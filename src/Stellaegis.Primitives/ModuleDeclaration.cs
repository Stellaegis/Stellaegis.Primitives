using System.Collections.Immutable;

namespace Stellaegis.Primitives;

/// <summary>
/// What a module declares to the application that assembles it: whether it has compartments, the capabilities it
/// has, and the parameters the assembly chooses among them. Read against the <see cref="ApplicationFrame"/> by
/// <see cref="ApplicationFrame.Confront"/>, which refuses an assembly that does not hold.
/// <code>
/// var documents = ModuleDeclaration.Compartmented&lt;DocumentsModule&gt;()
///     .WithCapability(ModuleCapability.OwnerIsolation);   // declared by its PostgreSQL adapter
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Whether a module has compartments is always declared, never assumed: a module with compartments that forgot to
/// say so would escape every check the frame makes.
/// </para>
/// <para>
/// A capability may come from the module or from its adapter: a module's core knows its nature — Messaging can
/// inherit the shared space's configuration —, its adapter knows what the database holds — an isolation by owner,
/// written for one engine. A parameter is a capability the assembly turns on; it is a choice, declared and
/// checked, not a mode the module discovers.
/// </para>
/// <para>Immutable: each <c>With…</c> method returns a new declaration.</para>
/// </remarks>
public sealed class ModuleDeclaration
{
    private readonly ImmutableHashSet<ModuleCapability> _capabilities;
    private readonly ImmutableHashSet<ModuleCapability> _parameters;

    private ModuleDeclaration(
        Type module, string name, bool isCompartmented,
        ImmutableHashSet<ModuleCapability> capabilities, ImmutableHashSet<ModuleCapability> parameters)
    {
        Module = module;
        Name = name;
        IsCompartmented = isCompartmented;
        _capabilities = capabilities;
        _parameters = parameters;
    }

    /// <summary>A module with compartments: it acts in a space the application gives it.</summary>
    /// <exception cref="ArgumentException">The name of <typeparamref name="TModule"/> is not a valid module name.</exception>
    public static ModuleDeclaration Compartmented<TModule>() where TModule : IStellaegisModule
        => Create<TModule>(isCompartmented: true);

    /// <summary>A module without compartments: it takes neither a space nor a compartment.</summary>
    /// <exception cref="ArgumentException">The name of <typeparamref name="TModule"/> is not a valid module name.</exception>
    public static ModuleDeclaration WithoutCompartments<TModule>() where TModule : IStellaegisModule
        => Create<TModule>(isCompartmented: false);

    /// <summary>The module type, which carries its identity.</summary>
    public Type Module { get; }

    /// <summary>The module name.</summary>
    public string Name { get; }

    /// <summary>Whether the module has compartments.</summary>
    public bool IsCompartmented { get; }

    /// <summary>What the module can do, as the module and its adapter declare.</summary>
    public IReadOnlySet<ModuleCapability> Capabilities => _capabilities;

    /// <summary>The capabilities the assembly turns on.</summary>
    public IReadOnlySet<ModuleCapability> Parameters => _parameters;

    /// <summary>Declares that the module has <paramref name="capability"/>. Declaring it twice is harmless.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="capability"/> is <see langword="null"/>.</exception>
    public ModuleDeclaration WithCapability(ModuleCapability capability)
        => new(Module, Name, IsCompartmented, _capabilities.Add(Guard.NotNull(capability)), _parameters);

    /// <summary>
    /// Turns <paramref name="capability"/> on, as the assembly's choice. Checked by
    /// <see cref="ApplicationFrame.Confront"/>, which refuses a parameter outside the module's capabilities.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="capability"/> is <see langword="null"/>.</exception>
    public ModuleDeclaration WithParameter(ModuleCapability capability)
        => new(Module, Name, IsCompartmented, _capabilities, _parameters.Add(Guard.NotNull(capability)));

    /// <summary>The module name.</summary>
    public override string ToString() => Name;

    private static ModuleDeclaration Create<TModule>(bool isCompartmented) where TModule : IStellaegisModule
        => new(typeof(TModule), NameRules.ModuleNameOf<TModule>(), isCompartmented, [], []);
}
