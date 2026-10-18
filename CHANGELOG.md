# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `ApplicationFrame`: what is true of an application's isolation, declared once in code the hosts share — no
  compartments, a single compartment or many; a shared space, absent unless declared; an isolation by owner; the
  actor recorded for a request without an authenticated user. Without ASP.NET (`Stellaegis.Primitives`).
- `ModuleDeclaration` and `ModuleCapability`: what a module declares to the application that assembles it —
  whether it has compartments, always explicit; its capabilities, from the module or from its adapter, such as
  `ModuleCapability.OwnerIsolation`; the parameters the assembly turns on among them.
- `ApplicationFrame.Confront(...)`: reads the frame against the modules' declarations, and fails naming every
  module at fault — a module with compartments in an application without any, a module that cannot hold the
  isolation by owner the frame declares, a parameter outside a module's capabilities, a module declared twice or
  two modules of the same name.
- `NameRules`: the identifier rule and the module name rule, public so that every package reads the same ones.

### Changed

- **Breaking.** An operation's target and name follow the grammar of table names, which permission codes follow
  too: 1 to 63 lowercase letters, digits and single underscores, starting with a letter and ending with a letter or
  a digit. A hyphen is refused, never converted: `fiscal-years` becomes `fiscal_years`, `replace-content` becomes
  `replace_content` (`Stellaegis.Primitives.AspNetCore`).
- **Breaking.** A module name is 3 to 63 lowercase letters and digits, starting with a letter, without a hyphen or
  an underscore: it may become, unchanged, the API code of a host serving only this module — an OAuth scope and
  audience —, a URL segment or a configuration key. `BelongsTo<TModule>()` refuses any other name.

### Fixed

- The exception of `RequireRequestContext` without a registered request context suggested
  `SingleCompartment("main")`. It now suggests `SingleCompartment("acme")`: a single compartment is named after the
  organization that owns the data, never a placeholder (`Stellaegis.Primitives.AspNetCore`).
- The documentation of `Actor` says that an actor is a stable, opaque identifier, never an e-mail address or a name;
  the README's examples pass the actor to a module's core, and warn that the startup check cannot see a missing
  row-level security rule behind `AllowAnyResource()`.

## [3.0.0] — 2026-10-17

A module now has a typed identity, a route labelled with an operation is closed unless it declares its
authorization, and a unit of work opens in a space, for an actor, which the database itself can read. The three
packages move together to 3.0.0; `Stellaegis.Primitives.AspNetCore` breaks its API.

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
- `SingleCompartment(Compartment)`: takes the constant an application declares once for its single
  compartment, which its work outside requests passes to the modules too. The documentation recommends
  naming that compartment after the organization owning the data, never a placeholder such as `main`, so
  that taking a second organization migrates nothing.
- `Space.ToSegment()` and `Space.SharedSegment`: the one projection of a space into an address segment — a
  vault path, a blob path, a cache key —, the compartment identifier or `_shared`. Not a serialization:
  nothing reads a space back from it (`Stellaegis.Primitives`).
- `ISpaceUnitOfWorkFactory<TDatabase>` and `AdoSpaceUnitOfWorkFactory<TDatabase>`: open a unit of work in
  a `Space`, for an `Actor`. The space may choose the connection, through a function of the space or one
  `DbDataSource` shared by every space, and both are set in the connection's session context for the
  session, with or without a transaction (`Stellaegis.Primitives.Data`).
- `SessionContext.SqlServer`, `SessionContext.PostgreSql` and `SessionContext.None`: how the engine
  receives `stellaegis.space` and `stellaegis.actor`. PostgreSQL refuses a connection setting
  `No Reset On Close=true`, with which Npgsql would hand the context to the next user of the connection.
- `InMemorySpaceUnitOfWorkFactory<TDatabase>`: the in-memory counterpart, which takes the repositories of
  each unit of work from a function of the space and the actor (`Stellaegis.Primitives.Data`).
- `IUnitOfWork` is also `IDisposable`: a plain `using` rolls back an open transaction and closes the
  connection, as `await using` does (`Stellaegis.Primitives.Data`).

### Changed

- **Breaking:** the startup check fails when a route labelled with an `Operation` declares no authorization —
  neither a requirement (`RequireAuthorization`, `RequireAuthorizationPerOperation`, an `[Authorize]`
  attribute) nor `AllowAnonymous()` —, naming the routes. Such a route used to be served open. A fallback
  policy does not count: it does not know the operation, and would open every operation of a module to any
  user it admits (`Stellaegis.Primitives.AspNetCore`).
- **Breaking:** `RequireRequestContext(acceptsSharedSpace = false)` no longer takes the module name: the
  module is declared by `BelongsTo<TModule>()`, which a module without compartments calls alone.
- **Breaking:** `Operation(target, name, kind)` takes the type of data it acts on, `Target`, instead of the
  module, `Module`: one module may act on several targets, `invoices` and `fiscal-years`. Its identity is
  `target.name`, such as `invoices.validate`. Target and name follow the rule of module names — lowercase
  letters, digits and single hyphens —, where any non-blank string was accepted.
- The documentation presents an operation as what a route does, granting nothing: a policy per operation,
  `op => op.ToString()`, is the usual way to secure a module, and a policy per kind a coarse fallback, the
  kind being a fact about the data, not a right. `AllowAnyResource()` also suits a database that filters
  what a route loads by row-level security.
- The documentation of `Compartment` no longer offers a user as a compartment: a compartment isolates an
  organization, and the records a user owns are a matter of ownership within it.

### Fixed

- The startup check fails when two routes share an endpoint name, naming the name and both routes. ASP.NET
  Core lets such a host start, then fails every request: a module naming its routes and mapped once per
  compartment and once in the shared space did exactly that, as the README showed
  (`Stellaegis.Primitives.AspNetCore`).

### Updating from 2.0.0

- Declare the identity of each module, `public sealed class DocumentsModule : IStellaegisModule`, and its
  routes with `BelongsTo<DocumentsModule>()` on the group; `RequireRequestContext("Documents", …)` becomes
  `RequireRequestContext(…)`.
- `new Operation("documents", "read", …)` keeps compiling: its first argument is now the target. Rename it
  where the target differs from the module, and make target and name lowercase.
- Give every labelled route an authorization requirement or `AllowAnonymous()`; an application relying on
  its fallback policy declares `RequireAuthorization()` on the module's group.
- An application using only modules without compartments calls `AddStellaegisStartupCheck()`.
- A module mapped twice drops `WithName(...)` from its routes, or names each mapping apart.
- Code using `Stellaegis.Primitives` and `Stellaegis.Primitives.Data` compiles unchanged: their changes are
  additions. A class of your own implementing `IUnitOfWork` adds `Dispose()`.

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

[Unreleased]: https://github.com/Stellaegis/Stellaegis.Primitives/compare/v3.0.0...HEAD
[3.0.0]: https://github.com/Stellaegis/Stellaegis.Primitives/compare/v2.0.0...v3.0.0
[2.0.0]: https://github.com/Stellaegis/Stellaegis.Primitives/compare/v1.1.0...v2.0.0
[1.1.0]: https://github.com/Stellaegis/Stellaegis.Primitives/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/Stellaegis/Stellaegis.Primitives/releases/tag/v1.0.0
