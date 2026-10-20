using System.Net;
using System.Reflection;
using System.Text.Json;
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
/// A module written with MVC controllers: what the package gives it through a group of routes, through attributes,
/// and the ASP.NET Core behaviours both rely on — a filter on a group reaches an action, <c>MapControllers()</c> maps
/// every controller of the application.
/// </summary>
public class ControllerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static readonly Operation ReadNotes = new("notes", "read", OperationKind.Read);
    internal static readonly Operation WriteNotes = new("notes", "write", OperationKind.Write);
    internal static readonly string NotAnOperation = "notes.read";

    private static readonly ApplicationFrame Acme = ApplicationFrame.SingleCompartment("acme");
    private static readonly ModuleDeclaration Notes = ModuleDeclaration.WithoutCompartments<NotesControllerModule>();
    private static readonly ModuleDeclaration CompartmentedNotes = ModuleDeclaration.Compartmented<NotesControllerModule>();

    // Through a group of routes

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
            services => services.AddStellaegisStartupCheck().AddStellaegisModule(Notes),
            routes => routes.MapGroup("/api").BelongsTo<NotesControllerModule>().MapControllers().AllowAnonymous());

        Assert.Equal("-", await app.GetTestClient().GetStringAsync("/api/notes/operation", Ct));
        Assert.All(ActionEndpoints(app), endpoint => Assert.Equal("notes", endpoint.GetModule()?.Name));
    }

    [Fact]
    public async Task MapControllers_in_a_group_maps_the_controllers_of_every_module()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisStartupCheck().AddStellaegisModule(Notes),
            routes => routes.MapGroup("/api").BelongsTo<NotesControllerModule>().MapControllers().AllowAnonymous(),
            typeof(NotesController), typeof(LabelsController));

        Assert.Equal("notes", ActionOf(app, "Labels").GetModule()?.Name);
    }

    [Fact]
    public async Task A_module_on_a_controller_prevails_over_its_group()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisStartupCheck()
                .AddStellaegisModule(Notes)
                .AddStellaegisModule(ModuleDeclaration.WithoutCompartments<TagsControllerModule>()),
            routes => routes.MapGroup("/api").BelongsTo<NotesControllerModule>().MapControllers().AllowAnonymous(),
            typeof(NotesController), typeof(TagsController));

        Assert.Equal("tags", ActionOf(app, "Tags").GetModule()?.Name);
    }

    [Fact]
    public async Task MapControllers_in_two_groups_maps_every_controller_twice()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisStartupCheck()
                .AddStellaegisModule(Notes)
                .AddStellaegisModule(ModuleDeclaration.WithoutCompartments<TagsControllerModule>()),
            routes =>
            {
                routes.MapGroup("/n").BelongsTo<NotesControllerModule>().MapControllers().AllowAnonymous();
                routes.MapGroup("/t").BelongsTo<TagsControllerModule>().MapControllers().AllowAnonymous();
            },
            typeof(NotesController), typeof(LabelsController));

        Assert.Equal(2, ActionEndpoints(app).Count(endpoint => ControllerOf(endpoint) == "Labels"));
        Assert.Equal("-", await app.GetTestClient().GetStringAsync("/t/notes/operation", Ct));
    }

    [Fact]
    public async Task A_group_declares_the_request_context_of_the_actions_it_maps()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisFrame(Acme).AddStellaegisRequestContext().AddStellaegisModule(CompartmentedNotes),
            routes => routes.MapGroup("").BelongsTo<NotesControllerModule>().RequireRequestContext().MapControllers().AllowAnonymous());

        Assert.Equal("acme", await app.GetTestClient().GetStringAsync("/notes/space", Ct));
    }

    [Fact]
    public async Task An_operation_given_to_all_the_controllers_reaches_an_action()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisStartupCheck().AddStellaegisModule(Notes),
            routes => routes.MapGroup("").BelongsTo<NotesControllerModule>().MapControllers()
                .WithMetadata(ReadNotes).AllowAnonymous().AllowAnyResource());

        Assert.Equal("notes.read", await app.GetTestClient().GetStringAsync("/notes/operation", Ct));
        Assert.Equal("True", await app.GetTestClient().GetStringAsync("/notes/authorized", Ct));
    }

    [Fact]
    public async Task The_startup_check_sees_an_action_labelled_outside_any_module()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(
            services => services.AddStellaegisStartupCheck(),
            routes => routes.MapControllers().WithMetadata(ReadNotes).AllowAnonymous().AllowAnyResource()));

        Assert.Contains("notes.read", exception.Message);
    }

    // Through attributes

    [Fact]
    public async Task Each_controller_carries_its_own_module_under_a_single_MapControllers()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisFrame(Acme).AddStellaegisRequestContext()
                .AddStellaegisModule(CompartmentedNotes)
                .AddStellaegisModule(ModuleDeclaration.WithoutCompartments<TagsControllerModule>()),
            routes => routes.MapControllers().AllowAnonymous().AllowAnyResource(),
            typeof(AttributedNotesController), typeof(TagsController));

        Assert.Equal("notes", ActionOf(app, "AttributedNotes").GetModule()?.Name);
        Assert.Equal("tags", ActionOf(app, "Tags").GetModule()?.Name);
        Assert.Equal("acme", await app.GetTestClient().GetStringAsync("/anotes/space", Ct));
    }

    [Fact]
    public async Task A_module_on_a_controller_must_be_declared()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(
            services => services.AddStellaegisStartupCheck(),
            routes => routes.MapControllers().AllowAnonymous(),
            typeof(TagsController)));

        Assert.Contains("modules nothing declares: tags", exception.Message);
    }

    [Fact]
    public async Task An_operation_named_on_an_action_reaches_it_and_prevails_over_its_controller()
    {
        await using var app = await StartAsync(
            services => services.AddStellaegisFrame(Acme).AddStellaegisRequestContext().AddStellaegisModule(CompartmentedNotes),
            routes => routes.MapControllers().AllowAnonymous().AllowAnyResource(),
            typeof(AttributedNotesController));

        Assert.Equal("notes.read", await app.GetTestClient().GetStringAsync("/anotes/operation", Ct));
        Assert.Equal("notes.write", await app.GetTestClient().GetStringAsync("/anotes/write", Ct));
    }

    [Fact]
    public async Task A_policy_per_operation_reaches_an_operation_named_by_attribute()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(
            services => services.AddStellaegisFrame(Acme).AddStellaegisRequestContext().AddStellaegisModule(CompartmentedNotes),
            routes => routes.MapControllers().RequireAuthorizationPerOperation(op => op.ToString()).AllowAnyResource(),
            typeof(AttributedNotesController)));

        Assert.Contains("notes.write", exception.Message);
    }

    [Fact]
    public async Task An_operation_attribute_naming_no_operation_fails_the_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(
            services => services.AddStellaegisStartupCheck().AddStellaegisModule(Notes),
            routes => routes.MapControllers().AllowAnonymous().AllowAnyResource(),
            typeof(MislabelledController)));

        Assert.Contains("names no static field or property holding an Operation", exception.Message);
    }

    [Fact]
    public async Task Reading_the_request_context_without_one_registered_fails_the_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(
            services => services.AddStellaegisFrame(Acme).AddStellaegisStartupCheck().AddStellaegisModule(CompartmentedNotes),
            routes => routes.MapControllers().AllowAnonymous().AllowAnyResource(),
            typeof(AttributedNotesController)));

        Assert.Contains("read the request context, and none is registered", exception.Message);
        Assert.Contains("AttributedNotes", exception.Message);
    }

    [Fact]
    public async Task Reading_the_request_context_from_a_module_without_compartments_fails_the_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(
            services => services.AddStellaegisFrame(Acme).AddStellaegisRequestContext().AddStellaegisModule(Notes),
            routes => routes.MapControllers().AllowAnonymous().AllowAnyResource(),
            typeof(AttributedNotesController)));

        Assert.Contains("declared without compartments", exception.Message);
    }

    [Fact]
    public async Task The_shared_space_needs_a_controller_that_accepts_it()
    {
        Task<WebApplication> StartInSharedSpace(Type controller) => StartAsync(
            services => services.AddStellaegisFrame(ApplicationFrame.ManyCompartments().WithSharedSpace())
                .AddStellaegisRequestContext(c => c.CompartmentFromRoute("org"))
                .AddStellaegisModule(CompartmentedNotes),
            routes => routes.MapGroup("/shared").MapControllers().AllowAnonymous().AllowAnyResource().InSharedSpace(),
            controller);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StartInSharedSpace(typeof(AttributedNotesController)));
        Assert.Contains("does not accept the shared space", exception.Message);

        await using var app = await StartInSharedSpace(typeof(SharedNotesController));
        Assert.Equal("(shared)", await app.GetTestClient().GetStringAsync("/shared/snotes/space", Ct));
    }

    // Results for an action returning IActionResult

    [Fact]
    public async Task ToActionResult_answers_the_value_or_the_error()
    {
        await using var app = await StartAsync(services => { }, routes => routes.MapControllers().AllowAnonymous());
        var client = app.GetTestClient();

        Assert.Equal("jane-1", await client.GetStringAsync("/notes/action/found", Ct));
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/notes/action/done", Ct)).StatusCode);

        var failure = await client.GetAsync("/notes/action/missing", Ct);
        Assert.Equal(HttpStatusCode.NotFound, failure.StatusCode);
        Assert.Equal("Note.NotFound", (await failure.ReadErrorAsync(Ct))!.Code);
    }

    [Fact]
    public async Task ToActionResult_hides_the_message_of_a_failure_outside_development()
    {
        await using var app = await StartAsync(services => { }, routes => routes.MapControllers().AllowAnonymous());

        var response = await app.GetTestClient().GetAsync("/notes/action/failure", Ct);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(body.TryGetProperty("detail", out _));
        Assert.Equal("Db.Timeout", body.GetProperty("code").GetString());
    }

    private static IEnumerable<Endpoint> ActionEndpoints(WebApplication app)
        => app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null);

    private static Endpoint ActionOf(WebApplication app, string controller)
        => ActionEndpoints(app).First(endpoint => ControllerOf(endpoint) == controller);

    private static string ControllerOf(Endpoint endpoint)
        => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName;

    // Only the controllers named — NotesController unless stated — so that each application maps what it tests.
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

