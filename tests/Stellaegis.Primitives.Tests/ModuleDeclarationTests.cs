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
        Assert.Equal("documents", documents.Name);
        Assert.Equal(typeof(DocumentsModule), documents.Module);
        Assert.False(secrets.IsCompartmented);
        Assert.Empty(secrets.Capabilities);
        Assert.Empty(secrets.Parameters);
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

        Assert.Contains("messaging cannot hold the isolation by owner", exception.Message);
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
    public void Every_fault_is_named_at_once()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OwnerIsolated.Validate([
            ModuleDeclaration.Compartmented<DocumentsModule>(),
            ModuleDeclaration.Compartmented<MessagingModule>().WithParameter(InheritSharedSpace),
        ]));

        Assert.Contains("documents cannot hold", exception.Message);
        Assert.Contains("messaging cannot hold", exception.Message);
        Assert.Contains("messaging is given a parameter", exception.Message);
    }

    [Fact]
    public void Validate_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => OwnerIsolated.Validate(null!));
        Assert.Throws<ArgumentNullException>(() => OwnerIsolated.Validate([null!]));
    }

    [Fact]
    public void Confront_stays_the_former_name_of_Validate()
    {
#pragma warning disable CS0618 // the former name, kept for 4.0 code
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ApplicationFrame.WithoutCompartments.Confront([ModuleDeclaration.Compartmented<DocumentsModule>()]));
#pragma warning restore CS0618
        Assert.Contains("documents has compartments, and the application has none", exception.Message);
    }
}
