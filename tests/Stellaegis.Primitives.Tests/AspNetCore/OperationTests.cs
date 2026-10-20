using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives.AspNetCore;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Primitives.Tests.AspNetCore;

/// <summary>The identity of the notes module.</summary>
internal sealed class NotesModule : IStellaegisModule
{
    public static string Name => "notes";
}

/// <summary>A module as a consumer sees it: routes labelled with operations, a resource checked on load.</summary>
internal static class NotesEndpoints
{
    public static readonly Operation Read = new("notes", "read", OperationKind.Read);
    public static readonly Operation Write = new("notes", "write", OperationKind.Write);
    public static readonly Operation Purge = new("notes", "purge", OperationKind.Delete);

    public static RouteGroupBuilder MapNotesEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/notes").BelongsTo<NotesModule>().RequireRequestContext();

        // A note's owner is the part of its id before the dash: "jane-1" belongs to jane.
        group.MapGet("/{id}", async (string id, IRequestContext context, HttpContext http) =>
        {
            if (context.Space is not { } space)
                return Results.NotFound();
            var note = new Note(id, space, Actor.Parse(id.Split('-')[0]));
            return await http.AuthorizeResourceAsync(note) ? Results.Text(note.Id) : Results.NotFound();
        }).WithMetadata(Read);
        group.MapPost("/", () => Results.Text("created")).WithMetadata(Write);
        return group;
    }
}

/// <summary>What an application with the notes module registers: its frame, its request context, the module.</summary>
internal static class NotesRegistration
{
    public static IServiceCollection AddNotes(
        this IServiceCollection services, ApplicationFrame? frame = null, Action<RequestContextBuilder>? requestContext = null)
        => services
            .AddStellaegisFrame(frame ?? ApplicationFrame.SingleCompartment("acme"))
            .AddStellaegisRequestContext(requestContext)
            .AddStellaegisModule(ModuleDeclaration.Compartmented<NotesModule>()
                .WithOperations(NotesEndpoints.Read, NotesEndpoints.Write, NotesEndpoints.Purge));
}

internal sealed record Note(string Id, Space Space, Actor Owner);

