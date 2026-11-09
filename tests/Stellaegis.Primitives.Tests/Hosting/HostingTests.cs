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

    [Fact]
    public void An_http_client_configured_in_code_takes_its_address_and_its_timeout_too()
    {
        using var provider = new ServiceCollection()
            .AddStellaegisHttpClient<INotesClient, HttpNotesClient, NotesClientOptions>(o =>
            {
                o.BaseUrl = "https://notes.internal/api";
                o.Timeout = TimeSpan.FromSeconds(5);
            }).Services
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

    private sealed class Recorder : IHostedService
    {
        public bool Started { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Theory]
    [InlineData("https://rendering.internal/api?tenant=acme", "query or a fragment")]
    [InlineData("https://rendering.internal/api#top", "query or a fragment")]
    [InlineData("https://svc:secret@rendering.internal/api", "credentials")]
    [InlineData("http://rendering.internal/api", "plain http")]
    public void A_base_address_that_would_lose_its_path_leak_or_travel_in_clear_is_refused(string baseUrl, string error)
        => Assert.Contains(error, new NotesClientOptions { BaseUrl = baseUrl }.FindError());

    [Fact]
    public void Plain_http_goes_to_the_local_machine_or_where_it_is_declared()
    {
        Assert.Null(new NotesClientOptions { BaseUrl = "http://localhost:5000/api" }.FindError());
        Assert.Null(new NotesClientOptions { BaseUrl = "http://127.0.0.1/api" }.FindError());
        Assert.Null(new NotesClientOptions { BaseUrl = "http://rendering.internal/api", AllowPlainHttp = true }.FindError());
        Assert.Contains("beyond what HttpClient takes", new NotesClientOptions { BaseUrl = "https://x", Timeout = TimeSpan.FromDays(30) }.FindError());
    }

    // Registered first, a purge started under an assembly that does not hold before the validation failed the host.
    [Fact]
    public async Task The_validation_runs_before_any_hosted_service_starts()
    {
        var recorder = new Recorder();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHostedService(_ => recorder);
        builder.Services
            .AddStellaegisFrame(ApplicationFrame.WithoutCompartments)
            .AddStellaegisModule(ModuleDeclaration.Compartmented<DocumentsModule>())
            .AddStellaegisValidation();
        using var host = builder.Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(Ct));
        Assert.False(recorder.Started);
    }

    // AddSingleton after AddStellaegisFrame replaced the frame in silence, the directory still listing the first.
    [Fact]
    public void A_frame_registered_again_fails_the_validation()
    {
        var services = new ServiceCollection().AddStellaegisFrame(ApplicationFrame.SingleCompartment("acme"));
        services.AddSingleton(ApplicationFrame.SingleCompartment("globex"));

        var exception = Assert.Throws<InvalidOperationException>(() => services.BuildServiceProvider().ValidateStellaegisModules());
        Assert.Contains("2 application frames are registered", exception.Message);
    }

    [Fact]
    public void A_single_compartment_frame_without_its_directory_fails_the_validation()
    {
        var services = new ServiceCollection().AddSingleton(ApplicationFrame.SingleCompartment("acme"));

        var exception = Assert.Throws<InvalidOperationException>(() => services.BuildServiceProvider().ValidateStellaegisModules());
        Assert.Contains("no directory lists it", exception.Message);
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
