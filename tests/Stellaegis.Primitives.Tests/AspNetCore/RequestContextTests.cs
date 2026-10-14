using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives.AspNetCore;

namespace Stellaegis.Primitives.Tests.AspNetCore;

public class RequestContextTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Answers "compartment|actor", "-" standing for an unresolved compartment.
    private static void MapEcho(IEndpointRouteBuilder routes, string pattern = "/echo")
        => routes.MapGet(pattern, (IRequestContext context) => $"{context.Compartment?.Value ?? "-"}|{context.Actor.Value}");

    private static async Task<string> EchoAsync(Action<RequestContextBuilder> configure, HttpRequestMessage request, string pattern = "/echo")
    {
        await using var app = await TestApp.StartAsync(
            services => services.AddStellaegisRequestContext(configure),
            routes => MapEcho(routes, pattern));
        var response = await app.GetTestClient().SendAsync(request, Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(Ct);
    }

    [Fact]
    public async Task A_single_compartment_and_an_anonymous_request()
        => Assert.Equal("main|anonymous", await EchoAsync(c => c.SingleCompartment("main"), TestApp.Request("/echo")));

    [Fact]
    public async Task The_actor_defaults_to_the_name_identifier_claim()
        => Assert.Equal("main|jane", await EchoAsync(
            c => c.SingleCompartment("main"),
            TestApp.Request("/echo", (TestApp.NameIdentifier, "jane"), ("sub", "other"))));

    [Fact]
    public async Task The_actor_falls_back_to_the_sub_claim()
        => Assert.Equal("main|jane", await EchoAsync(c => c.SingleCompartment("main"), TestApp.Request("/echo", ("sub", "jane"))));

    [Fact]
    public async Task The_compartment_comes_from_a_route_value()
        => Assert.Equal("acme|anonymous", await EchoAsync(
            c => c.CompartmentFromRoute("org"), TestApp.Request("/orgs/acme"), "/orgs/{org}"));

    [Fact]
    public async Task An_invalid_route_value_leaves_the_compartment_unresolved()
        => Assert.Equal("-|anonymous", await EchoAsync(
            c => c.CompartmentFromRoute("org"), TestApp.Request("/orgs/Acme"), "/orgs/{org}"));

    [Fact]
    public async Task The_compartment_comes_from_a_claim()
        => Assert.Equal("acme|jane", await EchoAsync(
            c => c.CompartmentFromClaim("org"), TestApp.Request("/echo", ("org", "acme"), ("sub", "jane"))));

    [Fact]
    public async Task The_compartment_comes_from_a_function_of_the_request()
    {
        var request = TestApp.Request("/echo");
        request.Headers.Add("X-Compartment", "acme");

        Assert.Equal("acme|anonymous", await EchoAsync(
            c => c.CompartmentFrom(http =>
                Compartment.TryParse(http.Request.Headers["X-Compartment"].ToString(), out var compartment) ? compartment : null),
            request));
    }

    [Fact]
    public async Task The_actor_comes_from_a_chosen_claim()
        => Assert.Equal("main|0000-oid", await EchoAsync(
            c => c.SingleCompartment("main").ActorFromClaim("oid"),
            TestApp.Request("/echo", (TestApp.NameIdentifier, "jane"), ("oid", "0000-oid"))));

    [Fact]
    public async Task The_actor_comes_from_a_function_of_the_request()
        => Assert.Equal("main|svc:billing", await EchoAsync(
            c => c.SingleCompartment("main").ActorFrom(http =>
                http.User.FindFirst("client_id")?.Value is { } client ? Actor.Parse("svc:" + client) : null),
            TestApp.Request("/echo", ("client_id", "billing"))));

    [Fact]
    public async Task The_anonymous_label_is_configurable()
        => Assert.Equal("main|guest", await EchoAsync(c => c.SingleCompartment("main").AnonymousActor("guest"), TestApp.Request("/echo")));

    [Fact]
    public async Task An_authenticated_user_without_the_actor_claim_fails_the_request()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => EchoAsync(
            c => c.SingleCompartment("main").ActorFromClaim("oid"),
            TestApp.Request("/echo", (TestApp.NameIdentifier, "jane"))));

    [Fact]
    public async Task An_actor_claim_that_is_not_a_valid_actor_fails_the_request()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => EchoAsync(
            c => c.SingleCompartment("main"),
            TestApp.Request("/echo", (TestApp.NameIdentifier, new string('a', Actor.MaxLength + 1)))));

    [Fact]
    public void A_compartment_source_is_required()
        => Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddStellaegisRequestContext(c => c.ActorFromClaim("oid")));

    [Fact]
    public void A_single_compartment_source_is_allowed()
        => Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddStellaegisRequestContext(c => c.SingleCompartment("main").CompartmentFromRoute("org")));

    [Fact]
    public void A_single_request_context_is_registered()
    {
        var services = new ServiceCollection().AddStellaegisRequestContext(c => c.SingleCompartment("main"));

        Assert.Throws<InvalidOperationException>(() => services.AddStellaegisRequestContext<FixedRequestContext>());
    }

    [Fact]
    public void The_request_context_is_scoped()
    {
        var services = new ServiceCollection()
            .AddStellaegisRequestContext(c => c.SingleCompartment("main"));

        Assert.Equal(ServiceLifetime.Scoped, services.Single(d => d.ServiceType == typeof(IRequestContext)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, new ServiceCollection().AddStellaegisRequestContext<FixedRequestContext>()
            .Single(d => d.ServiceType == typeof(IRequestContext)).Lifetime);
    }

    [Fact]
    public async Task An_adapter_of_the_application_serves_the_endpoints()
    {
        await using var app = await TestApp.StartAsync(
            services => services.AddStellaegisRequestContext<FixedRequestContext>(),
            routes => MapEcho(routes));

        Assert.Equal("platform|system", await app.GetTestClient().GetStringAsync("/echo", Ct));
    }

    [Fact]
    public async Task Mapping_a_module_without_a_request_context_fails_the_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestApp.StartAsync(
            _ => { },
            routes => routes.RequireRequestContext("Documents")));

        Assert.Contains("Documents endpoints need a request context", exception.Message);
        Assert.Contains("AddStellaegisRequestContext", exception.Message);
    }

    [Fact]
    public async Task Mapping_a_module_with_a_request_context_passes()
    {
        await using var app = await TestApp.StartAsync(
            services => services.AddStellaegisRequestContext(c => c.SingleCompartment("main")),
            routes => MapEcho(routes.RequireRequestContext("Documents")));

        Assert.Equal("main|anonymous", await app.GetTestClient().GetStringAsync("/echo", Ct));
    }

    [Fact]
    public async Task Reading_the_context_outside_a_request_fails()
    {
        await using var app = await TestApp.StartAsync(
            services => services.AddStellaegisRequestContext(c => c.SingleCompartment("main")),
            _ => { });
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IRequestContext>();

        Assert.Throws<InvalidOperationException>(() => context.Compartment);
    }

    [Fact]
    public async Task Concurrent_requests_each_receive_their_own_context()
    {
        await using var app = await TestApp.StartAsync(
            services => services.AddStellaegisRequestContext(c => c.CompartmentFromRoute("org")),
            routes => routes.MapGet("/orgs/{org}", async (IRequestContext context) =>
            {
                var compartment = context.Compartment;
                await Task.Yield();
                return $"{compartment?.Value}|{context.Actor.Value}";
            }));
        var client = app.GetTestClient();

        var answers = await Task.WhenAll(Enumerable.Range(0, 200).Select(async i =>
        {
            var response = await client.SendAsync(TestApp.Request($"/orgs/org{i}", (TestApp.NameIdentifier, $"user{i}")), Ct);
            return (Expected: $"org{i}|user{i}", Actual: await response.Content.ReadAsStringAsync(Ct));
        }));

        Assert.All(answers, answer => Assert.Equal(answer.Expected, answer.Actual));
    }

    [Fact]
    public async Task A_compartment_binds_as_a_route_parameter()
    {
        await using var app = await TestApp.StartAsync(
            _ => { },
            routes => routes.MapGet("/orgs/{org}", (Compartment org) => org.Value));
        var client = app.GetTestClient();

        Assert.Equal("acme", await client.GetStringAsync("/orgs/acme", Ct));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await client.GetAsync("/orgs/Acme", Ct)).StatusCode);
    }

    private sealed class FixedRequestContext : IRequestContext
    {
        public Compartment? Compartment { get; } = Compartment.Parse("platform");
        public Actor Actor { get; } = Actor.Parse("system");
    }
}
