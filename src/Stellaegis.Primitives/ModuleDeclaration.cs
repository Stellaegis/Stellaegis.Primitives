using System.Collections.Immutable;

namespace Stellaegis.Primitives;

/// <summary>
/// What a module declares to the application that assembles it: whether it has compartments, the operations it
/// performs, the capabilities it has, and the parameters the assembly chooses among them. Read against the
/// <see cref="ApplicationFrame"/> by <see cref="ApplicationFrame.Validate"/>, which refuses an assembly that does
/// not hold.
/// <code>
/// var documents = ModuleDeclaration.Compartmented&lt;DocumentsModule&gt;()
///     .WithOperations(DocumentOperations.Read, DocumentOperations.Write)   // declared by the module
///     .WithCapability(ModuleCapability.OwnerIsolation);                    // declared by its PostgreSQL adapter
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
/// <para>
/// The operations are the module's: each route of the module is labelled with one of them, and the startup check
/// refuses a route labelled with an operation its module does not declare. An operation with no route is a piece
/// of work done outside requests. The declarations of every module, read together, are the catalogue of what the
/// application can do.
/// </para>
/// <para>Immutable: each <c>With…</c> method returns a new declaration.</para>
/// </remarks>
public sealed class ModuleDeclaration
{
    private readonly ImmutableList<Operation> _operations;
    private readonly ImmutableHashSet<ModuleCapability> _capabilities;
    private readonly ImmutableHashSet<ModuleCapability> _parameters;

    private ModuleDeclaration(
        Type module, string name, bool isCompartmented, ImmutableList<Operation> operations,
        ImmutableHashSet<ModuleCapability> capabilities, ImmutableHashSet<ModuleCapability> parameters)
    {
        Module = module;
        Name = name;
        IsCompartmented = isCompartmented;
        _operations = operations;
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

    /// <summary>The operations the module performs, each with one identity, <c>target.name</c>.</summary>
    public IReadOnlyList<Operation> Operations => _operations;

    /// <summary>What the module can do, as the module and its adapter declare.</summary>
    public IReadOnlySet<ModuleCapability> Capabilities => _capabilities;

    /// <summary>The capabilities the assembly turns on.</summary>
    public IReadOnlySet<ModuleCapability> Parameters => _parameters;

    /// <summary>
    /// Declares the operations the module performs. Declaring the same operation twice is harmless; declaring an
    /// identity twice with two kinds is refused: an identity is the key an application grants, and it names one
    /// nature.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="operations"/>, or one of them, is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An identity is declared with two kinds.</exception>
    public ModuleDeclaration WithOperations(params IEnumerable<Operation> operations)
    {
        var declared = _operations;
        foreach (var operation in Guard.NotNull(operations))
        {
            Guard.NotNull(operation);
            var twin = declared.FirstOrDefault(known => string.Equals(known.Identity, operation.Identity, StringComparison.Ordinal));
            if (twin is null)
                declared = declared.Add(operation);
            else if (twin != operation)
            {
                throw new ArgumentException(
                    $"{Name} declares {operation.Identity} twice, as {twin.Kind} and as {operation.Kind}: an identity names one operation.",
                    nameof(operations));
            }
        }
        return new(Module, Name, IsCompartmented, declared, _capabilities, _parameters);
    }

    /// <summary>Declares that the module has <paramref name="capability"/>. Declaring it twice is harmless.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="capability"/> is <see langword="null"/>.</exception>
    public ModuleDeclaration WithCapability(ModuleCapability capability)
        => new(Module, Name, IsCompartmented, _operations, _capabilities.Add(Guard.NotNull(capability)), _parameters);

    /// <summary>
    /// Turns <paramref name="capability"/> on, as the assembly's choice. Checked by
    /// <see cref="ApplicationFrame.Validate"/>, which refuses a parameter outside the module's capabilities.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="capability"/> is <see langword="null"/>.</exception>
    public ModuleDeclaration WithParameter(ModuleCapability capability)
        => new(Module, Name, IsCompartmented, _operations, _capabilities, _parameters.Add(Guard.NotNull(capability)));

    /// <summary>The module name.</summary>
    public override string ToString() => Name;

    private static ModuleDeclaration Create<TModule>(bool isCompartmented) where TModule : IStellaegisModule
        => new(typeof(TModule), NameRules.ModuleNameOf<TModule>(), isCompartmented, [], [], []);
}
