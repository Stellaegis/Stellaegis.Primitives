using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// Whether the actor behind an authenticated principal is still admitted: an account blocked since the token was
/// issued, a token revoked, whatever the credentials say. One implementation per source — the accounts module for
/// the blocked accounts, the token issuer for the revoked tokens —, every one registered consulted, the first
/// refusal winning: admission only ever restricts.
/// <code>
/// public sealed class BlockedAccounts : IActorAdmission
/// {
///     public ValueTask&lt;Result&gt; AdmitAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
///         => ValueTask.FromResult(IsBlocked(principal) ? Result.Failure(Error.Unauthorized("Account.Blocked", "This account is blocked.")) : Result.Success());
/// }
/// </code>
/// </summary>
public interface IActorAdmission
{
    /// <summary>
    /// Admits <paramref name="principal"/>, which the host authenticated, or refuses it with an error whose code
    /// says why — <c>Account.Blocked</c>, <c>Token.Revoked</c>. Called on every request, so it reads what the
    /// implementation holds in memory rather than a store.
    /// </summary>
    ValueTask<Result> AdmitAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

/// <summary>
/// The admissions registered, asked as one question: whether this principal is still admitted. The middleware
/// of <see cref="ActorAdmissionApplicationBuilderExtensions.UseStellaegisActorAdmission"/> asks it on every
/// request; a token issuer asks it itself, at the authorization and at the refresh, where no request of the
/// resource server is involved.
/// </summary>
public interface IActorAdmissions
{
    /// <summary>
    /// The first refusal of the admissions registered, or success: an anonymous principal is admitted, there is
    /// nobody to refuse; so is any principal when no admission is registered.
    /// </summary>
    ValueTask<Result> AdmitAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

internal sealed class ActorAdmissions(IEnumerable<IActorAdmission> admissions) : IActorAdmissions
{
    public async ValueTask<Result> AdmitAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        Guard.NotNull(principal);
        if (principal.Identity?.IsAuthenticated != true)
            return Result.Success();
        foreach (var admission in admissions)
        {
            var result = await admission.AdmitAsync(principal, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
                return result;
        }
        return Result.Success();
    }
}

/// <summary>Registers the admissions of actors.</summary>
public static class ActorAdmissionServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TAdmission"/> as one of the admissions the host consults, a singleton, beside
    /// the ones already registered; and <see cref="IActorAdmissions"/>, which asks them all. A module registers its
    /// own from its registration; the host enables the question on its pipeline with
    /// <see cref="ActorAdmissionApplicationBuilderExtensions.UseStellaegisActorAdmission"/>.
    /// </summary>
    /// <remarks>
    /// The admissions are also asked wherever the authorization authenticates — a policy naming its own schemes
    /// authenticates the request again, after the door —, so that a bearer refused is refused on a route whose policy
    /// names the bearer while the cookie is the default. And the startup check
    /// (<see cref="StartupCheckServiceCollectionExtensions.AddStellaegisStartupCheck"/>, registered here) fails a host
    /// that registers admissions and never calls <c>UseStellaegisActorAdmission()</c>.
    /// </remarks>
    public static IServiceCollection AddStellaegisActorAdmission<TAdmission>(this IServiceCollection services)
        where TAdmission : class, IActorAdmission
    {
        Guard.NotNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActorAdmission, TAdmission>());
        return Admissions(services);
    }

    /// <summary>
    /// Registers an admission built by <paramref name="factory"/> — typically one the module registered under
    /// its own type, <c>provider => provider.GetRequiredService&lt;BlockedAccounts&gt;()</c> —, beside the ones
    /// already registered. Registered as many times as called: a module calls it once, from its registration.
    /// </summary>
    public static IServiceCollection AddStellaegisActorAdmission(this IServiceCollection services, Func<IServiceProvider, IActorAdmission> factory)
    {
        Guard.NotNull(services);
        Guard.NotNull(factory);
        services.AddSingleton(factory);
        return Admissions(services);
    }

    /// <summary>
    /// Registers <see cref="IActorAdmissions"/> alone, for a host that asks the question itself — a token issuer —
    /// and lets its modules register the admissions. Harmless when an admission already registered it.
    /// </summary>
    public static IServiceCollection AddStellaegisActorAdmissions(this IServiceCollection services)
        => Admissions(Guard.NotNull(services));

    private static IServiceCollection Admissions(IServiceCollection services)
    {
        services.TryAddSingleton<IActorAdmissions, ActorAdmissions>();
        services.TryAddSingleton<ActorAdmissionPipeline>();
        services.Replace(ServiceDescriptor.Transient<IPolicyEvaluator, AdmittingPolicyEvaluator>());
        return services.AddStellaegisStartupCheck();
    }
}

