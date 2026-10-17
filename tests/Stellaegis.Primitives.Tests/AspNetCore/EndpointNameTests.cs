using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Stellaegis.Primitives.AspNetCore;

namespace Stellaegis.Primitives.Tests.AspNetCore;

public class EndpointNameTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // A module naming its routes, mapped once per compartment and once in the shared space.
    private static void MapNamedModuleTwice(IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder Map(RouteGroupBuilder group)
        {
            group.RequireRequestContext("Named", acceptsSharedSpace: true);
            group.MapGet("/", () => "list").WithName("ListItems");
            group.MapPost("/", () => "created").WithName("CreateItem");
            group.MapGet("/{id}", (string id) => id);
            return group;
        }

        Map(routes.MapGroup("/orgs/{org}/items"));
        Map(routes.MapGroup("/shared/items")).InSharedSpace();
    }

    [Fact]
    public async Task A_module_mapped_twice_with_named_routes_fails_the_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            services => services.AddStellaegisRequestContext(c => c.CompartmentFromRoute("org")),
            MapNamedModuleTwice));

        Assert.Contains("ListItems", exception.Message);
        Assert.Contains("CreateItem", exception.Message);
        Assert.Contains("/orgs/{org}/items/", exception.Message);
        Assert.Contains("/shared/items/", exception.Message);
        Assert.DoesNotContain("{id}", exception.Message);
    }

    [Fact]
    public async Task Distinct_names_start()
    {
        await using var app = await TestApp.StartAsync(
            services => services.AddStellaegisRequestContext(c => c.SingleCompartment("main")),
            routes =>
            {
                routes.MapGet("/one", () => "one").WithName("One");
                routes.MapGet("/two", () => "two").WithName("Two");
            });

        Assert.Equal("one", await app.GetTestClient().GetStringAsync("/one", Ct));
    }

    // ASP.NET Core behaviour this check mirrors rather than codes: a duplicate name lets the host start, then fails
    // every request once routing builds. If this test fails, ASP.NET Core changed and the check must follow it.
    [Fact]
    public async Task Without_the_check_a_duplicate_name_fails_every_request()
    {
        await using var app = await TestApp.StartAsync(_ => { }, routes =>
        {
            routes.MapGet("/one", () => "one").WithName("Same");
            routes.MapGet("/two", () => "two").WithName("Same");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.GetTestClient().GetStringAsync("/one", Ct));
    }

    // Same: a route excluded from link generation keeps its name and still collides, so the check makes no exception.
    [Fact]
    public async Task A_name_excluded_from_link_generation_still_collides()
    {
        await using var app = await TestApp.StartAsync(_ => { }, routes =>
        {
            routes.MapGet("/one", () => "one").WithName("Same");
            routes.MapGet("/two", () => "two").WithName("Same").WithMetadata(new SuppressLinkGenerationMetadata());
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.GetTestClient().GetStringAsync("/one", Ct));
    }

    // Same: names differing in case do not collide for ASP.NET Core, so the check lets them through.
    [Fact]
    public async Task Names_differing_in_case_do_not_collide()
    {
        await using var app = await TestApp.StartAsync(
            services => services.AddStellaegisRequestContext(c => c.SingleCompartment("main")),
            routes =>
            {
                routes.MapGet("/one", () => "one").WithName("Same");
                routes.MapGet("/two", () => "two").WithName("same");
            });

        Assert.Equal(HttpStatusCode.OK, (await app.GetTestClient().GetAsync("/two", Ct)).StatusCode);
    }
}
