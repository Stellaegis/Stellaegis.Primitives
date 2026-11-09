using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stellaegis.Primitives.AspNetCore;

namespace Stellaegis.Primitives.Tests.AspNetCore;

/// <summary>The admission of actors: every admission registered is asked, the first refusal wins, and answers 401.</summary>
public class ActorAdmissionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Refuses the subjects it holds, with its own code.</summary>
    private sealed class Refusing(string code, params string[] subjects) : IActorAdmission
    {
        public int Asked { get; private set; }

        public ValueTask<Result> AdmitAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
        {
            Asked++;
            var subject = principal.FindFirst("sub")?.Value;
            return ValueTask.FromResult(subject is not null && subjects.Contains(subject)
                ? Result.Failure(Error.Unauthorized(code, $"{subject} is refused by {code}."))
                : Result.Success());
        }
    }

    private static async Task<WebApplication> StartAsync(params IActorAdmission[] admissions)
        => await TestApp.StartAsync(
            services =>
            {
                foreach (var admission in admissions)
                    services.AddStellaegisActorAdmission(_ => admission);
            },
            app =>
            {
                app.UseAuthentication();
                app.UseStellaegisActorAdmission();
                app.UseStellaegisActorAdmission();   // harmless
                app.MapGet("/ping", () => "pong");
            });

    [Fact]
    public async Task The_first_refusal_wins_and_answers_401_with_its_code()
    {
        var accounts = new Refusing("Account.Blocked", "john");
        var tokens = new Refusing("Token.Revoked", "jane");
        await using var app = await StartAsync(accounts, tokens);
        var client = app.GetTestClient();

        var refused = await client.SendAsync(TestApp.Request("/ping", ("sub", "john")), Ct);
        var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(Ct)).RootElement;
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(401, body.GetProperty("status").GetInt32());
        Assert.Equal("Account.Blocked", body.GetProperty("code").GetString());
        Assert.Equal("john is refused by Account.Blocked.", body.GetProperty("detail").GetString());
        Assert.Equal(0, tokens.Asked);   // the first refusal ends the question

        var revoked = await client.SendAsync(TestApp.Request("/ping", ("sub", "jane")), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal("Token.Revoked", JsonDocument.Parse(await revoked.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_actor_nobody_refuses_goes_on_and_so_does_an_anonymous_request()
    {
        var accounts = new Refusing("Account.Blocked", "john");
        await using var app = await StartAsync(accounts);
        var client = app.GetTestClient();

        var admitted = await client.SendAsync(TestApp.Request("/ping", ("sub", "jane")), Ct);
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        Assert.Equal(1, accounts.Asked);

        var anonymous = await client.GetAsync("/ping", Ct);
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.Equal(1, accounts.Asked);   // nobody to ask about
    }

    [Fact]
    public async Task A_host_without_any_admission_admits_what_the_authentication_admitted()
    {
        await using var app = await StartAsync();

        Assert.Equal(HttpStatusCode.OK, (await app.GetTestClient().SendAsync(TestApp.Request("/ping", ("sub", "john")), Ct)).StatusCode);
    }

    // A second scheme, "bearer", reading the X-Bearer header; the default stays "cookie", reading X-Cookie.
    private sealed class HeaderScheme(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Scheme.Name == "bearer" ? "X-Bearer" : "X-Cookie";
            return Task.FromResult(Request.Headers.TryGetValue(header, out var value)
                ? AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", value.ToString())], Scheme.Name)), Scheme.Name))
                : AuthenticateResult.NoResult());
        }
    }

    private static async Task<WebApplication> StartTwoSchemesAsync(Action<IServiceCollection>? services = null, Action<WebApplication>? pipeline = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("cookie")
            .AddScheme<AuthenticationSchemeOptions, HeaderScheme>("cookie", null)
            .AddScheme<AuthenticationSchemeOptions, HeaderScheme>("bearer", null);
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("api", policy => policy.AddAuthenticationSchemes("bearer").RequireAuthenticatedUser()));
        builder.Services.AddStellaegisActorAdmission(_ => new Refusing("Token.Revoked", "jane"));
        services?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseAuthentication();
        (pipeline ?? (a => a.UseStellaegisActorAdmission()))(app);
        app.UseAuthorization();
        app.MapGet("/api", () => "served").RequireAuthorization("api");
        app.MapGet("/api/open", (HttpContext http) => http.User.FindFirst("sub")?.Value ?? "nobody")
            .RequireAuthorization("api").AllowAnonymous();
        await app.StartAsync(Ct);
        return app;
    }

    private static HttpRequestMessage Bearer(string path, string subject)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Bearer", subject);
        return request;
    }

    // A policy naming its own schemes authenticates again, after the door: measured before 5.6.0, the revoked bearer
    // of jane was served, 200, on a host whose default scheme is the cookie.
    [Fact]
    public async Task A_principal_a_policy_authenticates_with_its_own_schemes_meets_the_admission()
    {
        await using var app = await StartTwoSchemesAsync();
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Bearer("/api", "jane"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Bearer("/api", "john"), Ct)).StatusCode);
        Assert.Equal("nobody", await (await client.SendAsync(Bearer("/api/open", "jane"), Ct)).Content.ReadAsStringAsync(Ct));
        Assert.Equal("john", await (await client.SendAsync(Bearer("/api/open", "john"), Ct)).Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Admissions_registered_and_never_asked_fail_the_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StartTwoSchemesAsync(pipeline: _ => { }));

        Assert.Contains("UseStellaegisActorAdmission()", exception.Message);
    }

    // The examples of the family put the door on every route but the protocol routes of the issuer: a branch counts.
    [Fact]
    public async Task The_admission_on_a_branch_of_the_pipeline_counts()
    {
        await using var app = await StartTwoSchemesAsync(pipeline: a =>
            a.UseWhen(context => !context.Request.Path.StartsWithSegments("/connect"), door => door.UseStellaegisActorAdmission()));

        Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetTestClient().SendAsync(Bearer("/api", "jane"), Ct)).StatusCode);
    }

    private sealed class OwnEvaluator(IAuthorizationService authorization) : IPolicyEvaluator
    {
        private readonly PolicyEvaluator _inner = new(authorization);

        public Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context) => _inner.AuthenticateAsync(policy, context);

        public Task<PolicyAuthorizationResult> AuthorizeAsync(AuthorizationPolicy policy, AuthenticateResult authenticationResult, HttpContext context, object? resource)
            => _inner.AuthorizeAsync(policy, authenticationResult, context, resource);
    }

    [Fact]
    public async Task A_policy_evaluator_replaced_after_the_admissions_fails_the_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StartTwoSchemesAsync(services => services.AddTransient<IPolicyEvaluator, OwnEvaluator>()));

        Assert.Contains("the policy evaluator is replaced", exception.Message);
    }

    // A token issuer asks the admissions itself and may have no authorization: its container, validated at build in
    // Development, still builds — measured, it refused the evaluator it could not construct.
    [Fact]
    public void A_host_without_authorization_builds_its_container_validated()
    {
        var services = new ServiceCollection().AddLogging()
            .AddStellaegisActorAdmission(_ => new Refusing("Account.Blocked", "john"))
            .AddStellaegisActorAdmissions();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.NotNull(provider.GetRequiredService<IActorAdmissions>());
    }

    [Fact]
    public async Task The_question_is_asked_apart_from_the_middleware_by_a_token_issuer()
    {
        using var provider = new ServiceCollection()
            .AddStellaegisActorAdmission(_ => new Refusing("Account.Blocked", "john"))
            .AddStellaegisActorAdmissions()
            .BuildServiceProvider();
        var admissions = provider.GetRequiredService<IActorAdmissions>();

        var john = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "john")], "test"));
        var jane = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "jane")], "test"));
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Equal("Account.Blocked", (await admissions.AdmitAsync(john, Ct)).Error!.Code);
        Assert.True((await admissions.AdmitAsync(jane, Ct)).IsSuccess);
        Assert.True((await admissions.AdmitAsync(anonymous, Ct)).IsSuccess);
        Assert.Single(provider.GetServices<IActorAdmission>());
    }
}
