using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Stellaegis.Primitives.Tests.AspNetCore;

/// <summary>
/// An ASP.NET Core application on a <see cref="TestServer"/>. A request is authenticated from its headers:
/// each <c>X-Claim-{type}</c> header becomes a claim of that type, and the user is authenticated as soon as
/// one is present. A header name cannot carry a URI, so <see cref="NameIdentifier"/> stands for
/// <see cref="ClaimTypes.NameIdentifier"/>.
/// </summary>
internal static class TestApp
{
    private const string ClaimHeaderPrefix = "X-Claim-";

    public const string NameIdentifier = "nameid";

    public static async Task<WebApplication> StartAsync(Action<IServiceCollection> services, Action<WebApplication> map)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        services(builder.Services);

        var app = builder.Build();
        app.Use(AuthenticateFromHeaders);
        map(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    public static HttpRequestMessage Request(string path, params (string Type, string Value)[] claims)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        foreach (var (type, value) in claims)
            request.Headers.Add(ClaimHeaderPrefix + type, value);
        return request;
    }

    private static Task AuthenticateFromHeaders(HttpContext context, Func<Task> next)
    {
        var claims = context.Request.Headers
            .Where(header => header.Key.StartsWith(ClaimHeaderPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(header => (Type: header.Key[ClaimHeaderPrefix.Length..], Value: header.Value.ToString()))
            .Select(claim => new Claim(claim.Type == NameIdentifier ? ClaimTypes.NameIdentifier : claim.Type, claim.Value))
            .ToList();
        if (claims.Count > 0)
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
        return next();
    }
}
