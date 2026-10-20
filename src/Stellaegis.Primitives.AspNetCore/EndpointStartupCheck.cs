using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// Fails the startup when the application frame and the modules' declarations do not hold together
/// (<see cref="ApplicationFrame.Validate"/>); when the request context disagrees with the frame; when a route
/// belongs to a module nothing declares, is labelled with an operation its module does not declare, or reads the
/// request context while its module is declared without compartments; when a route is in the shared space of an application without one; when two routes share a name; when two module types carry the same name; when a route is
/// in the shared space without a declaration accepting it; when a route acting on spaces is in the shared space,
/// belongs to no module or carries no operation; when a route labelled with an <see cref="Operation"/>
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
        CheckRequestContextRegistered(services, endpoints);
        CheckModules(endpoints);
        CheckDeclaredModules(endpoints, modules);
        CheckDeclaredOperations(endpoints, modules);
        CheckFrame(endpoints, frame);
        CheckSharedSpace(endpoints, modules);
        CheckSpacesRoutes(endpoints);
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
            .Select(endpoint => (Endpoint: endpoint, Module: endpoint.GetModule()))
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
            .Where(routed => routed.Endpoint.Metadata.GetMetadata<RequiresRequestContextAttribute>() is not null
                && declared[routed.Module!.Type].Compartments == ModuleCompartments.None)
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

    // A route labelled with an operation its module does not declare would escape the catalogue, which reads the
    // declarations: an application granting from the catalogue would never see the operation it needs to grant.
    private static void CheckDeclaredOperations(IEnumerable<Endpoint> all, IReadOnlyList<ModuleDeclaration> modules)
    {
        var declared = modules.ToDictionary(module => module.Module, module => module.Operations, EqualityComparer<Type>.Default);
        var undeclared = all
            .Select(endpoint => (Endpoint: endpoint, Operation: endpoint.GetOperation(), Module: endpoint.GetModule()))
            .Where(routed => routed.Operation is not null && routed.Module is not null
                && !declared[routed.Module.Type].Contains(routed.Operation))
            .ToList();
        if (undeclared.Count > 0)
        {
            var described = string.Join(", ", undeclared.Select(routed =>
                $"{routed.Endpoint.DisplayName} ({routed.Operation}, {routed.Operation!.Kind}, of {routed.Module!.Name})"));
            throw new InvalidOperationException(
                $"These routes are labelled with operations their module does not declare: {described}. Declare the "
                + "operations of a module on its declaration, AddStellaegisModule(ModuleDeclaration.Compartmented<TModule>()"
                + ".WithOperations(...)); an identity declared with another kind is another operation.");
        }
    }

    // RequireRequestContext() checks the registration when the group is mapped; an attribute cannot, so the routes
    // that declare it are checked here.
    private static void CheckRequestContextRegistered(IServiceProvider services, IEnumerable<Endpoint> all)
    {
        if (services.GetService<IServiceProviderIsService>()?.IsService(typeof(IRequestContext)) != false)
            return;

        var reading = all.Where(endpoint => endpoint.Metadata.GetMetadata<RequiresRequestContextAttribute>() is not null).ToList();
        if (reading.Count > 0)
        {
            throw new InvalidOperationException(
                $"These routes read the request context, and none is registered: {DescribeWithModule(reading)}. "
                + "Register one with the application's frame: "
                + "builder.Services.AddStellaegisFrame(ApplicationFrame.SingleCompartment(\"acme\")).AddStellaegisRequestContext(), "
                + "or a compartment source when the frame has many.");
        }
    }

    // A route reading the request context in an application without compartments needs no check of its own: it needs
    // a registered request context (CheckRequestContextRegistered), which CheckRequestContext refuses in such a frame.
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
            endpoint.GetModule() is { } module
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
            .Select(endpoint => endpoint.GetModule())
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
            .Where(endpoint => endpoint.GetOperation() is not null && endpoint.GetModule() is null)
            .ToList();
        if (orphans.Count > 0)
        {
            throw new InvalidOperationException(
                $"These routes are labelled with an Operation and belong to no module: {Describe(orphans)}. "
                + "Declare the module on their group with BelongsTo<TModule>(), or on their controller with [BelongsTo<TModule>], TModule being the IStellaegisModule "
                + "that names it; an application's own routes declare a module of the application's.");
        }
    }

    // The declaration closest to the route counts; by default it defers to the module's.
    private static void CheckSharedSpace(IEnumerable<Endpoint> all, IReadOnlyList<ModuleDeclaration> modules)
    {
        var declared = modules.ToDictionary(module => module.Module, module => module, EqualityComparer<Type>.Default);
        var refused = all
            .Where(endpoint => endpoint.Metadata.GetMetadata<SharedSpaceMetadata>() is not null)
            .Select(endpoint => (Endpoint: endpoint, Reason: SharedSpaceRefusal(endpoint, declared)))
            .Where(marked => marked.Reason is not null)
            .ToList();
        if (refused.Count > 0)
        {
            var described = string.Join(", ", refused.Select(marked => $"{marked.Endpoint.DisplayName} ({marked.Reason})"));
            throw new InvalidOperationException(
                $"These routes are in the shared space, and nothing declares they accept it: {described}. Remove "
                + "InSharedSpace() from them; a module accepts the shared space with AcceptingSharedSpace() on its declaration, "
                + "and a group of routes of your own with RequireRequestContext(SharedSpaceAcceptance.Accepted).");
        }
    }

    // A route acting on spaces takes its space as an object: the shared space, a place, has nothing to give it; and
    // it is a route of a module, labelled, so that the catalogue and the authorization see it as any other.
    private static void CheckSpacesRoutes(IEnumerable<Endpoint> all)
    {
        var faulty = all
            .Where(endpoint => endpoint.Metadata.GetMetadata<ActsOnSpacesMetadata>() is not null)
            .Select(endpoint => (Endpoint: endpoint, Reason: SpacesRouteFault(endpoint)))
            .Where(acting => acting.Reason is not null)
            .ToList();
        if (faulty.Count > 0)
        {
            var described = string.Join(", ", faulty.Select(acting => $"{acting.Endpoint.DisplayName} ({acting.Reason})"));
            throw new InvalidOperationException(
                $"These routes act on spaces, and their declarations do not hold: {described}. A route acting on spaces "
                + "belongs to a module, carries an operation, and is never mapped InSharedSpace(): its space is its object.");
        }
    }

    private static string? SpacesRouteFault(Endpoint endpoint)
        => endpoint.Metadata.GetMetadata<SharedSpaceMetadata>() is not null ? "mapped in the shared space"
            : endpoint.GetModule() is null ? "no module declared"
            : endpoint.GetOperation() is null ? "no operation"
            : null;

    private static string? SharedSpaceRefusal(Endpoint endpoint, IReadOnlyDictionary<Type, ModuleDeclaration> declared)
    {
        var declaration = endpoint.Metadata.GetMetadata<RequiresRequestContextAttribute>();
        if (declaration is null)
            return "no request context declared";
        var module = endpoint.GetModule();
        return declaration.SharedSpace switch
        {
            SharedSpaceAcceptance.Accepted => null,
            SharedSpaceAcceptance.Refused => $"{module?.Name ?? "the route"} refuses the shared space",
            _ when module is null => "no module declared, and the route does not accept the shared space itself",
            _ => declared.TryGetValue(module.Type, out var declaredModule) && declaredModule.AcceptsSharedSpace
                ? null
                : $"{module.Name} does not accept the shared space",
        };
    }

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
                + "Register a handler for the resources, such as an AuthorizationHandler<OperationRequirement, TResource>, "
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
                return current.GetGenericArguments()[0].IsAssignableFrom(typeof(OperationRequirement));
        }
        return true;
    }

    private static string Describe(IEnumerable<Endpoint> endpoints)
        => string.Join(", ", endpoints.Select(endpoint => $"{endpoint.DisplayName} ({endpoint.GetOperation()})"));
}
