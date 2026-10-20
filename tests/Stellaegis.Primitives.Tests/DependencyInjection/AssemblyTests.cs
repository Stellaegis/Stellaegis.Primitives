using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives.DependencyInjection;

namespace Stellaegis.Primitives.Tests.DependencyInjection;

public class AssemblyTests
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

    private sealed class HyphenModule : IStellaegisModule
    {
        public static string Name => "html-pdf";
    }

    private static readonly Operation ReadDocuments = new("documents", "read", OperationKind.Read);
    private static readonly Operation PurgeDocuments = new("documents", "purge", OperationKind.Delete);

    // What a module's registration does, and what its PostgreSQL adapter adds.
    private static IServiceCollection AddDocuments(IServiceCollection services)
        => services.AddStellaegisModule(ModuleDeclaration.Compartmented<DocumentsModule>().WithOperations(ReadDocuments, PurgeDocuments));

    private static IServiceCollection AddDocumentsPostgreSql(IServiceCollection services)
        => AddDocuments(services).AddStellaegisModuleCapability<DocumentsModule>(ModuleCapability.OwnerIsolation);

    private static IServiceCollection AddMessaging(IServiceCollection services)
        => services.AddStellaegisModule(ModuleDeclaration.Compartmented<MessagingModule>().WithCapability(InheritSharedSpace));

    private static void Validate(IServiceCollection services) => services.BuildServiceProvider().ValidateStellaegisModules();

    [Fact]
    public void The_frame_is_registered_once_and_readable()
    {
        var frame = ApplicationFrame.SingleCompartment("acme");
        var services = new ServiceCollection().AddStellaegisFrame(frame);

        Assert.Same(frame, services.BuildServiceProvider().GetRequiredService<ApplicationFrame>());
        var exception = Assert.Throws<InvalidOperationException>(() => services.AddStellaegisFrame(frame));
        Assert.Contains("already registered", exception.Message);
    }

    [Fact]
    public void An_application_without_a_frame_has_no_compartments()
    {
        Validate(new ServiceCollection());

        var exception = Assert.Throws<InvalidOperationException>(() => Validate(AddDocuments(new ServiceCollection())));
        Assert.Contains("documents has compartments, and the application has none", exception.Message);
    }

    [Fact]
    public void ConfrontStellaegisModules_stays_the_former_name_of_ValidateStellaegisModules()
    {
        var services = AddDocuments(new ServiceCollection()).BuildServiceProvider();

#pragma warning disable CS0618 // the former name, kept for 4.0 code
        var exception = Assert.Throws<InvalidOperationException>(() => services.ConfrontStellaegisModules());
#pragma warning restore CS0618
        Assert.Contains("documents has compartments, and the application has none", exception.Message);
    }

    [Fact]
    public void The_adapter_brings_the_capability_the_frame_requires()
    {
        var frame = ApplicationFrame.SingleCompartment("acme").WithOwnerIsolation();

        Validate(AddDocumentsPostgreSql(new ServiceCollection().AddStellaegisFrame(frame)));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Validate(AddDocuments(new ServiceCollection().AddStellaegisFrame(frame))));
        Assert.Contains("documents cannot hold the isolation by owner", exception.Message);
    }

    [Fact]
    public void The_application_chooses_parameters_among_the_capabilities()
    {
        var services = AddMessaging(new ServiceCollection().AddStellaegisFrame(ApplicationFrame.ManyCompartments()))
            .AddStellaegisModuleParameter<MessagingModule>(InheritSharedSpace);
        Validate(services);

        AddDocuments(services).AddStellaegisModuleParameter<DocumentsModule>(InheritSharedSpace);
        var exception = Assert.Throws<InvalidOperationException>(() => Validate(services));
        Assert.Contains("documents is given a parameter it has no capability for", exception.Message);
    }

    [Fact]
    public void A_module_reads_the_parameters_the_application_turned_on()
    {
        using var provider = AddMessaging(new ServiceCollection())
            .AddStellaegisModuleParameter<MessagingModule>(InheritSharedSpace)
            .BuildServiceProvider();

        Assert.Contains(InheritSharedSpace, provider.GetStellaegisModule<MessagingModule>()!.Parameters);
        Assert.Null(provider.GetStellaegisModule<DocumentsModule>());
    }

    // The catalogue of an application, read without ASP.NET Core: every operation of every module, those with a
    // route and those done outside requests alike.
    [Fact]
    public void The_declarations_are_the_catalogue_of_the_application()
    {
        using var provider = AddMessaging(AddDocuments(new ServiceCollection().AddStellaegisFrame(ApplicationFrame.ManyCompartments())))
            .BuildServiceProvider();

        var catalogue = provider.GetStellaegisModules()
            .SelectMany(module => module.Operations.Select(operation => $"{module.Name}: {operation.Identity} {operation.Kind}"))
            .ToList();

        Assert.Equal(["documents: documents.read Read", "documents: documents.purge Delete"], catalogue);
        Assert.Equal([ReadDocuments, PurgeDocuments], provider.GetStellaegisModule<DocumentsModule>()!.Operations);
    }

    [Fact]
    public void A_capability_or_a_parameter_for_a_module_nothing_declares_is_refused()
    {
        var services = new ServiceCollection()
            .AddStellaegisFrame(ApplicationFrame.SingleCompartment("acme"))
            .AddStellaegisModuleCapability<DocumentsModule>(ModuleCapability.OwnerIsolation)
            .AddStellaegisModuleParameter<MessagingModule>(InheritSharedSpace);

        var exception = Assert.Throws<InvalidOperationException>(() => Validate(services));

        Assert.Contains("documents (capability owner_isolation)", exception.Message);
        Assert.Contains("messaging (parameter inherit_shared_space)", exception.Message);
        Assert.Contains("AddStellaegisModule", exception.Message);
    }

    [Fact]
    public void A_module_registered_twice_is_refused()
    {
        var services = AddDocuments(AddDocuments(new ServiceCollection().AddStellaegisFrame(ApplicationFrame.ManyCompartments())));

        var exception = Assert.Throws<InvalidOperationException>(() => Validate(services));

        Assert.Contains("DocumentsModule is declared 2 times", exception.Message);
    }

    [Fact]
    public void A_module_name_is_checked_where_it_is_registered()
    {
        Assert.Throws<ArgumentException>(() =>
            new ServiceCollection().AddStellaegisModuleCapability<HyphenModule>(ModuleCapability.OwnerIsolation));
        Assert.Throws<ArgumentException>(() =>
            new ServiceCollection().AddStellaegisModuleParameter<HyphenModule>(InheritSharedSpace));
    }
}
