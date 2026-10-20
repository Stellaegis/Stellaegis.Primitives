using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives.AspNetCore;
using Stellaegis.Primitives.DependencyInjection;

namespace Stellaegis.Primitives.Tests.AspNetCore;

/// <summary>
/// What the package offers a module written with MVC controllers today, before anything is added for them: each
/// test states a fact about ASP.NET Core and the package together.
/// </summary>
public class ControllerProbeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static readonly Operation ReadNotes = new("notes", "read", OperationKind.Read);

    private static readonly ApplicationFrame Acme = ApplicationFrame.SingleCompartment("acme");

    [Fact]
    public async Task An_action_answers_a_result_as_problem_details()
    {
        await using var app = await StartAsync(services => { }, routes => routes.MapControllers().AllowAnonymous());

        var response = await app.GetTestClient().GetAsync("/notes/missing", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Note.NotFound", (await response.ReadErrorAsync(Ct))!.Code);
    }

    [Fact]
    public async Task The_error_translation_filter_reaches_an_action()
    {
        await using var app = await StartAsync(services => { }, routes => routes.MapControllers().AllowAnonymous().AddErrorTranslation());

        var response = await app.GetTestClient().GetAsync("/notes/locked", Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Note.Locked", (await response.ReadErrorAsync(Ct))!.Code);
    }

    [Fact]
    public async Task The_request_context_reaches_an_action()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisFrame(Acme).AddStellaegisRequestContext(),
            routes => routes.MapControllers().AllowAnonymous());

        Assert.Equal("acme", await app.GetTestClient().GetStringAsync("/notes/space", Ct));
    }

    [Fact]
    public async Task A_group_carries_its_module_and_its_prefix_onto_the_actions_it_maps()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisStartupCheck().AddStellaegisModule(ModuleDeclaration.WithoutCompartments<NotesControllerModule>()),
            routes => routes.MapGroup("/api").BelongsTo<NotesControllerModule>().MapControllers().AllowAnonymous());

        Assert.Equal("-", await app.GetTestClient().GetStringAsync("/api/notes/operation", Ct));
        Assert.All(ActionEndpoints(app), endpoint => Assert.Equal("notes", endpoint.Metadata.GetMetadata<ModuleMetadata>()?.Name));
    }

    [Fact]
    public async Task MapControllers_in_a_group_maps_the_controllers_of_every_module()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisStartupCheck().AddStellaegisModule(ModuleDeclaration.WithoutCompartments<NotesControllerModule>()),
            routes => routes.MapGroup("/api").BelongsTo<NotesControllerModule>().MapControllers().AllowAnonymous(),
            typeof(NotesController), typeof(TagsController));

        var tags = Assert.Single(ActionEndpoints(app), endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName == "Tags");
        Assert.Equal("notes", tags.Metadata.GetMetadata<ModuleMetadata>()?.Name);
    }

    [Fact]
    public async Task MapControllers_in_two_groups_maps_every_controller_twice()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisStartupCheck()
                .AddStellaegisModule(ModuleDeclaration.WithoutCompartments<NotesControllerModule>())
                .AddStellaegisModule(ModuleDeclaration.WithoutCompartments<TagsControllerModule>()),
            routes =>
            {
                routes.MapGroup("/n").BelongsTo<NotesControllerModule>().MapControllers().AllowAnonymous();
                routes.MapGroup("/t").BelongsTo<TagsControllerModule>().MapControllers().AllowAnonymous();
            },
            typeof(NotesController), typeof(TagsController));

        Assert.Equal(2, ActionEndpoints(app).Count(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName == "Tags"));
        Assert.Equal("-", await app.GetTestClient().GetStringAsync("/t/notes/operation", Ct));
    }

    [Fact]
    public async Task A_group_declares_the_request_context_of_the_actions_it_maps()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisFrame(Acme).AddStellaegisRequestContext()
                .AddStellaegisModule(ModuleDeclaration.Compartmented<NotesControllerModule>()),
            routes => routes.MapGroup("").BelongsTo<NotesControllerModule>().RequireRequestContext()
                .MapControllers().AllowAnonymous());

        Assert.Equal("acme", await app.GetTestClient().GetStringAsync("/notes/space", Ct));
    }

    [Fact]
    public async Task The_shared_space_reaches_an_action()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisFrame(ApplicationFrame.ManyCompartments().WithSharedSpace())
                .AddStellaegisRequestContext(c => c.CompartmentFromRoute("org"))
                .AddStellaegisModule(ModuleDeclaration.Compartmented<NotesControllerModule>()),
            routes => routes.MapGroup("/shared").BelongsTo<NotesControllerModule>().RequireRequestContext(acceptsSharedSpace: true)
                .MapControllers().AllowAnonymous().InSharedSpace());

        Assert.Equal("(shared)", await app.GetTestClient().GetStringAsync("/shared/notes/space", Ct));
    }

    [Fact]
    public async Task An_operation_given_to_all_the_controllers_reaches_an_action()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisStartupCheck().AddStellaegisModule(ModuleDeclaration.WithoutCompartments<NotesControllerModule>()),
            routes => routes.MapGroup("").BelongsTo<NotesControllerModule>().MapControllers()
                .WithMetadata(ReadNotes).AllowAnonymous().AllowAnyResource());

        Assert.Equal("notes.read", await app.GetTestClient().GetStringAsync("/notes/operation", Ct));
    }

    [Fact]
    public async Task An_operation_reaches_one_action_through_a_final_convention()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisStartupCheck().AddStellaegisModule(ModuleDeclaration.WithoutCompartments<NotesControllerModule>()),
            routes => routes.MapGroup("").BelongsTo<NotesControllerModule>().MapControllers().AllowAnonymous().AllowAnyResource()
                .Finally(endpoint =>
                {
                    if (endpoint.Metadata.OfType<ControllerActionDescriptor>().FirstOrDefault()?.ActionName == nameof(NotesController.Operation))
                        endpoint.Metadata.Add(ReadNotes);
                }));

        Assert.Equal("notes.read", await app.GetTestClient().GetStringAsync("/notes/operation", Ct));
    }

    [Fact]
    public async Task The_startup_check_sees_an_action_labelled_outside_any_module()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(
            services => services.AddStellaegisStartupCheck(),
            routes => routes.MapControllers().WithMetadata(ReadNotes).AllowAnonymous().AllowAnyResource()));

        Assert.Contains("notes.read", exception.Message);
    }

    [Fact]
    public async Task An_action_asks_the_authorization_on_its_resource()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisStartupCheck().AddStellaegisModule(ModuleDeclaration.WithoutCompartments<NotesControllerModule>()),
            routes => routes.MapGroup("").BelongsTo<NotesControllerModule>().MapControllers()
                .WithMetadata(ReadNotes).AllowAnonymous().AllowAnyResource());

        Assert.Equal("True", await app.GetTestClient().GetStringAsync("/notes/authorized", Ct));
    }

    private static IEnumerable<Endpoint> ActionEndpoints(WebApplication app)
        => app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null);

    private static Task<WebApplication> StartAsync(
        Action<IServiceCollection> services, Action<WebApplication> map, params Type[] controllers)
        => TestApp.StartAsync(
            s =>
            {
                s.AddAuthorization();
                s.AddControllers().ConfigureApplicationPartManager(parts =>
                {
                    parts.ApplicationParts.Clear();
                    parts.FeatureProviders.Add(new Only(controllers.Length == 0 ? [typeof(NotesController)] : controllers));
                });
                services(s);
            },
            map);

    private sealed class Only(Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in controllers)
                feature.Controllers.Add(controller.GetTypeInfo());
        }
    }
}

public sealed class NotesControllerModule : IStellaegisModule
{
    public static string Name => "notes";
}

[ApiController]
[Route("notes")]
public sealed class NotesController : ControllerBase
{
    [HttpGet("missing")]
    public IResult Missing() => Result.Failure<string>(Error.NotFound("Note.NotFound", "Note jane-1 does not exist.")).ToHttpResult();

    [HttpGet("locked")]
    public string Locked() => throw new UserFacingException(Error.Conflict("Note.Locked", "This note is locked."));

    [HttpGet("space")]
    public string Where([FromServices] IRequestContext context) => context.Space?.ToString() ?? "-";

    [HttpGet("operation")]
    public string Operation() => HttpContext.GetOperation()?.ToString() ?? "-";

    [HttpGet("authorized")]
    public async Task<string> Authorized() => (await HttpContext.AuthorizeResourceAsync(new object())).ToString();
}

public sealed class TagsControllerModule : IStellaegisModule
{
    public static string Name => "tags";
}

[ApiController]
[Route("tags")]
public sealed class TagsController : ControllerBase
{
    [HttpGet]
    public string List() => "[]";
}
