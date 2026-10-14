using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Stellaegis.Primitives.Tests.AspNetCore;

/// <summary>
/// An ASP.NET Core application on a <see cref="TestServer"/>, with an authentication scheme reading the
/// request headers: each <c>X-Claim-{type}</c> header becomes a claim of that type, and the user is
/// authenticated as soon as one is present. A header name cannot carry a URI, so <see cref="NameIdentifier"/>
/// stands for <see cref="ClaimTypes.NameIdentifier"/>.
/// </summary>
internal static class TestApp
{
    public const string NameIdentifier = "nameid";

    private const string ClaimHeaderPrefix = "X-Claim-";
    private const string SchemeName = "headers";

    /// <summary>Starts the application in <paramref name="environment"/>, Production unless stated, whatever the machine sets.</summary>
    public static async Task<WebApplication> StartAsync(
        Action<IServiceCollection> services, Action<WebApplication> map, string environment = "Production")
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication(SchemeName).AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(SchemeName, null);
        services(builder.Services);

        var app = builder.Build();
        map(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    public static HttpRequestMessage Request(string path, params (string Type, string Value)[] claims)
        => Request(HttpMethod.Get, path, claims);

    public static HttpRequestMessage Request(HttpMethod method, string path, params (string Type, string Value)[] claims)
    {
        var request = new HttpRequestMessage(method, path);
        foreach (var (type, value) in claims)
            request.Headers.Add(ClaimHeaderPrefix + type, value);
        return request;
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = Request.Headers
                .Where(header => header.Key.StartsWith(ClaimHeaderPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(header => (Type: header.Key[ClaimHeaderPrefix.Length..], Value: header.Value.ToString()))
                .Select(claim => new Claim(claim.Type == NameIdentifier ? ClaimTypes.NameIdentifier : claim.Type, claim.Value))
                .ToList();
            return Task.FromResult(claims.Count == 0
                ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }
}
