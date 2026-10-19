namespace Stellaegis.Primitives;

/// <summary>How many compartments an application has, as its <see cref="ApplicationFrame"/> declares.</summary>
public enum CompartmentCount
{
    /// <summary>None: the application isolates nothing, and no module it uses has compartments.</summary>
    None,

    /// <summary>One, fixed, named after the organization that owns the data.</summary>
    Single,

    /// <summary>Several, each request or each piece of work naming its own — a tenant read from the route, for instance.</summary>
    Many,
}

/// <summary>
/// What is true of an application's isolation, identical in every host that serves it: how many compartments it
/// has, whether it has a shared space, whether each actor sees only its own data, and how an actor without
/// identity is recorded. Declared once, in code the hosts share, and read by the routes, the work done outside
/// requests and the startup checks alike.
/// <code>
/// public static class AppFrame
/// {
///     public static readonly ApplicationFrame Value = ApplicationFrame
///         .SingleCompartment("acme")
///         .WithOwnerIsolation();
/// }
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The frame is code, not configuration: a configuration diverges from one host to another, a constant does not.
/// A host forgotten when the application takes a second compartment would otherwise serve everyone in the first,
/// without an error.
/// </para>
/// <para>
/// It says nothing of which modules are assembled, nor of who administers what: the first belongs to the
/// dependency injection of each host, the second to the application. An application that isolates nothing needs
/// no frame at all; <see cref="WithoutCompartments"/> says so explicitly.
/// </para>
/// <para>
/// Immutable: each <c>With…</c> method returns a new frame, so a frame shared as a static field cannot be changed
/// by any code holding it.
/// </para>
/// </remarks>
public sealed class ApplicationFrame
{
    private static readonly Actor DefaultAnonymousActor = Actor.Parse("anonymous");

    private ApplicationFrame(
        CompartmentCount compartments, Compartment? fixedCompartment, bool hasSharedSpace, bool ownerIsolation, Actor anonymousActor)
    {
        Compartments = compartments;
        FixedCompartment = fixedCompartment;
        HasSharedSpace = hasSharedSpace;
        OwnerIsolation = ownerIsolation;
        AnonymousActor = anonymousActor;
    }

    /// <summary>
    /// An application without compartments: it isolates nothing, and no module it uses has compartments. Its code
    /// sees neither a space nor a compartment.
    /// </summary>
    public static ApplicationFrame WithoutCompartments { get; } =
        new(CompartmentCount.None, null, hasSharedSpace: false, ownerIsolation: false, DefaultAnonymousActor);

    /// <summary>
    /// An application with a single compartment. Name it after the organization that owns the data, as a real
    /// compartment: the day the application takes a second one, the first keeps its name, and nothing is migrated.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="compartment"/> is <see langword="null"/>.</exception>
    public static ApplicationFrame SingleCompartment(Compartment compartment)
        => new(CompartmentCount.Single, Guard.NotNull(compartment), hasSharedSpace: false, ownerIsolation: false, DefaultAnonymousActor);

    /// <inheritdoc cref="SingleCompartment(Compartment)"/>
    /// <exception cref="FormatException"><paramref name="compartment"/> is not a valid compartment.</exception>
    public static ApplicationFrame SingleCompartment(string compartment)
        => SingleCompartment(Compartment.Parse(Guard.NotNull(compartment)));

    /// <summary>
    /// An application with several compartments, each request or each piece of work naming its own. Where a
    /// request reads it — a route value, a claim — is declared by the host's request context.
    /// </summary>
    public static ApplicationFrame ManyCompartments()
        => new(CompartmentCount.Many, null, hasSharedSpace: false, ownerIsolation: false, DefaultAnonymousActor);

    /// <summary>How many compartments the application has.</summary>
    public CompartmentCount Compartments { get; }

    /// <summary>The single compartment, or <see langword="null"/> unless <see cref="Compartments"/> is <see cref="CompartmentCount.Single"/>.</summary>
    public Compartment? FixedCompartment { get; }

    /// <summary>
    /// Whether the application has a shared space, holding the data that belongs to no compartment. Absent unless
    /// declared, with <see cref="WithSharedSpace"/>. Whether a host maps a module in it is a choice of that host.
    /// </summary>
    public bool HasSharedSpace { get; }

    /// <summary>
    /// Whether each actor sees only the data it owns, within a compartment. The same for every module of the
    /// application: a module that cannot hold it refuses to start.
    /// </summary>
    public bool OwnerIsolation { get; }

    /// <summary>The actor recorded for a request without an authenticated user. Defaults to <c>anonymous</c>.</summary>
    public Actor AnonymousActor { get; }

    /// <summary>Declares that the application has a shared space, besides its compartments.</summary>
    /// <exception cref="InvalidOperationException">The application has no compartments.</exception>
    public ApplicationFrame WithSharedSpace()
    {
        RequireCompartments("a shared space");
        return new(Compartments, FixedCompartment, hasSharedSpace: true, OwnerIsolation, AnonymousActor);
    }

