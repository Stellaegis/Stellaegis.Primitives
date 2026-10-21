using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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
