using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// Fails the startup when a route labelled with an <see cref="Operation"/> would refuse every resource — no
/// authorization handler for operations, and no <c>AllowAnyResource</c> — or requires a policy that does not
/// exist.
/// </summary>
/// <remarks>
/// <para>
/// A startup filter, because both conditions are only known once the application has mapped its routes and
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
internal sealed class OperationStartupCheck : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        Check(app.ApplicationServices);
    };

    private static void Check(IServiceProvider services)
    {
        var endpoints = (services.GetService<EndpointDataSource>()?.Endpoints ?? [])
            .Where(endpoint => endpoint.GetOperation() is not null)
            .ToList();
        if (endpoints.Count == 0)
            return;

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
