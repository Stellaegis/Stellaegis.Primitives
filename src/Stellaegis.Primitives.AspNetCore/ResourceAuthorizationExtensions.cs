using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>The question a module asks before giving access to a resource it loaded.</summary>
public static class ResourceAuthorizationExtensions
{
    /// <summary>
    /// Whether the user of the request may access <paramref name="resource"/> for the operation of the route —
    /// asked on each resource, each item of a list included. A refusal is answered as an absence, 404:
    /// <code>
    /// if (document is null || !await http.AuthorizeResourceAsync(document))
    ///     return Results.NotFound();
    /// </code>
    /// </summary>
    /// <remarks>
    /// Always <see langword="true"/> when the consumer declared <c>AllowAnyResource</c>. Otherwise ASP.NET Core's
    /// resource-based authorization answers, with the route's <see cref="Operation"/> as the requirement: a
    /// handler of the consumer — an <c>AuthorizationHandler&lt;Operation, TResource&gt;</c> — must succeed, and
    /// refusal is the default.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The route is not labelled with an <see cref="Operation"/>, or declares it loads no resource.
    /// </exception>
    public static async Task<bool> AuthorizeResourceAsync(this HttpContext httpContext, object resource)
    {
        Guard.NotNull(httpContext);
        Guard.NotNull(resource);

        var endpoint = httpContext.GetEndpoint();
        var operation = endpoint?.GetOperation()
            ?? throw new InvalidOperationException(
                $"The route {endpoint?.DisplayName ?? httpContext.Request.Path} asks the authorization on a resource "
                + "without being labelled with an Operation.");
        if (endpoint!.Metadata.GetMetadata<LoadsNoResourceMetadata>() is not null)
        {
            throw new InvalidOperationException(
                $"The route {endpoint.DisplayName} asks the authorization on a resource, and declares it loads none "
                + "(LoadsNoResource). Remove the declaration, or the question.");
        }
        if (endpoint.Metadata.GetMetadata<AllowAnyResourceMetadata>() is not null)
            return true;

        var authorization = httpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        var result = await authorization.AuthorizeAsync(httpContext.User, resource, operation);
        return result.Succeeded;
    }
}
