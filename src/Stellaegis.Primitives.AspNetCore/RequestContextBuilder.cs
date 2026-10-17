using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// Declares where the configurable request context reads the compartment and the actor of a request.
/// Exactly one compartment source is required. The actor defaults to the <see cref="ClaimTypes.NameIdentifier"/>
/// claim, then <c>sub</c>, and to <c>anonymous</c> when no user is authenticated.
/// </summary>
public sealed class RequestContextBuilder
{
    private static readonly string[] DefaultActorClaims = [ClaimTypes.NameIdentifier, "sub"];

    private Func<HttpContext, Compartment?>? _compartment;
    private Func<HttpContext, Actor?> _actor = http => ReadActorClaim(http, DefaultActorClaims);
    private Actor _anonymous = Actor.Parse("anonymous");

    internal RequestContextBuilder()
    {
    }

    /// <summary>
    /// Every request addresses the same compartment — an application that needs only one. Name it after the
    /// organization that owns the data, as a real compartment: the day the application takes a second one, the
    /// first keeps its name, and nothing is migrated.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="compartment"/> is not a valid compartment.</exception>
    public RequestContextBuilder SingleCompartment(string compartment)
        => SingleCompartment(Compartment.Parse(Guard.NotNull(compartment)));

    /// <inheritdoc cref="SingleCompartment(string)"/>
    /// <remarks>
    /// Takes the constant the application declares once, which its work outside requests — a scheduled task,
    /// an import — passes to the modules as well.
    /// </remarks>
    public RequestContextBuilder SingleCompartment(Compartment compartment)
    {
        var single = Guard.NotNull(compartment);
        return SetCompartment(_ => single);
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
    /// the anonymous label.
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
    /// the anonymous label.
    /// </summary>
    public RequestContextBuilder ActorFrom(Func<HttpContext, Actor?> source)
    {
        _actor = Guard.NotNull(source);
        return this;
    }

    /// <summary>The label recorded as the actor of a request without one. Defaults to <c>anonymous</c>.</summary>
    /// <exception cref="FormatException"><paramref name="label"/> is not a valid actor.</exception>
    public RequestContextBuilder AnonymousActor(string label)
    {
        _anonymous = Actor.Parse(Guard.NotNull(label));
        return this;
    }

    internal RequestContextSettings Build() => new(
        _compartment ?? throw new InvalidOperationException(
            "A request context needs a compartment source: call SingleCompartment, CompartmentFromRoute, "
            + "CompartmentFromClaim or CompartmentFrom."),
        _actor,
        _anonymous);

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

/// <summary>What a <see cref="RequestContextBuilder"/> declared: the compartment and actor sources, and the anonymous label.</summary>
internal sealed record RequestContextSettings(
    Func<HttpContext, Compartment?> Compartment,
    Func<HttpContext, Actor?> Actor,
    Actor Anonymous);
