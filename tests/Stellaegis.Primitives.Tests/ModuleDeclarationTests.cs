namespace Stellaegis.Primitives.Tests;

public class ModuleDeclarationTests
{
    private static readonly ModuleCapability InheritSharedSpace = new("inherit_shared_space");

    private sealed class DocumentsModule : IStellaegisModule
    {
        public static string Name => "documents";
    }

    private sealed class MessagingModule : IStellaegisModule
    {
        public static string Name => "messaging";
    }

    private sealed class SecretsModule : IStellaegisModule
    {
        public static string Name => "secrets";
    }

    private sealed class OtherDocumentsModule : IStellaegisModule
    {
        public static string Name => "documents";
    }

    private sealed class HyphenModule : IStellaegisModule
    {
        public static string Name => "html-pdf";
    }

    private static readonly ApplicationFrame OwnerIsolated = ApplicationFrame.SingleCompartment("acme").WithOwnerIsolation();

    [Fact]
    public void A_declaration_says_whether_the_module_has_compartments()
    {
        var documents = ModuleDeclaration.Compartmented<DocumentsModule>();
        var secrets = ModuleDeclaration.WithoutCompartments<SecretsModule>();

        Assert.True(documents.IsCompartmented);
        Assert.Equal(ModuleCompartments.Required, documents.Compartments);
        Assert.Equal("documents", documents.Name);
        Assert.Equal(typeof(DocumentsModule), documents.Module);
        Assert.False(secrets.IsCompartmented);
        Assert.Equal(ModuleCompartments.None, secrets.Compartments);
        Assert.False(documents.AcceptsSharedSpace);
        Assert.Empty(secrets.Capabilities);
        Assert.Empty(secrets.Parameters);

        var vault = ModuleDeclaration.OptionallyCompartmented<SecretsModule>();
        Assert.True(vault.IsCompartmented);
        Assert.Equal(ModuleCompartments.Optional, vault.Compartments);
    }

    [Fact]
    public void A_module_accepts_the_shared_space_by_declaring_it()
    {
        Assert.True(ModuleDeclaration.Compartmented<DocumentsModule>().AcceptingSharedSpace().AcceptsSharedSpace);
        Assert.True(ModuleDeclaration.OptionallyCompartmented<SecretsModule>().AcceptingSharedSpace().AcceptsSharedSpace);

        var exception = Assert.Throws<InvalidOperationException>(() => ModuleDeclaration.WithoutCompartments<SecretsModule>().AcceptingSharedSpace());
        Assert.Contains("secrets is declared without compartments", exception.Message);
    }

    // The vault serves an application without compartments, by its assembly paths, and one with, by its spaces.
    [Fact]
    public void An_optionally_compartmented_module_fits_every_frame()
    {
        var vault = ModuleDeclaration.OptionallyCompartmented<SecretsModule>();

        ApplicationFrame.WithoutCompartments.Validate([vault]);
        ApplicationFrame.SingleCompartment("acme").Validate([vault]);
        ApplicationFrame.ManyCompartments().WithSharedSpace().Validate([vault]);

        var exception = Assert.Throws<InvalidOperationException>(() => OwnerIsolated.Validate([vault]));
        Assert.Contains("secrets is not compatible with the isolation by owner", exception.Message);
        OwnerIsolated.Validate([vault.WithCapability(ModuleCapability.OwnerIsolation)]);
        ApplicationFrame.WithoutCompartments.Validate([vault]);
    }

    [Fact]
    public void A_parameter_supposing_a_shared_space_needs_one_in_the_frame()
    {
        var inherits = new ModuleCapability("inherit_shared_space", requiresSharedSpace: true);
        Assert.True(inherits.RequiresSharedSpace);
        Assert.False(InheritSharedSpace.RequiresSharedSpace);
        Assert.Equal(inherits, InheritSharedSpace);

        var messaging = ModuleDeclaration.Compartmented<MessagingModule>().WithCapability(inherits).WithParameter(inherits);
        ApplicationFrame.SingleCompartment("acme").WithSharedSpace().Validate([messaging]);
        ApplicationFrame.SingleCompartment("acme").Validate([ModuleDeclaration.Compartmented<MessagingModule>().WithCapability(inherits)]);   // the capability alone is harmless

        var exception = Assert.Throws<InvalidOperationException>(() => ApplicationFrame.SingleCompartment("acme").Validate([messaging]));
        Assert.Contains("messaging is given a parameter that supposes a shared space, and the application has none: inherit_shared_space", exception.Message);
        Assert.Contains("WithSharedSpace()", exception.Message);
    }

