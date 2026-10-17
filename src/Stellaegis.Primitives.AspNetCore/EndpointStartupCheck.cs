using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// Fails the startup when two routes share a name; when a route is in the shared space without a declaration
/// accepting it; when a route
/// labelled with an <see cref="Operation"/> declares no authorization — neither a requirement nor
/// <c>AllowAnonymous</c>; when it would refuse every resource — no authorization handler for operations, and no
/// <c>AllowAnyResource</c>; or when it requires a policy that does not exist.
/// </summary>
/// <remarks>
/// <para>
/// A startup filter, because these conditions are only known once the application has mapped its routes and
/// chained the module's conventions, and because ASP.NET Core builds endpoints — and runs final conventions —
/// at the first request. The filter runs right after the pipeline is built, forces the endpoints to build,
/// and a failure stops the application before it serves anything.
/// </para>
/// <para>
/// Handler detection is an aid, not the guarantee: the guarantee is refusal by default at run time. Handlers
/// are resolved, so one registered by a factory is seen; a handler written directly against
/// <see cref="IAuthorizationHandler"/> may handle anything, and counts.
/// </para>
/// </remarks>
internal sealed class EndpointStartupCheck : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        var endpoints = app.ApplicationServices.GetService<EndpointDataSource>()?.Endpoints ?? [];
        CheckEndpointNames(endpoints);
        CheckSharedSpace(endpoints);
        CheckOperations(app.ApplicationServices, endpoints);
    };

    // ASP.NET Core compares endpoint names ordinally, and fails every request of the host on a duplicate: a module
    // naming its routes and mapped twice — once per compartment, once in the shared space — is the usual cause.
    private static void CheckEndpointNames(IEnumerable<Endpoint> all)
    {
        var duplicates = all
            .Select(endpoint => (Endpoint: endpoint, Name: endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName))
            .Where(named => named.Name is not null)
            .GroupBy(named => named.Name!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToList();
        if (duplicates.Count > 0)
        {
            var described = duplicates.Select(group =>
                $"'{group.Key}' on {string.Join(" and ", group.Select(named => named.Endpoint.DisplayName))}");
            throw new InvalidOperationException(
                $"These endpoint names are used more than once: {string.Join("; ", described)}. ASP.NET Core requires "
                + "names unique across the host. A module mapped twice cannot name its routes: remove WithName(...) "
                + "from them, or give each mapping its own names.");
        }
    }

    private static void CheckSharedSpace(IEnumerable<Endpoint> all)
    {
        var refused = all
            .Where(endpoint => endpoint.Metadata.GetMetadata<SharedSpaceMetadata>() is not null
                && endpoint.Metadata.GetMetadata<RequestContextDeclaration>() is not { AcceptsSharedSpace: true })
            .ToList();
        if (refused.Count > 0)
        {
            throw new InvalidOperationException(
                $"These routes are in the shared space, and nothing declares they accept it: {DescribeSharedSpace(refused)}. "
                + "Remove InSharedSpace() from them, or, for routes of your own, declare it on their group with "
                + "RequireRequestContext(\"<name>\", acceptsSharedSpace: true).");
        }
    }

    private static string DescribeSharedSpace(IEnumerable<Endpoint> endpoints)
        => string.Join(", ", endpoints.Select(endpoint =>
            endpoint.Metadata.GetMetadata<RequestContextDeclaration>() is { } declaration
                ? $"{endpoint.DisplayName} ({declaration.Module} does not accept the shared space)"
                : $"{endpoint.DisplayName} (no module declared)"));

    private static void CheckOperations(IServiceProvider services, IEnumerable<Endpoint> all)
    {
        var endpoints = all
            .Where(endpoint => endpoint.GetOperation() is not null)
            .ToList();
        if (endpoints.Count == 0)
            return;

        var open = endpoints.Where(endpoint => !DeclaresAuthorization(endpoint)).ToList();
        if (open.Count > 0)
        {
            throw new InvalidOperationException(
                $"These routes are labelled with an Operation and declare no authorization: {Describe(open)}. "
                + "Require it, with RequireAuthorization(...) or RequireAuthorizationPerOperation(...), "
                + "or declare the routes open to anyone, with .AllowAnonymous(). A fallback policy does not count: "
                + "it does not know the operation.");
        }

        var closed = endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<AllowAnyResourceMetadata>() is null)
            .ToList();
        if (closed.Count > 0 && !AnyOperationHandler(services))
        {
            throw new InvalidOperationException(
                $"These routes check every resource they load, and nothing answers: {Describe(closed)}. "
                + "Register a handler for the resources, such as an AuthorizationHandler<Operation, TResource>, "
                + "or declare that any user reaching the routes may access any resource, with .AllowAnyResource().");
        }

        var policyProvider = services.GetService<IAuthorizationPolicyProvider>();
        var missing = endpoints
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>())
            .Select(data => data.Policy)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Where(policy => policyProvider?.GetPolicyAsync(policy).GetAwaiter().GetResult() is null)
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Routes labelled with an Operation require policies that are not registered: {string.Join(", ", missing)}. "
                + "Declare them with AddAuthorization(options => options.AddPolicy(...)).");
        }
    }

    // The forms ASP.NET Core's authorization middleware reads on an endpoint: an attribute or RequireAuthorization
    // with policy names, a policy object, requirements, or the anonymous declaration.
    private static bool DeclaresAuthorization(Endpoint endpoint)
        => endpoint.Metadata.Any(metadata => metadata
            is IAuthorizeData or AuthorizationPolicy or IAuthorizationRequirementData or IAllowAnonymous);

    private static bool AnyOperationHandler(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetServices<IAuthorizationHandler>().Any(HandlesOperation);
    }

    private static bool HandlesOperation(IAuthorizationHandler handler)
    {
        var type = handler.GetType();
        if (type.Assembly.GetName().Name?.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) == true)
            return false;

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType)
                continue;
            var definition = current.GetGenericTypeDefinition();
            if (definition == typeof(AuthorizationHandler<>) || definition == typeof(AuthorizationHandler<,>))
                return current.GetGenericArguments()[0].IsAssignableFrom(typeof(Operation));
        }
        return true;
    }

    private static string Describe(IEnumerable<Endpoint> endpoints)
        => string.Join(", ", endpoints.Select(endpoint => $"{endpoint.DisplayName} ({endpoint.GetOperation()})"));
}
