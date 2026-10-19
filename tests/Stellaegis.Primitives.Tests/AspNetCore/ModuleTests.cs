using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives.AspNetCore;
using Stellaegis.Primitives.DependencyInjection;

namespace Stellaegis.Primitives.Tests.AspNetCore;

public class ModuleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Operation Render = new("rendering", "html", OperationKind.Read);

    private sealed class RenderingModule : IStellaegisModule
    {
        public static string Name => "rendering";
    }

    private sealed class OtherRenderingModule : IStellaegisModule
    {
        public static string Name => "rendering";
    }

    private sealed class UppercaseModule : IStellaegisModule
    {
        public static string Name => "Rendering";
    }

    private sealed class HyphenModule : IStellaegisModule
    {
        public static string Name => "html-pdf";
    }

    private sealed class UnderscoreModule : IStellaegisModule
    {
        public static string Name => "html_pdf";
    }

    private sealed class ShortModule : IStellaegisModule
    {
        public static string Name => "ab";
    }

    private sealed class DigitFirstModule : IStellaegisModule
    {
        public static string Name => "2fa";
    }

    private sealed class LongModule : IStellaegisModule
    {
        public static string Name => new('a', 64);
    }

    private sealed class LongestModule : IStellaegisModule
    {
        public static string Name => "a" + new string('1', 62);
    }

    private sealed class EmptyModule : IStellaegisModule
    {
        public static string Name => string.Empty;
    }

    private static readonly ModuleDeclaration Rendering = ModuleDeclaration.WithoutCompartments<RenderingModule>();

    // A module without compartments: it reads no request context, so the application registers none.
    private static RouteGroupBuilder MapRendering(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/rendering").BelongsTo<RenderingModule>();
        group.MapPost("/html", () => "<p></p>").WithMetadata(Render);
        return group;
    }

    [Fact]
    public async Task A_module_without_compartments_is_checked_at_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            services => services.AddStellaegisStartupCheck().AddStellaegisModule(Rendering).AddAuthorization(),
            routes => MapRendering(routes).AllowAnyResource()));

        Assert.Contains("rendering.html", exception.Message);
        Assert.Contains("declare no authorization", exception.Message);
    }

    [Fact]
    public async Task A_module_without_compartments_needs_no_request_context()
    {
        await using var app = await TestApp.StartAsync(
            services => services.AddStellaegisStartupCheck().AddStellaegisModule(Rendering).AddAuthorization(),
            routes => MapRendering(routes).AllowAnonymous().AllowAnyResource());

        Assert.Null(app.Services.GetService<IRequestContext>());
        var response = await app.GetTestClient().PostAsync("/rendering/html", null, Ct);
        Assert.Equal("<p></p>", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Belonging_to_a_module_without_the_startup_check_fails()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            services => services.AddAuthorization(),
            routes => MapRendering(routes)));

        Assert.Contains("rendering module", exception.Message);
        Assert.Contains("AddStellaegisStartupCheck()", exception.Message);
    }

    [Fact]
    public async Task A_route_labelled_outside_any_module_fails_the_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            services => services.AddStellaegisStartupCheck().AddStellaegisModule(Rendering).AddAuthorization(),
            routes =>
            {
                MapRendering(routes).AllowAnonymous().AllowAnyResource();
                routes.MapPost("/pdf", () => "%PDF").WithMetadata(new Operation("rendering", "pdf", OperationKind.Read))
                    .AllowAnonymous().AllowAnyResource();
            }));

        Assert.Contains("belong to no module", exception.Message);
        Assert.Contains("rendering.pdf", exception.Message);
        Assert.DoesNotContain("rendering.html", exception.Message);
    }

    [Fact]
    public async Task Two_module_types_with_the_same_name_fail_the_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            services => services.AddStellaegisStartupCheck()
                .AddStellaegisModule(Rendering)
                .AddStellaegisModule(ModuleDeclaration.WithoutCompartments<OtherRenderingModule>()),
            routes =>
            {
                routes.MapGroup("/one").BelongsTo<RenderingModule>().MapGet("/", () => "one");
                routes.MapGroup("/two").BelongsTo<OtherRenderingModule>().MapGet("/", () => "two");
            }));

        Assert.Contains("'rendering'", exception.Message);
        Assert.Contains(nameof(RenderingModule), exception.Message);
        Assert.Contains(nameof(OtherRenderingModule), exception.Message);
    }

    [Fact]
    public async Task One_module_mapped_twice_is_not_a_homonym()
    {
        await using var app = await TestApp.StartAsync(
            services => services.AddStellaegisStartupCheck().AddStellaegisModule(Rendering),
            routes =>
            {
                routes.MapGroup("/one").BelongsTo<RenderingModule>().MapGet("/", () => "one");
                routes.MapGroup("/two").BelongsTo<RenderingModule>().MapGet("/", () => "two");
            });

        Assert.Equal("two", await app.GetTestClient().GetStringAsync("/two", Ct));
    }

    // Without this, a module with compartments that forgot to declare itself would escape the validation, and
    // with it the refusal of an isolation by owner it cannot hold.
    [Fact]
    public async Task A_module_on_the_routes_and_absent_from_the_registrations_fails_the_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            services => services.AddStellaegisStartupCheck().AddAuthorization(),
            routes => MapRendering(routes).AllowAnonymous().AllowAnyResource()));

        Assert.Contains("Routes belong to modules nothing declares: rendering", exception.Message);
        Assert.Contains("AddStellaegisModule", exception.Message);
    }

    [Fact]
    public async Task A_module_declared_without_compartments_cannot_read_the_request_context()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            services => services
                .AddStellaegisFrame(ApplicationFrame.SingleCompartment("acme"))
                .AddStellaegisRequestContext()
                .AddStellaegisModule(Rendering)
                .AddAuthorization(),
            routes => MapRendering(routes).RequireRequestContext().AllowAnonymous().AllowAnyResource()));

        Assert.Contains("their module is declared without compartments", exception.Message);
        Assert.Contains("/rendering/html (rendering)", exception.Message);
    }

    [Fact]
    public async Task A_module_name_follows_the_rule()
    {
        await using (await TestApp.StartAsync(
            services => services.AddStellaegisStartupCheck().AddStellaegisModule(ModuleDeclaration.WithoutCompartments<LongestModule>()),
            routes => routes.MapGroup("/m").BelongsTo<LongestModule>()))
        {
        }

        await AssertRefusedAsync<UppercaseModule>();
        await AssertRefusedAsync<HyphenModule>();
        await AssertRefusedAsync<UnderscoreModule>();
        await AssertRefusedAsync<ShortModule>();
        await AssertRefusedAsync<DigitFirstModule>();
        await AssertRefusedAsync<LongModule>();
        await AssertRefusedAsync<EmptyModule>();

        static async Task AssertRefusedAsync<TModule>() where TModule : IStellaegisModule
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => TestApp.StartAsync(
                services => services.AddStellaegisStartupCheck(),
                routes => routes.MapGroup("/m").BelongsTo<TModule>()));
            Assert.Contains(typeof(TModule).Name, exception.Message);
        }
    }

    [Fact]
    public void Registering_the_check_twice_registers_it_once()
    {
        var services = new ServiceCollection()
            .AddStellaegisStartupCheck()
            .AddStellaegisRequestContext()
            .AddStellaegisStartupCheck();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IStartupFilter));
    }
}
