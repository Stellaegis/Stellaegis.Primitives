# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `IStellaegisModule`: the identity of a module, a type carrying its name as a static member, so that code
  needing a module takes the type, never a string (`Stellaegis.Primitives`).
- `BelongsTo<TModule>()`: declares that a group's routes belong to a module, with or without compartments.
  It fails when the startup check is not registered.
- `AddStellaegisStartupCheck()`: registers the startup check without a request context, for applications
  using only modules without compartments, which were not checked at all. `AddStellaegisRequestContext`
  calls it.
- The startup check fails when a route labelled with an `Operation` belongs to no module, and when two
  module types carry the same name.

### Changed

- **Breaking:** `RequireRequestContext(acceptsSharedSpace = false)` no longer takes the module name: the
  module is declared by `BelongsTo<TModule>()`, which a module without compartments calls alone.

- **Breaking:** the startup check fails when a route labelled with an `Operation` declares no authorization —
  neither a requirement (`RequireAuthorization`, `RequireAuthorizationPerOperation`, an `[Authorize]`
  attribute) nor `AllowAnonymous()` —, naming the routes. Such a route used to be served open. A fallback
  policy does not count: it does not know the operation, and would open every operation of a module to any
  user it admits (`Stellaegis.Primitives.AspNetCore`).

### Fixed

- The startup check fails when two routes share an endpoint name, naming the name and both routes. ASP.NET
  Core lets such a host start, then fails every request: a module naming its routes and mapped once per
  compartment and once in the shared space did exactly that, as the README showed
  (`Stellaegis.Primitives.AspNetCore`).

## [2.0.0] — 2026-10-14

The shared space: data that belongs to no compartment, reached only by the routes an application marks for
it. The three packages move together to 2.0.0; only `Stellaegis.Primitives.AspNetCore` breaks its API.

### Added

- `Space`: where a module acts — a `Compartment`, or `Space.Shared`. Its `Compartment` is `null` exactly when
  it is the shared space, with `IsShared` beside it. A compartment converts to its space, and a `null`
  compartment to a `null` space, never to the shared space. No text form and no serialization: a module that
  has a shared space decides how it stores it (`Stellaegis.Primitives`).
- `IRequestContextSource`: what an application implements to give the compartment and the actor of a request.
  It knows nothing of the shared space.
- `InSharedSpace()`: marks a group of routes as addressing the shared space. On a marked route the space is
  `Space.Shared`, and the source's compartment is not read.
- The startup check fails when a route marked `InSharedSpace()` has no declaration accepting the shared space,
  naming the route and the module.

### Changed

- **Breaking:** `IRequestContext` gives `Space? Space` instead of `Compartment? Compartment`. `null` still
  means an unresolved compartment.
- **Breaking:** `AddStellaegisRequestContext<TSource>()` takes an `IRequestContextSource` instead of an
  `IRequestContext`. Only the interface an application's adapter implements changes; its members do not.
- **Breaking:** `RequireRequestContext(module, acceptsSharedSpace = false)` applies to the module's
  `RouteGroupBuilder` instead of an `IEndpointRouteBuilder`, and places the module's declaration on each of
  its endpoints. The shared space is refused by default; the declaration closest to a route counts.
- The documentation of `Compartment` no longer places data that belongs to no compartment in a compartment set
  apart for it.

## [1.1.0] — 2026-10-14

A third package, `Stellaegis.Primitives.AspNetCore`, for modules that expose HTTP endpoints.

### Added

- `Compartment`: the compartment a piece of data belongs to, an identifier of 3 to 63 lowercase letters,
  digits and single hyphens (`Stellaegis.Primitives`).
- `Actor`: who performs an operation, as recorded for traceability — 1 to 128 characters, no control
  character, no leading or trailing whitespace.
- Both implement `IParsable<T>` and serialize as a JSON string, including as a dictionary key.
- `PrimitiveTypeHandlers.Register()`: Dapper type handlers reading and writing `Compartment` and `Actor` as
  strings (`Stellaegis.Primitives.Data`).
- `Stellaegis.Primitives.AspNetCore`, a new package for modules that expose HTTP endpoints.
- `IRequestContext`: the compartment and the actor of a request, registered per request with
  `AddStellaegisRequestContext`, configurable from a constant, a route value, a claim or a function, or
  provided by an adapter of the application.
- `RequireRequestContext`: fails the startup of a module mapped without a request context.
- `Operation` and `OperationKind`: the operation a route performs, as route metadata and as the requirement
  checked on a resource; `GetOperation` reads it from an endpoint or a request.
- `RequireAuthorizationPerOperation`: attaches the application's policy to each route by its operation.
- `AuthorizeResourceAsync`: asks ASP.NET Core's resource-based authorization about a loaded resource, refusal
  by default; `AllowAnyResource` declares that any resource is accessible.
- A startup check, registered with the request context: a route labelled with an operation needs an
  authorization handler or `AllowAnyResource`, and every policy it requires must exist.
- `ToStatusCode`, `ToProblemDetails` and `ToHttpResult`: the translation of `ErrorType`, `Error` and `Result`
  to HTTP, the error code in a `code` extension and the message of a `Failure` hidden outside Development.
- `AddErrorTranslation`: an endpoint filter answering `UserFacingException` with its status and message, and
  `ArgumentException` with 400.

## [1.0.0] — 2026-10-12

First release.

### Added

- `Result`, `Result<T>`, `Error` and `ErrorType`: the outcome of an operation that can fail for an
  expected reason, with one factory per error category (`Stellaegis.Primitives`).
- `Guard`: argument checks that return the checked value and capture the parameter name.
- `UserFacingException`: a message safe to show to the end user, categorized by `ErrorType`.
- `IEvent`, `IEventPublisher` and `NullEventPublisher`: the event marker, the publication port, and an
  implementation that discards events.
- `IUnitOfWorkFactory<TDatabase>` and `IUnitOfWork`, with an ADO.NET implementation opening from a
  `DbDataSource` or a connection provider, and an in-memory implementation for tests
  (`Stellaegis.Primitives.Data`).
- `IUnitOfWork.Publish`: events held until the transaction ends, published after commit and discarded
  on rollback.
- `IRestorableRepository`: lets an in-memory repository be restored on rollback.
- `SnakeCaseTypeMap<T>` and `[ColumnMap]`: a Dapper column map from snake_case to PascalCase, for classes
  and positional records.

[2.0.0]: https://github.com/Stellaegis/Stellaegis.Primitives/compare/v1.1.0...v2.0.0
[1.1.0]: https://github.com/Stellaegis/Stellaegis.Primitives/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/Stellaegis/Stellaegis.Primitives/releases/tag/v1.0.0
