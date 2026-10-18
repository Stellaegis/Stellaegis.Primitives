using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// Declares where the configurable request context reads the compartment and the actor of a request. How many
/// compartments there are is the application frame's to say, not this builder's: an application with a single
/// compartment declares no compartment source, one with many declares exactly one. The actor defaults to the
/// <see cref="ClaimTypes.NameIdentifier"/> claim, then <c>sub</c>, and to the frame's anonymous actor when no user
/// is authenticated.
/// </summary>
public sealed class RequestContextBuilder
{
    private static readonly string[] DefaultActorClaims = [ClaimTypes.NameIdentifier, "sub"];

    private Func<HttpContext, Compartment?>? _compartment;
    private Func<HttpContext, Actor?> _actor = http => ReadActorClaim(http, DefaultActorClaims);

    internal RequestContextBuilder()
    {
    }

    /// <summary>
    /// Reads the compartment from a route value, such as <c>{org}</c> in <c>/api/orgs/{org}</c>. A missing or
    /// invalid value resolves to <see langword="null"/>.
    /// </summary>
    public RequestContextBuilder CompartmentFromRoute(string routeValueName)
    {
        Guard.NotWhiteSpace(routeValueName);
        return SetCompartment(http =>
            Compartment.TryParse(http.GetRouteValue(routeValueName) as string, out var compartment) ? compartment : null);
    }

    /// <summary>
    /// Reads the compartment from a claim of the authenticated user. A missing or invalid value resolves to
    /// <see langword="null"/>.
    /// </summary>
    public RequestContextBuilder CompartmentFromClaim(string claimType)
    {
        Guard.NotWhiteSpace(claimType);
        return SetCompartment(http =>
            Compartment.TryParse(http.User.FindFirst(claimType)?.Value, out var compartment) ? compartment : null);
    }

    /// <summary>
    /// Reads the compartment with a function of the request — a subdomain or a header, for instance. The
    /// function parses the value itself, and returns <see langword="null"/> when it cannot resolve one.
    /// </summary>
    public RequestContextBuilder CompartmentFrom(Func<HttpContext, Compartment?> source)
        => SetCompartment(Guard.NotNull(source));

    /// <summary>
    /// Reads the actor from a claim of the authenticated user — <c>oid</c> with Microsoft Entra, for instance,
    /// which stays the same from one application to another. Without an authenticated user, the actor is
    /// the frame's anonymous actor.
    /// </summary>
    /// <remarks>
    /// An authenticated user without that claim, or with a value that is not a valid <see cref="Actor"/>,
    /// fails the request: it means the wrong claim was chosen, and recording the request as anonymous would
    /// attribute it to nobody.
    /// </remarks>
    public RequestContextBuilder ActorFromClaim(string claimType)
    {
        string[] claimTypes = [Guard.NotWhiteSpace(claimType)];
        _actor = http => ReadActorClaim(http, claimTypes);
        return this;
    }

    /// <summary>
    /// Reads the actor with a function of the request. When it returns <see langword="null"/>, the actor is
    /// the frame's anonymous actor.
    /// </summary>
    public RequestContextBuilder ActorFrom(Func<HttpContext, Actor?> source)
    {
        _actor = Guard.NotNull(source);
        return this;
    }

    internal RequestContextSettings Build() => new(_compartment, _actor);

    private RequestContextBuilder SetCompartment(Func<HttpContext, Compartment?> source)
    {
        if (_compartment is not null)
            throw new InvalidOperationException("A request context has a single compartment source, and one is already declared.");
        _compartment = source;
        return this;
    }

    private static Actor? ReadActorClaim(HttpContext http, string[] claimTypes)
    {
        if (http.User.Identity?.IsAuthenticated != true)
            return null;

        var value = claimTypes.Select(type => http.User.FindFirst(type)?.Value).FirstOrDefault(v => v is not null);
        var names = string.Join(" or ", claimTypes);
        if (value is null)
            throw new InvalidOperationException($"The authenticated user has no {names} claim to read the actor from.");
        return Actor.TryParse(value, out var actor)
            ? actor
            : throw new InvalidOperationException($"The {names} claim of the authenticated user is not a valid actor.");
    }
}

/// <summary>
/// What a <see cref="RequestContextBuilder"/> declared: the compartment source, if any, and the actor source.
/// Registered as a singleton, so that the startup check reads it against the frame.
/// </summary>
internal sealed record RequestContextSettings(
    Func<HttpContext, Compartment?>? Compartment,
    Func<HttpContext, Actor?> Actor);