    private static readonly Operation Read = new("documents", "read", OperationKind.Read);
    private static readonly Operation Purge = new("documents", "purge", OperationKind.Delete);

    [Fact]
    public void A_declaration_carries_the_operations_of_the_module()
    {
        var documents = ModuleDeclaration.Compartmented<DocumentsModule>().WithOperations(Read, Purge).WithOperations(Read);

        Assert.Equal([Read, Purge], documents.Operations);
        Assert.Equal("documents.read", Read.Identity);
        Assert.Empty(ModuleDeclaration.Compartmented<DocumentsModule>().Operations);
    }

    [Fact]
    public void An_identity_names_one_operation()
    {
        var exception = Assert.Throws<ArgumentException>(() => ModuleDeclaration.Compartmented<DocumentsModule>()
            .WithOperations(Read, new Operation("documents", "read", OperationKind.Write)));

        Assert.Contains("documents declares documents.read twice, as Read and as Write", exception.Message);
        Assert.Throws<ArgumentNullException>(() => ModuleDeclaration.Compartmented<DocumentsModule>().WithOperations(null!));
        Assert.Throws<ArgumentNullException>(() => ModuleDeclaration.Compartmented<DocumentsModule>().WithOperations([null!]));
    }

    [Fact]
    public void A_declaration_checks_the_module_name()
    {
        var exception = Assert.Throws<ArgumentException>(() => ModuleDeclaration.Compartmented<HyphenModule>());
        Assert.Contains(nameof(HyphenModule), exception.Message);
    }

    [Fact]
    public void A_declaration_is_immutable()
    {
        var messaging = ModuleDeclaration.Compartmented<MessagingModule>();

        var capable = messaging.WithCapability(InheritSharedSpace);
        var chosen = capable.WithParameter(InheritSharedSpace).WithParameter(InheritSharedSpace);

        Assert.Empty(messaging.Capabilities);
        Assert.Equal([InheritSharedSpace], capable.Capabilities);
        Assert.Empty(capable.Parameters);
        Assert.Equal([InheritSharedSpace], chosen.Parameters);
    }

    [Fact]
    public void A_capability_follows_the_identifier_rule()
    {
        Assert.Equal(new ModuleCapability("owner_isolation"), ModuleCapability.OwnerIsolation);
        Assert.Throws<ArgumentException>(() => new ModuleCapability("owner-isolation"));
        Assert.Throws<ArgumentException>(() => new ModuleCapability("Owner"));
        Assert.Throws<ArgumentException>(() => new ModuleCapability(string.Empty));
    }

    [Fact]
    public void A_frame_holds_with_modules_that_fit_it()
    {
        ApplicationFrame.WithoutCompartments.Validate([ModuleDeclaration.WithoutCompartments<SecretsModule>()]);
        ApplicationFrame.WithoutCompartments.Validate([]);
        ApplicationFrame.ManyCompartments().Validate([
            ModuleDeclaration.Compartmented<DocumentsModule>(),
            ModuleDeclaration.Compartmented<MessagingModule>().WithCapability(InheritSharedSpace).WithParameter(InheritSharedSpace),
            ModuleDeclaration.WithoutCompartments<SecretsModule>(),
        ]);
    }