public sealed class TagsControllerModule : IStellaegisModule
{
    public static string Name => "tags";
}

// A controller declaring nothing: what it gets comes from the group it is mapped in.
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

    [HttpGet("action/found")]
    public IActionResult Found() => Result.Success("jane-1").ToActionResult();

    [HttpGet("action/done")]
    public IActionResult Done() => Result.Success().ToActionResult();

    [HttpGet("action/missing")]
    public IActionResult MissingAction()
    {
        var result = Result.Failure<string>(Error.NotFound("Note.NotFound", "Note jane-1 does not exist."));
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }

    [HttpGet("action/failure")]
    public IActionResult Failure() => Result.Failure(Error.Failure("Db.Timeout", "Timeout on 10.0.0.4.")).ToActionResult();
}

[ApiController]
[Route("anotes")]
[BelongsTo<NotesControllerModule>]
[RequiresRequestContext]
[Operation(typeof(ControllerTests), nameof(ControllerTests.ReadNotes))]
public sealed class AttributedNotesController : ControllerBase
{
    [HttpGet("space")]
    public string Where([FromServices] IRequestContext context) => context.Space?.ToString() ?? "-";

    [HttpGet("operation")]
    public string Operation() => HttpContext.GetOperation()?.ToString() ?? "-";

    [HttpGet("write")]
    [Operation(typeof(ControllerTests), nameof(ControllerTests.WriteNotes))]
    public string Write() => HttpContext.GetOperation()?.ToString() ?? "-";
}

[ApiController]
[Route("snotes")]
[BelongsTo<NotesControllerModule>]
[RequiresRequestContext(AcceptsSharedSpace = true)]
public sealed class SharedNotesController : ControllerBase
{
    [HttpGet("space")]
    public string Where([FromServices] IRequestContext context) => context.Space?.ToString() ?? "-";
}

[ApiController]
[Route("tags")]
[BelongsTo<TagsControllerModule>]
public sealed class TagsController : ControllerBase
{
    [HttpGet]
    public string List() => "[]";
}

// A second controller declaring nothing, as another module's would be.
[ApiController]
[Route("labels")]
public sealed class LabelsController : ControllerBase
{
    [HttpGet]
    public string List() => "[]";
}

[ApiController]
[Route("mislabelled")]
[BelongsTo<NotesControllerModule>]
public sealed class MislabelledController : ControllerBase
{
    [HttpGet]
    [Operation(typeof(ControllerTests), nameof(ControllerTests.NotAnOperation))]
    public string Get() => "-";
}
