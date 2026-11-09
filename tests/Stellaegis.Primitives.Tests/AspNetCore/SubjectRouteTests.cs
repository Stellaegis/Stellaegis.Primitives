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

/// <summary>The identity of a module that holds people, without compartments.</summary>
internal sealed class PeopleModule : IStellaegisModule
{
    public static string Name => "people";
}

/// <summary>A route that acts on a person: it loads the subject and asks about it, as an accounts module does.</summary>
internal static class PeopleEndpoints
{
    public static readonly Operation Block = new("people", "block", OperationKind.Write);

    public static RouteGroupBuilder MapPeopleEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/people").BelongsTo<PeopleModule>();
        group.MapPost("/{sub}/block", async (string sub, HttpContext http) =>
            Subject.TryParse(sub, out var subject) && await http.AuthorizeResourceAsync(subject)
                ? Results.Text("blocked")
                : Results.NotFound())
            .WithMetadata(Block);
        return group;
    }
}

/// <summary>
/// A rights system's rule on people, apart from the module: nobody acts on themselves, and the administrator is
/// reached by nobody.
/// </summary>
internal sealed class PeopleRule : AuthorizationHandler<OperationRequirement, Subject>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationRequirement requirement, Subject resource)
    {
        Assert.Equal(PeopleEndpoints.Block, requirement.Operation);
        if (Actor.TryParse(context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var actor)
            && !resource.Is(actor)
            && resource.Value != "admin")
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public class SubjectRouteTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<WebApplication> StartAsync(Action<IServiceCollection> rule)
        => TestApp.StartAsync(
            services =>
            {
                services.AddStellaegisFrame(ApplicationFrame.SingleCompartment("acme"));
                services.AddStellaegisModule(ModuleDeclaration.WithoutCompartments<PeopleModule>().WithOperations(PeopleEndpoints.Block));
                services.AddStellaegisStartupCheck();
                services.AddAuthorization();
                rule(services);
            },
            routes => routes.MapPeopleEndpoints().RequireAuthorization());

    private static async Task<HttpStatusCode> BlockAsync(WebApplication app, string target, string caller)
        => (await app.GetTestClient().SendAsync(
            TestApp.Request(HttpMethod.Post, $"/people/{target}/block", (TestApp.NameIdentifier, caller)), Ct)).StatusCode;

    [Fact]
    public async Task The_rule_sees_whom_a_gesture_reaches_not_only_what_it_is()
    {
        await using var app = await StartAsync(services => services.AddSingleton<IAuthorizationHandler, PeopleRule>());

        Assert.Equal(HttpStatusCode.OK, await BlockAsync(app, "john", "jane"));
        Assert.Equal(HttpStatusCode.NotFound, await BlockAsync(app, "jane", "jane"));    // on herself
        Assert.Equal(HttpStatusCode.NotFound, await BlockAsync(app, "admin", "jane"));   // above her
        Assert.Equal(HttpStatusCode.NotFound, await BlockAsync(app, "job:purge", "jane"));   // no gesture acts upon a job
    }

    [Fact]
    public async Task Without_a_rule_on_people_no_person_is_reached()
    {
        // A handler for other resources passes the startup check; the gesture is refused when it asks.
        await using var app = await StartAsync(services => services.AddSingleton<IAuthorizationHandler, OwnerRule>());

        Assert.Equal(HttpStatusCode.NotFound, await BlockAsync(app, "john", "jane"));
    }
}
