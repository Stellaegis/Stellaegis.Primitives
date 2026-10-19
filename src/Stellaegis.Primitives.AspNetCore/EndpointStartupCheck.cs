using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives.DependencyInjection;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// Fails the startup when the application frame and the modules' declarations do not hold together
/// (<see cref="ApplicationFrame.Validate"/>); when the request context disagrees with the frame; when a route
/// belongs to a module nothing declares, or reads the request context while its module is declared without
/// compartments; when a route is in the shared space of an application without one; when two routes share a name; when two module types carry the same name; when a route is
/// in the shared space without a declaration accepting it; when a route labelled with an <see cref="Operation"/>
/// belongs to no module; when it declares no authorization — neither a requirement nor
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
        var services = app.ApplicationServices;
        var frame = services.GetStellaegisFrame();
        var modules = services.GetStellaegisModules();
        frame.Validate(modules);
        CheckRequestContext(services, frame);

        var endpoints = services.GetService<EndpointDataSource>()?.Endpoints ?? [];
        CheckEndpointNames(endpoints);
        CheckModules(endpoints);
        CheckDeclaredModules(endpoints, modules);
        CheckFrame(endpoints, frame);
        CheckSharedSpace(endpoints);
        CheckOperations(services, endpoints);
    };

    // How many compartments there are is the frame's to say; where a request reads its own, the request context's.
    private static void CheckRequestContext(IServiceProvider services, ApplicationFrame frame)
    {
        if (services.GetService<IServiceProviderIsService>()?.IsService(typeof(IRequestContext)) != true)
            return;

        if (frame.Compartments == CompartmentCount.None)
        {
            throw new InvalidOperationException(
                $"A request context is registered, and the application frame ({frame}) has no compartments: there is no "
                + "space to give a request. Declare the application's compartments, with "
                + "AddStellaegisFrame(ApplicationFrame.SingleCompartment(\"acme\")) or ApplicationFrame.ManyCompartments(), "
                + "or remove AddStellaegisRequestContext(...).");
        }

        if (services.GetService<RequestContextSettings>() is not { } settings)
            return;
        if (frame.Compartments == CompartmentCount.Single && settings.Compartment is not null)
        {
            throw new InvalidOperationException(
                $"The request context declares a compartment source, and the application frame ({frame}) has a single "
                + "compartment: every request addresses it. Remove the compartment source from AddStellaegisRequestContext(...), "
                + "or declare ApplicationFrame.ManyCompartments().");
        }
        if (frame.Compartments == CompartmentCount.Many && settings.Compartment is null)
        {
            throw new InvalidOperationException(
                $"The application frame ({frame}) has many compartments, and the request context declares no source to read "
                + "each request's from: declare one, such as AddStellaegisRequestContext(c => c.CompartmentFromRoute(\"org\")).");
        }
    }

    // A module present on the routes and absent from the registrations would escape the validation: a module
    // with compartments that cannot hold the frame's isolation by owner would start without a word.
    private static void CheckDeclaredModules(IEnumerable<Endpoint> all, IReadOnlyList<ModuleDeclaration> modules)
    {
        var declared = modules.ToDictionary(module => module.Module, module => module, EqualityComparer<Type>.Default);
        var routed = all
            .Select(endpoint => (Endpoint: endpoint, Module: endpoint.Metadata.GetMetadata<ModuleMetadata>()))
            .Where(routed => routed.Module is not null)
            .ToList();

        var undeclared = routed
            .Select(routed => routed.Module!)
            .Where(module => !declared.ContainsKey(module.Type))
            .Distinct()
            .ToList();
        if (undeclared.Count > 0)
        {
            throw new InvalidOperationException(
                $"Routes belong to modules nothing declares: {string.Join(", ", undeclared.Select(module => module.Name))}. "
                + "Register each module, with AddStellaegisModule(ModuleDeclaration.Compartmented<TModule>()) or "
                + "ModuleDeclaration.WithoutCompartments<TModule>() — usually the module's own registration does; "
                + "an application's own module is declared by the application.");
        }

        var contradicted = routed
            .Where(routed => routed.Endpoint.Metadata.GetMetadata<RequestContextDeclaration>() is not null
                && !declared[routed.Module!.Type].IsCompartmented)
            .Select(routed => routed.Endpoint)
            .ToList();
        if (contradicted.Count > 0)
        {
            throw new InvalidOperationException(
                $"These routes read the request context, and their module is declared without compartments: "
                + $"{DescribeWithModule(contradicted)}. Declare the module with ModuleDeclaration.Compartmented<TModule>(), "
                + "or remove RequireRequestContext() from its routes.");
        }
    }

    // A route reading the request context in an application without compartments needs no check of its own:
    // RequireRequestContext needs a registered request context, which CheckRequestContext refuses in such a frame.
    private static void CheckFrame(IEnumerable<Endpoint> all, ApplicationFrame frame)
    {
        if (frame.HasSharedSpace)
            return;

        var shared = all.Where(endpoint => endpoint.Metadata.GetMetadata<SharedSpaceMetadata>() is not null).ToList();
        if (shared.Count > 0)
        {
            throw new InvalidOperationException(
                $"These routes are in the shared space, and the application frame ({frame}) has none: "
                + $"{DescribeWithModule(shared)}. Remove InSharedSpace() from them, or declare the shared space "
                + "in the frame, with WithSharedSpace().");
        }
    }

    private static string DescribeWithModule(IEnumerable<Endpoint> endpoints)
        => string.Join(", ", endpoints.Select(endpoint =>
            endpoint.Metadata.GetMetadata<ModuleMetadata>() is { } module
                ? $"{endpoint.DisplayName} ({module.Name})"
                : endpoint.DisplayName));

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

    private static void CheckModules(IEnumerable<Endpoint> all)
    {
        var homonyms = all
            .Select(endpoint => endpoint.Metadata.GetMetadata<ModuleMetadata>())
            .OfType<ModuleMetadata>()
            .Distinct()
            .GroupBy(module => module.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToList();
        if (homonyms.Count > 0)
        {
            var described = homonyms.Select(group =>
                $"'{group.Key}' for {string.Join(" and ", group.Select(module => module.Type.FullName))}");
            throw new InvalidOperationException(
                $"These module names are carried by more than one type: {string.Join("; ", described)}. "
                + "A module name identifies one module: rename one of them.");
        }

        var orphans = all
            .Where(endpoint => endpoint.GetOperation() is not null && endpoint.Metadata.GetMetadata<ModuleMetadata>() is null)
            .ToList();
        if (orphans.Count > 0)
        {
            throw new InvalidOperationException(
                $"These routes are labelled with an Operation and belong to no module: {Describe(orphans)}. "
                + "Declare the module on their group with BelongsTo<TModule>(), TModule being the IStellaegisModule "
                + "that names it; an application's own routes declare a module of the application's.");
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
                + "RequireRequestContext(acceptsSharedSpace: true).");
        }
    }

    private static string DescribeSharedSpace(IEnumerable<Endpoint> endpoints)
        => string.Join(", ", endpoints.Select(endpoint =>
            endpoint.Metadata.GetMetadata<RequestContextDeclaration>() is null
                ? $"{endpoint.DisplayName} (no request context declared)"
                : $"{endpoint.DisplayName} ({endpoint.Metadata.GetMetadata<ModuleMetadata>()?.Name ?? "its module"} does not accept the shared space)"));

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
            .Where(endpoint => endpoint.Metadata.GetMetadata<AllowAnyResourceMetadata>() is null
                && endpoint.Metadata.GetMetadata<LoadsNoResourceMetadata>() is null)
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