/// <summary>A consumer's rule: a note is accessible to its owner only.</summary>
internal sealed class OwnerRule : AuthorizationHandler<OperationRequirement, Note>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationRequirement requirement, Note resource)
    {
        Assert.Equal(NotesEndpoints.Read, requirement.Operation);
        if (context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value == resource.Owner.Value)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public class OperationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<WebApplication> StartNotesAsync(
        Action<IServiceCollection> services, Func<RouteGroupBuilder, RouteGroupBuilder> secure, string prefix = "")
        => TestApp.StartAsync(
            s =>
            {
                s.AddNotes();
                services(s);
            },
            app => secure(app.MapGroup(prefix).MapNotesEndpoints()));

    private static void AddRolePolicies(IServiceCollection services) => services.AddAuthorization(options =>
    {
        options.AddPolicy("reader", policy => policy.RequireClaim("role", "reader", "writer"));
        options.AddPolicy("writer", policy => policy.RequireClaim("role", "writer"));
    });

    private static async Task<HttpStatusCode> StatusAsync(WebApplication app, HttpRequestMessage request)
        => (await app.GetTestClient().SendAsync(request, Ct)).StatusCode;

    [Fact]
    public void Operations_compare_by_value()
    {
        Assert.Equal(NotesEndpoints.Read, new Operation("notes", "read", OperationKind.Read));
        Assert.True(NotesEndpoints.Read == new Operation("notes", "read", OperationKind.Read));
        Assert.True(NotesEndpoints.Read != new Operation("notes", "read", OperationKind.Write));
        Assert.Equal("notes.read", NotesEndpoints.Read.ToString());
    }

    [Fact]
    public void An_operation_needs_a_target_a_name_and_a_known_kind()
    {
        Assert.Throws<ArgumentException>(() => new Operation(" ", "read", OperationKind.Read));
        Assert.Throws<ArgumentException>(() => new Operation("notes", "", OperationKind.Read));
        Assert.Throws<ArgumentException>(() => new Operation("notes", "read", (OperationKind)42));
    }

    [Fact]
    public void Target_and_name_follow_the_grammar_of_table_names()
    {
        var operation = new Operation("fiscal_years", "close", OperationKind.Write);
        Assert.Equal("fiscal_years", operation.Target);
        Assert.Equal("fiscal_years.close", operation.ToString());
        Assert.Equal("members.transfer_ownership", new Operation("members", "transfer_ownership", OperationKind.Write).ToString());
        Assert.Equal("a.b", new Operation("a", "b", OperationKind.Read).ToString());
        Assert.Equal(new string('a', 63), new Operation(new string('a', 63), "read", OperationKind.Read).Target);

        Assert.Throws<ArgumentException>(() => new Operation("Invoices", "validate", OperationKind.Write));
        Assert.Throws<ArgumentException>(() => new Operation("fiscal-years", "close", OperationKind.Write));
        Assert.Throws<ArgumentException>(() => new Operation("documents", "replace-content", OperationKind.Write));
        Assert.Throws<ArgumentException>(() => new Operation("invoices", "transfer__ownership", OperationKind.Write));
        Assert.Throws<ArgumentException>(() => new Operation("_invoices", "read", OperationKind.Read));
        Assert.Throws<ArgumentException>(() => new Operation("invoices", "read_", OperationKind.Read));
        Assert.Throws<ArgumentException>(() => new Operation("1invoices", "read", OperationKind.Read));
        Assert.Throws<ArgumentException>(() => new Operation("invoices.lines", "read", OperationKind.Read));
        Assert.Throws<ArgumentException>(() => new Operation("invoices", "read\n", OperationKind.Read));
        Assert.Throws<ArgumentException>(() => new Operation(new string('a', 64), "read", OperationKind.Read));
    }

    [Fact]
    public async Task The_operation_is_read_from_the_endpoint_and_the_request()
    {
        await using var app = await TestApp.StartAsync(_ => { }, routes =>
            routes.MapGet("/op", (HttpContext http) => $"{http.GetOperation()}|{http.GetEndpoint()!.GetOperation()}")
                .WithMetadata(NotesEndpoints.Read));

        Assert.Equal("notes.read|notes.read", await app.GetTestClient().GetStringAsync("/op", Ct));
    }

    [Fact]
    public async Task Policies_are_attached_per_operation()
    {
        await using var app = await StartNotesAsync(AddRolePolicies, group => group
            .RequireAuthorizationPerOperation(op => op.Kind == OperationKind.Read ? "reader" : "writer")
            .AllowAnyResource());

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(app, TestApp.Request("/notes/jane-1")));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(app, TestApp.Request("/notes/jane-1", ("role", "reader"))));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(app, TestApp.Request(HttpMethod.Post, "/notes", ("role", "reader"))));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(app, TestApp.Request(HttpMethod.Post, "/notes", ("role", "writer"))));
    }

    [Fact]
    public async Task A_route_without_operation_falls_to_the_fallback_policy()
    {
        await using var app = await TestApp.StartAsync(
            services => services
                .AddNotes()
                .AddAuthorization(options =>
                {
                    options.AddPolicy("member", policy => policy.RequireAuthenticatedUser());
                    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
                }),
            routes =>
            {
                routes.MapNotesEndpoints().RequireAuthorizationPerOperation(_ => "member").AllowAnyResource();
                routes.MapGet("/forgotten", () => "open");
            });

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(app, TestApp.Request("/forgotten")));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(app, TestApp.Request("/forgotten", ("sub", "jane"))));
    }

    [Fact]
    public async Task The_same_permission_on_another_compartment_is_forbidden()
    {
        await using var app = await TestApp.StartAsync(
            services => services
                .AddNotes(ApplicationFrame.ManyCompartments(), c => c.CompartmentFromRoute("org"))
                .AddSingleton<IAuthorizationHandler, MemberOfCompartmentRule>()
                .AddAuthorization(options => options.AddPolicy("member", policy => policy.AddRequirements(new MemberOfCompartment()))),
            routes => routes.MapGroup("/orgs/{org}").MapNotesEndpoints()
                .RequireAuthorizationPerOperation(_ => "member")
                .AllowAnyResource());

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(app, TestApp.Request("/orgs/acme/notes/jane-1", ("org", "acme"))));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(app, TestApp.Request("/orgs/other/notes/jane-1", ("org", "acme"))));
    }

    [Fact]
    public async Task A_resource_handler_decides_access_to_each_resource()
    {
        await using var app = await StartNotesAsync(
            services => services.AddAuthorization().AddSingleton<IAuthorizationHandler, OwnerRule>(),
            group => group.AllowAnonymous());

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(app, TestApp.Request("/notes/jane-1", (TestApp.NameIdentifier, "jane"))));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(app, TestApp.Request("/notes/john-1", (TestApp.NameIdentifier, "jane"))));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(app, TestApp.Request("/notes/jane-1")));
    }

    [Fact]
    public async Task Allowing_any_resource_skips_the_question()
    {
        await using var app = await StartNotesAsync(_ => { }, group => group.AllowAnyResource().AllowAnonymous());

        Assert.Equal("john-1", await app.GetTestClient().GetStringAsync("/notes/john-1", Ct));
    }

    private static readonly Operation Purge = NotesEndpoints.Purge;

    // The shape of UC-07: the main mapping allows any resource, the database isolating them; the administration
    // mapping holds a purge, which loads nothing, and the host registers no handler for the resources.
    [Fact]
    public async Task A_route_loading_no_resource_needs_neither_a_handler_nor_an_open_declaration()
    {
        await using var app = await TestApp.StartAsync(
            services => services.AddNotes().AddAuthorization(),
            routes =>
            {
                routes.MapNotesEndpoints().RequireAuthorization().AllowAnyResource();
                routes.MapGroup("/admin/notes").BelongsTo<NotesModule>().RequireRequestContext().ActsOnSpaces()
                    .MapDelete("/compartments/{compartment}", () => "purged").WithMetadata(Purge).LoadsNoResource()
                    .AllowAnonymous();
            });

        var response = await app.GetTestClient().DeleteAsync("/admin/notes/compartments/acme", Ct);
        Assert.Equal("purged", await response.Content.ReadAsStringAsync(Ct));
    }

    // A route acting on spaces reads its object from its path, and the actor from the context; the context has no space.
    [Fact]
    public async Task A_route_acting_on_spaces_has_an_actor_and_no_space()
    {
        await using var app = await TestApp.StartAsync(
            services => services.AddNotes().AddAuthorization(),
            routes => routes.MapGroup("/admin/notes").BelongsTo<NotesModule>().RequireRequestContext().ActsOnSpaces().AllowAnonymous()
                .MapDelete("/compartments/{compartment}", (Compartment compartment, IRequestContext context) =>
                {
                    var exception = Assert.Throws<InvalidOperationException>(() => context.Space);
                    Assert.Contains("acts on spaces", exception.Message);
                    return $"{compartment} purged by {context.Actor}";
                }).WithMetadata(Purge).LoadsNoResource());

        var response = await app.GetTestClient().SendAsync(TestApp.Request(HttpMethod.Delete, "/admin/notes/compartments/globex", ("sub", "jane")), Ct);
        Assert.Equal("globex purged by jane", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_route_acting_on_spaces_is_never_in_the_shared_space()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            services => services
                .AddNotes(ApplicationFrame.ManyCompartments().WithSharedSpace(), c => c.CompartmentFromRoute("org"))
                .AddAuthorization(),
            routes => routes.MapGroup("/admin/notes").BelongsTo<NotesModule>().RequireRequestContext(SharedSpaceAcceptance.Accepted)
                .ActsOnSpaces().InSharedSpace().AllowAnonymous()
                .MapDelete("/compartments/{compartment}", () => "purged").WithMetadata(Purge).LoadsNoResource()));

        Assert.Contains("/admin/notes/compartments/{compartment} (mapped in the shared space)", exception.Message);
    }

    [Fact]
    public async Task A_route_acting_on_spaces_belongs_to_a_module_and_carries_an_operation()
    {
        var orphan = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            services => services.AddNotes().AddAuthorization(),
            routes => routes.MapGroup("/admin").RequireRequestContext().ActsOnSpaces().AllowAnonymous()
                .MapDelete("/compartments/{compartment}", () => "purged").LoadsNoResource()));
        Assert.Contains("(no module declared)", orphan.Message);

        var unlabelled = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            services => services.AddNotes().AddAuthorization(),
            routes => routes.MapGroup("/admin/notes").BelongsTo<NotesModule>().RequireRequestContext().ActsOnSpaces().AllowAnonymous()
                .MapDelete("/compartments/{compartment}", () => "purged").LoadsNoResource()));
        Assert.Contains("(no operation)", unlabelled.Message);
    }

    [Fact]
    public async Task A_route_loading_no_resource_still_declares_its_authorization()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            services => services.AddNotes().AddAuthorization(),
            routes => routes.MapGroup("/admin/notes").BelongsTo<NotesModule>().RequireRequestContext()
                .MapDelete("/compartments/{compartment}", () => "purged").WithMetadata(Purge).LoadsNoResource()));

        Assert.Contains("notes.purge", exception.Message);
        Assert.Contains("declare no authorization", exception.Message);
    }

    [Fact]
    public async Task Asking_on_a_route_loading_no_resource_fails()
    {
        await using var app = await TestApp.StartAsync(
            services => services.AddNotes().AddAuthorization(),
            routes => routes.MapGroup("/admin/notes").BelongsTo<NotesModule>().RequireRequestContext()
                .MapDelete("/compartments/{compartment}", async (HttpContext http) => await http.AuthorizeResourceAsync("anything"))
                .WithMetadata(Purge).LoadsNoResource().AllowAnonymous());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.GetTestClient().DeleteAsync("/admin/notes/compartments/acme", Ct));
        Assert.Contains("LoadsNoResource", exception.Message);
    }

    [Fact]
    public async Task Neither_a_handler_nor_an_open_declaration_fails_the_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StartNotesAsync(services => services.AddAuthorization(), group => group.AllowAnonymous()));

        Assert.Contains("notes.read", exception.Message);
        Assert.Contains("AuthorizationHandler<OperationRequirement, TResource>", exception.Message);
        Assert.Contains(".AllowAnyResource()", exception.Message);
    }

    [Fact]
    public async Task A_handler_registered_by_a_factory_is_detected()
    {
        await using var app = await StartNotesAsync(
            services => services.AddAuthorization().AddScoped<IAuthorizationHandler>(_ => new OwnerRule()),
            group => group.AllowAnonymous());

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(app, TestApp.Request("/notes/jane-1", (TestApp.NameIdentifier, "jane"))));
    }

    [Fact]
    public async Task A_handler_written_against_the_interface_counts()
    {
        await using var app = await StartNotesAsync(
            services => services.AddAuthorization().AddSingleton<IAuthorizationHandler, AnythingRule>(),
            group => group.AllowAnonymous());

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(app, TestApp.Request("/notes/john-1")));
    }

    [Fact]
    public async Task A_handler_for_another_requirement_does_not_count()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => StartNotesAsync(
            services => services.AddAuthorization().AddSingleton<IAuthorizationHandler, MemberOfCompartmentRule>(),
            group => group.AllowAnonymous()));
    }

    // The catalogue reads the declarations: an operation on a route and absent from them could never be granted.
    [Fact]
    public async Task A_route_labelled_with_an_operation_its_module_does_not_declare_fails_the_startup()
    {
        var archive = new Operation("notes", "archive", OperationKind.Write);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            services => services.AddNotes().AddAuthorization(),
            routes =>
            {
                var group = routes.MapNotesEndpoints().AllowAnonymous().AllowAnyResource();
                group.MapPost("/archive", () => "archived").WithMetadata(archive);
            }));

        Assert.Contains("operations their module does not declare", exception.Message);
        Assert.Contains("notes.archive", exception.Message);
        Assert.Contains("WithOperations", exception.Message);
        Assert.DoesNotContain("notes.read", exception.Message);
    }

    [Fact]
    public async Task The_same_identity_with_another_kind_is_another_operation()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            services => services.AddNotes().AddAuthorization(),
            routes => routes.MapNotesEndpoints().AllowAnonymous().AllowAnyResource()
                .MapDelete("/all", () => "deleted").WithMetadata(new Operation("notes", "write", OperationKind.Delete))));

        Assert.Contains("notes.write, Delete, of notes", exception.Message);
    }

    [Fact]
    public async Task A_route_without_authorization_fails_the_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StartNotesAsync(services => services.AddAuthorization(), group => group.AllowAnyResource()));

        Assert.Contains("notes.read", exception.Message);
        Assert.Contains("notes.write", exception.Message);
        Assert.Contains("RequireAuthorization", exception.Message);
        Assert.Contains("AllowAnonymous", exception.Message);
    }

    // The fallback policy does not know the operation: a host requiring an authenticated user would open every
    // operation of a module, its purge included, to any user signed in.
    [Fact]
    public async Task A_fallback_policy_does_not_count_as_authorization()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => StartNotesAsync(
            services => services.AddAuthorization(options =>
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()),
            group => group.AllowAnyResource()));
    }

    [Fact]
    public async Task Only_the_routes_without_authorization_are_named()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            services => services.AddNotes().AddAuthorization(),
            routes =>
            {
                var group = routes.MapGroup("/open").BelongsTo<NotesModule>().RequireRequestContext().AllowAnyResource();
                group.MapGet("/secured", () => "secured").WithMetadata(NotesEndpoints.Read).RequireAuthorization();
                group.MapPost("/forgotten", () => "forgotten").WithMetadata(NotesEndpoints.Write);
            }));

        Assert.Contains("notes.write", exception.Message);
        Assert.DoesNotContain("notes.read", exception.Message);
    }

    [Fact]
    public async Task Every_form_of_authorization_counts()
    {
        await using var app = await TestApp.StartAsync(
            services => services.AddNotes().AddAuthorization(),
            routes =>
            {
                var group = routes.MapGroup("/forms").BelongsTo<NotesModule>().RequireRequestContext().AllowAnyResource();
                group.MapGet("/attribute", () => "attribute").WithMetadata(NotesEndpoints.Read).RequireAuthorization();
                group.MapGet("/policy", () => "policy").WithMetadata(NotesEndpoints.Read)
                    .RequireAuthorization(new AuthorizationPolicyBuilder().RequireClaim("role").Build());
                group.MapGet("/builder", () => "builder").WithMetadata(NotesEndpoints.Read)
                    .RequireAuthorization(policy => policy.RequireClaim("role"));
                group.MapGet("/anonymous", () => "anonymous").WithMetadata(NotesEndpoints.Read).AllowAnonymous();
            });

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(app, TestApp.Request("/forms/attribute")));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(app, TestApp.Request("/forms/policy")));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(app, TestApp.Request("/forms/builder")));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(app, TestApp.Request("/forms/anonymous")));
    }

    [Fact]
    public async Task An_unknown_policy_fails_the_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StartNotesAsync(
            AddRolePolicies,
            group => group.RequireAuthorizationPerOperation(op => op.Kind == OperationKind.Read ? "reader" : "writr").AllowAnyResource()));

        Assert.Contains("writr", exception.Message);
        Assert.DoesNotContain("reader", exception.Message);
    }

    [Fact]
    public async Task Asking_on_a_route_without_operation_fails()
    {
        await using var app = await TestApp.StartAsync(_ => { }, routes =>
            routes.MapGet("/unlabelled", async (HttpContext http) => await http.AuthorizeResourceAsync("anything")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.GetTestClient().GetStringAsync("/unlabelled", Ct));
    }

    // ASP.NET Core behaviour this package documents rather than code: a filter added by a final convention is
    // silently ignored. If this test fails, the trap is gone and the warning can go with it.
    [Fact]
    public async Task A_filter_added_by_a_final_convention_is_ignored()
    {
        await using var app = await TestApp.StartAsync(_ => { }, routes =>
        {
            var group = routes.MapGroup("/filtered");
            group.MapGet("/", () => "passed");
            ((IEndpointConventionBuilder)group).Finally(endpoint =>
                endpoint.FilterFactories.Add((_, next) => _ => ValueTask.FromResult<object?>(Results.StatusCode(403))));
        });

        Assert.Equal("passed", await app.GetTestClient().GetStringAsync("/filtered", Ct));
    }

    private sealed class MemberOfCompartment : IAuthorizationRequirement;

    /// <summary>Membership read from an <c>org</c> claim, against the compartment of the request.</summary>
    private sealed class MemberOfCompartmentRule : AuthorizationHandler<MemberOfCompartment>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MemberOfCompartment requirement)
        {
            if (context.Resource is HttpContext http
                && http.RequestServices.GetRequiredService<IRequestContext>().Space?.Compartment?.Value == context.User.FindFirst("org")?.Value)
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>Succeeds on every requirement it is given — written against the interface, so its scope is unknown.</summary>
    private sealed class AnythingRule : IAuthorizationHandler
    {
        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            foreach (var requirement in context.PendingRequirements.ToList())
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