/// <summary>Enables the admission of actors on the pipeline.</summary>
public static class ActorAdmissionApplicationBuilderExtensions
{
    private const string Enabled = "Stellaegis.Primitives.AspNetCore.ActorAdmission";

    /// <summary>
    /// Refuses, with 401 and the code of the refusing admission as problem details, a request whose authenticated
    /// principal one of the registered <see cref="IActorAdmission"/> no longer admits. Call it after
    /// <c>UseAuthentication()</c>, so that the principal is known, and before <c>UseAuthorization()</c>, so that no
    /// policy is evaluated for an actor refused at the door. An anonymous request goes on. Calling it twice adds
    /// the middleware once.
    /// </summary>
    /// <remarks>
    /// ⚠ A host that calls it without registering any admission admits nothing more than the authentication did:
    /// the middleware has nobody to ask, and lets every request through. The admissions come from the modules —
    /// the accounts module for the blocked accounts, the token issuer for the revoked tokens —, each registered
    /// by <see cref="ActorAdmissionServiceCollectionExtensions.AddStellaegisActorAdmission{TAdmission}(IServiceCollection)"/>.
    /// Called before <c>UseAuthentication()</c>, it sees no principal: the routes requiring an authorization are kept
    /// all the same, the authorization asking the admissions itself, and the others are not.
    /// </remarks>
    public static IApplicationBuilder UseStellaegisActorAdmission(this IApplicationBuilder app)
    {
        Guard.NotNull(app);
        app.ApplicationServices.GetService<ActorAdmissionPipeline>()?.Enable();   // a branch of UseWhen counts
        if (app.Properties.ContainsKey(Enabled))
            return app;
        app.Properties[Enabled] = true;
        return app.UseMiddleware<ActorAdmissionMiddleware>();
    }
}

/// <summary>
/// Whether the host put the admission on its pipeline, on the whole of it or on a branch: the properties of a branch do
/// not reach the application, a singleton does.
/// </summary>
internal sealed class ActorAdmissionPipeline
{
    private int _enabled;

    public bool Enabled => Volatile.Read(ref _enabled) == 1;

    public void Enable() => Interlocked.Exchange(ref _enabled, 1);
}

/// <summary>
/// The policy evaluator of ASP.NET Core, asking the admissions about the principal the authorization authenticated:
/// a policy naming its own schemes authenticates the request again, after the door, and a principal the door never
/// saw — a revoked bearer on a host whose default scheme is the cookie — would otherwise be served (measured, 200).
/// A principal refused becomes anonymous: the authorization challenges it, and a route open to anyone sees nobody.
/// </summary>
internal sealed class AdmittingPolicyEvaluator(IAuthorizationService authorization, IActorAdmissions admissions, ILogger<AdmittingPolicyEvaluator> logger)
    : IPolicyEvaluator
{
    private readonly PolicyEvaluator _inner = new(authorization);

    public async Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context)
    {
        var result = await _inner.AuthenticateAsync(policy, context).ConfigureAwait(false);
        if (!result.Succeeded || result.Principal is not { } principal)
            return result;
        var admitted = await admissions.AdmitAsync(principal, context.RequestAborted).ConfigureAwait(false);
        if (admitted.IsSuccess)
            return result;
        logger.LogWarning("Request refused at the authorization on {Path}: {Code}", context.Request.Path, admitted.Error!.Code);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        return AuthenticateResult.Fail(admitted.Error.Message);
    }

    public Task<PolicyAuthorizationResult> AuthorizeAsync(AuthorizationPolicy policy, AuthenticateResult authenticationResult, HttpContext context, object? resource)
        => _inner.AuthorizeAsync(policy, authenticationResult, context, resource);
}

/// <summary>The middleware behind <see cref="ActorAdmissionApplicationBuilderExtensions.UseStellaegisActorAdmission"/>.</summary>
internal sealed class ActorAdmissionMiddleware(RequestDelegate next, ILogger<ActorAdmissionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.RequestServices.GetService<IActorAdmissions>() is { } admissions)
        {
            var result = await admissions.AdmitAsync(httpContext.User, httpContext.RequestAborted).ConfigureAwait(false);
            if (result.IsFailure)
            {
                var error = result.Error!;
                logger.LogWarning("Request refused at the door on {Path}: {Code}", httpContext.Request.Path, error.Code);
                var details = error.ToProblemDetails();
                details.Status = StatusCodes.Status401Unauthorized;
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await httpContext.Response.WriteAsJsonAsync(details, httpContext.RequestAborted).ConfigureAwait(false);
                return;
            }
        }
        await next(httpContext).ConfigureAwait(false);
    }
}