    [Fact]
    public void A_module_with_compartments_needs_an_application_with_compartments()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ApplicationFrame.WithoutCompartments.Validate([ModuleDeclaration.Compartmented<DocumentsModule>()]));

        Assert.Contains("documents has compartments", exception.Message);
        Assert.Contains("SingleCompartment", exception.Message);
    }

    [Fact]
    public void An_isolation_by_owner_is_held_by_every_module_with_compartments()
    {
        OwnerIsolated.Validate([
            ModuleDeclaration.Compartmented<DocumentsModule>().WithCapability(ModuleCapability.OwnerIsolation),
            ModuleDeclaration.WithoutCompartments<SecretsModule>(),
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => OwnerIsolated.Validate([
            ModuleDeclaration.Compartmented<DocumentsModule>().WithCapability(ModuleCapability.OwnerIsolation),
            ModuleDeclaration.Compartmented<MessagingModule>(),
        ]));

        Assert.Contains("messaging is not compatible with the isolation by owner", exception.Message);
        Assert.DoesNotContain("documents", exception.Message);
        Assert.Contains("single compartment 'acme', owner isolation", exception.Message);
    }

    [Fact]
    public void A_parameter_is_chosen_among_the_capabilities()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ApplicationFrame.ManyCompartments().Validate([
            ModuleDeclaration.Compartmented<DocumentsModule>().WithParameter(InheritSharedSpace),
        ]));

        Assert.Contains("documents is given a parameter it has no capability for: inherit_shared_space", exception.Message);
    }

    [Fact]
    public void A_module_is_declared_once_and_its_name_is_its_own()
    {
        var twice = Assert.Throws<InvalidOperationException>(() => ApplicationFrame.ManyCompartments().Validate([
            ModuleDeclaration.Compartmented<DocumentsModule>(),
            ModuleDeclaration.Compartmented<DocumentsModule>(),
        ]));
        var homonyms = Assert.Throws<InvalidOperationException>(() => ApplicationFrame.ManyCompartments().Validate([
            ModuleDeclaration.Compartmented<DocumentsModule>(),
            ModuleDeclaration.Compartmented<OtherDocumentsModule>(),
        ]));

        Assert.Contains("DocumentsModule is declared 2 times", twice.Message);
        Assert.Contains("'documents' is the name of", homonyms.Message);
        Assert.Contains(nameof(OtherDocumentsModule), homonyms.Message);
    }

    [Fact]
    public void A_declaration_carries_the_preferences_of_the_module_under_its_name()
    {
        var view = Preference.Choice("view", ["list", "grid"], "list");
        var compact = Preference.Boolean("compact", false);

        var documents = ModuleDeclaration.Compartmented<DocumentsModule>().WithPreferences(view, compact).WithPreferences(view);
        var messaging = ModuleDeclaration.Compartmented<MessagingModule>().WithPreferences(compact);

        Assert.Equal(["documents.compact", "documents.view"], documents.Preferences.Keys.Order(StringComparer.Ordinal));
        Assert.Same(view, documents.Preferences["documents.view"]);
        Assert.Equal(["messaging.compact"], messaging.Preferences.Keys);
        Assert.Empty(ModuleDeclaration.Compartmented<SecretsModule>().Preferences);
        Assert.Equal(documents.Preferences, documents.WithCapability(InheritSharedSpace).WithOperations(Read).AcceptingSharedSpace().Preferences);
        ApplicationFrame.ManyCompartments().Validate([documents, messaging]);
    }

    [Fact]
    public void A_preference_name_names_one_definition_in_its_module()
    {
        var exception = Assert.Throws<ArgumentException>(() => ModuleDeclaration.Compartmented<DocumentsModule>()
            .WithPreferences(Preference.Choice("view", ["list", "grid"]), Preference.Choice("view", ["list", "grid", "table"])));

        Assert.Contains("documents declares the preference view twice, with two definitions", exception.Message);
        Assert.Throws<ArgumentNullException>(() => ModuleDeclaration.Compartmented<DocumentsModule>().WithPreferences(null!));
        Assert.Throws<ArgumentNullException>(() => ModuleDeclaration.Compartmented<DocumentsModule>().WithPreferences([null!]));
    }

    [Fact]
    public void An_operation_identity_belongs_to_one_module()
    {
        // Identity and Registry once both declared users.read: a right granted on one would have opened the other.
        var users = new Operation("users", "read", OperationKind.Read);
        var documents = ModuleDeclaration.Compartmented<DocumentsModule>().WithOperations(Read, users);
        var messaging = ModuleDeclaration.Compartmented<MessagingModule>().WithOperations(users);

        var exception = Assert.Throws<InvalidOperationException>(() => ApplicationFrame.ManyCompartments().Validate([documents, messaging]));

        Assert.Contains("users.read is declared by documents and messaging", exception.Message);
        Assert.DoesNotContain("documents.read", exception.Message);
        ApplicationFrame.ManyCompartments().Validate([documents, ModuleDeclaration.Compartmented<MessagingModule>()]);
    }

    [Fact]
    public void Every_fault_is_named_at_once()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OwnerIsolated.Validate([
            ModuleDeclaration.Compartmented<DocumentsModule>(),
            ModuleDeclaration.Compartmented<MessagingModule>().WithParameter(InheritSharedSpace),
        ]));

        Assert.Contains("documents is not compatible", exception.Message);
        Assert.Contains("messaging is not compatible", exception.Message);
        Assert.Contains("messaging is given a parameter", exception.Message);
    }

    [Fact]
    public void Validate_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => OwnerIsolated.Validate(null!));
        Assert.Throws<ArgumentNullException>(() => OwnerIsolated.Validate([null!]));
    }

}
