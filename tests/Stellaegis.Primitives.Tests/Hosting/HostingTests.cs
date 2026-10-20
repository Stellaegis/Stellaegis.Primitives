using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Primitives.Tests.Hosting;

public class HostingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class DocumentsModule : IStellaegisModule
    {
        public static string Name => "documents";
    }

    private sealed class StoreOptions : IValidatableOptions
    {
        public string ConnectionString { get; init; } = string.Empty;
        public KeySlots Keys { get; init; } = new();

        public string? FindError() => string.IsNullOrWhiteSpace(ConnectionString) ? "ConnectionString is required." : Keys.FindError();
    }

    private sealed class PlainOptions
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class NotesClientOptions : StellaegisClientOptions;

    private interface INotesClient;

    private sealed class HttpNotesClient(HttpClient http) : INotesClient
    {
        public HttpClient Http { get; } = http;
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void Options_are_bound_from_their_section_and_validated_at_startup()
    {
        var configuration = Configuration(
            ("Stellaegis:Documents:PostgreSQL:ConnectionString", "Host=db"),
            ("Stellaegis:Documents:PostgreSQL:Keys:ActiveKeySlot", "2"),
            ("Stellaegis:Documents:PostgreSQL:Keys:Keys:2", "second"));
        using var provider = new ServiceCollection()
            .AddStellaegisOptions<StoreOptions>(configuration, "Stellaegis:Documents:PostgreSQL").Services
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<StoreOptions>>().Value;
        Assert.Equal("Host=db", options.ConnectionString);
        Assert.Equal("second", options.Keys.ActiveKey);
    }

    [Fact]
    public async Task Options_that_do_not_hold_fail_the_host_before_anything_runs()
    {
        var configuration = Configuration(("Stellaegis:Documents:PostgreSQL:ConnectionString", "Host=db"));
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddStellaegisOptions<StoreOptions>(configuration, "Stellaegis:Documents:PostgreSQL");
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(Ct));
        Assert.Contains("StoreOptions: No key is configured for the active slot 1", exception.Message);
    }

    [Fact]
    public void Options_configured_in_code_are_validated_too_and_plain_options_are_left_alone()
    {
        using var failing = new ServiceCollection().AddStellaegisOptions<StoreOptions>(o => { }).Services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => failing.GetRequiredService<IOptions<StoreOptions>>().Value);

        using var plain = new ServiceCollection().AddStellaegisOptions<PlainOptions>(Configuration(("Plain:Name", "x")), "Plain").Services.BuildServiceProvider();
        Assert.Equal("x", plain.GetRequiredService<IOptions<PlainOptions>>().Value.Name);
        Assert.Empty(plain.GetServices<IValidateOptions<PlainOptions>>());
    }

    [Fact]
    public void An_http_client_takes_its_address_and_its_timeout_from_its_options()
    {
        var configuration = Configuration(
            ("Stellaegis:Notes:Client:BaseUrl", "https://notes.internal/api"),
            ("Stellaegis:Notes:Client:Timeout", "00:00:05"));
        using var provider = new ServiceCollection()
            .AddStellaegisHttpClient<INotesClient, HttpNotesClient, NotesClientOptions>(configuration, "Stellaegis:Notes:Client").Services
            .BuildServiceProvider();

        var client = Assert.IsType<HttpNotesClient>(provider.GetRequiredService<INotesClient>());
        Assert.Equal(new Uri("https://notes.internal/api/"), client.Http.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(5), client.Http.Timeout);
    }

    [Theory]
    [InlineData("", "BaseUrl is required")]
    [InlineData("notes.internal/api", "BaseUrl is required")]
    [InlineData("ftp://notes.internal", "BaseUrl is required")]
    public void A_client_address_is_an_absolute_http_url(string baseUrl, string error)
    {
        using var provider = new ServiceCollection()
            .AddStellaegisHttpClient<INotesClient, HttpNotesClient, NotesClientOptions>(o => { })
            .Services.BuildServiceProvider();
        var configured = new NotesClientOptions { BaseUrl = baseUrl };

        Assert.Contains(error, configured.FindError());
        Assert.Contains("Timeout must be positive", new NotesClientOptions { BaseUrl = "https://x", Timeout = TimeSpan.Zero }.FindError());
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<INotesClient>());
    }

    [Fact]
    public async Task A_generic_host_validates_the_assembly_when_it_starts()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddStellaegisFrame(ApplicationFrame.WithoutCompartments)
            .AddStellaegisModule(ModuleDeclaration.Compartmented<DocumentsModule>())
            .AddStellaegisValidation()
            .AddStellaegisValidation();
        using var host = builder.Build();

        Assert.Single(host.Services.GetServices<IHostedService>(), service => service is ValidationHostedService);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(Ct));
        Assert.Contains("documents has compartments, and the application has none", exception.Message);
    }

    [Fact]
    public async Task A_generic_host_whose_assembly_holds_starts()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddStellaegisFrame(ApplicationFrame.SingleCompartment("acme"))
            .AddStellaegisModule(ModuleDeclaration.Compartmented<DocumentsModule>())
            .AddStellaegisValidation();
        using var host = builder.Build();

        await host.StartAsync(Ct);
        await host.StopAsync(Ct);
    }
}