    /// <summary>
    /// Declares that each actor sees only the data it owns, in every module: the documents of a user are invisible
    /// to the other users of the same compartment.
    /// </summary>
    /// <exception cref="InvalidOperationException">The application has no compartments.</exception>
    public ApplicationFrame WithOwnerIsolation()
    {
        RequireCompartments("an isolation by owner");
        return new(Compartments, FixedCompartment, HasSharedSpace, ownerIsolation: true, AnonymousActor);
    }

    /// <summary>Records <paramref name="actor"/> as the actor of a request without an authenticated user.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="actor"/> is <see langword="null"/>.</exception>
    public ApplicationFrame WithAnonymousActor(Actor actor)
        => new(Compartments, FixedCompartment, HasSharedSpace, OwnerIsolation, Guard.NotNull(actor));

    /// <inheritdoc cref="WithAnonymousActor(Actor)"/>
    /// <exception cref="FormatException"><paramref name="actor"/> is not a valid actor.</exception>
    public ApplicationFrame WithAnonymousActor(string actor) => WithAnonymousActor(Actor.Parse(Guard.NotNull(actor)));

    /// <summary>
    /// Reads the frame against what each module of the assembly declares, and fails when they do not hold
    /// together, naming every module at fault and what to change. Called once, at startup, before anything is
    /// served — by the host's registration, or explicitly by an application without one:
    /// <code>
    /// AppFrame.Value.Validate([documents, messaging]);
    /// </code>
    /// </summary>
    /// <remarks>
    /// Refused: a module declared twice, or two module types carrying the same name; a module with compartments
    /// in an application without any; a module with compartments that cannot hold the isolation by owner the
    /// frame declares; a parameter outside the module's capabilities. The guarantee stays the refusal at run time;
    /// the validation turns it into a message, before the first user meets it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="modules"/>, or one of them, is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The frame and the modules do not hold together.</exception>
    public void Validate(IEnumerable<ModuleDeclaration> modules)
    {
        var declared = Guard.NotNull(modules).Select(module => Guard.NotNull(module)).ToList();
        var faults = new List<string>();

        faults.AddRange(declared
            .GroupBy(module => module.Module)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Name} is declared {group.Count()} times: a module declares itself once"));
        faults.AddRange(declared
            .DistinctBy(module => module.Module)
            .GroupBy(module => module.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"'{group.Key}' is the name of {string.Join(" and ", group.Select(module => module.Module.FullName))}: "
                + "a module name identifies one module"));

        foreach (var module in declared.DistinctBy(module => module.Module))
        {
            if (module.IsCompartmented && Compartments == CompartmentCount.None)
            {
                faults.Add($"{module.Name} has compartments, and the application has none: declare its compartment, "
                    + "ApplicationFrame.SingleCompartment(\"acme\"), named after the organization that owns the data");
            }
            else if (module.IsCompartmented && OwnerIsolation && !module.Capabilities.Contains(ModuleCapability.OwnerIsolation))
            {
                faults.Add($"{module.Name} cannot hold the isolation by owner the application declares: register an "
                    + "adapter of the module that holds it, or remove WithOwnerIsolation() from the frame");
            }

            var outside = module.Parameters.Where(parameter => !module.Capabilities.Contains(parameter)).ToList();
            if (outside.Count > 0)
            {
                faults.Add($"{module.Name} is given {Plural(outside.Count, "a parameter", "parameters")} it has no capability for: "
                    + $"{string.Join(", ", outside)}");
            }
        }

        if (faults.Count > 0)
        {
            throw new InvalidOperationException(
                $"The modules do not hold with the application frame ({this}): {string.Join("; ", faults)}.");
        }
    }

    /// <summary>The former name of <see cref="Validate"/>, kept for 4.0 code.</summary>
    [Obsolete("Use Validate, its new name.")]
    public void Confront(IEnumerable<ModuleDeclaration> modules) => Validate(modules);

    /// <summary>The frame as one line, for a startup message: <c>single compartment 'acme', owner isolation</c>.</summary>
    public override string ToString()
    {
        var parts = new List<string>
        {
            Compartments switch
            {
                CompartmentCount.None => "no compartments",
                CompartmentCount.Single => $"single compartment '{FixedCompartment}'",
                _ => "many compartments",
            },
        };
        if (HasSharedSpace)
            parts.Add("shared space");
        if (OwnerIsolation)
            parts.Add("owner isolation");
        if (AnonymousActor != DefaultAnonymousActor)
            parts.Add($"anonymous actor '{AnonymousActor}'");
        return string.Join(", ", parts);
    }

    private static string Plural(int count, string one, string many) => count == 1 ? one : many;

    // A shared space and an isolation by owner are both ways of placing data among compartments: without
    // compartments, there is nothing to place, and a module that reads either would need a space it never gets.
    private void RequireCompartments(string what)
    {
        if (Compartments == CompartmentCount.None)
        {
            throw new InvalidOperationException(
                $"An application without compartments cannot have {what}. Declare its compartment first: "
                + "ApplicationFrame.SingleCompartment(\"acme\"), named after the organization that owns the data.");
        }
    }
}
