using Microsoft.AspNetCore.Authorization;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// The <see cref="Operation"/> of a route, as the requirement a module checks on a resource it loaded
/// (<see cref="ResourceAuthorizationExtensions.AuthorizeResourceAsync"/>). The application answers with an
/// <c>AuthorizationHandler&lt;OperationRequirement, TResource&gt;</c>: the route and the data speak the same language.
/// <code>
/// sealed class OwnerRule : AuthorizationHandler&lt;OperationRequirement, Note&gt;
/// {
///     protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationRequirement requirement, Note resource)
///     {
///         if (resource.Owner.Value == context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value)
///             context.Succeed(requirement);
///         return Task.CompletedTask;
///     }
/// }
/// </code>
/// </summary>
/// <remarks>
/// The operation itself lives in <c>Stellaegis.Primitives</c>, with no knowledge of ASP.NET Core: this type is its
/// translation into the authorization model of ASP.NET Core, and nothing more.
/// </remarks>
public sealed class OperationRequirement : IAuthorizationRequirement
{
    /// <summary>Wraps <paramref name="operation"/> as a requirement.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    public OperationRequirement(Operation operation) => Operation = Guard.NotNull(operation);

    /// <summary>The operation of the route.</summary>
    public Operation Operation { get; }

    /// <summary>The identity of the operation, <c>target.name</c>.</summary>
    public override string ToString() => Operation.ToString();
}
