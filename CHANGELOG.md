# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/Stellaegis/Stellaegis.Primitives/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Stellaegis/Stellaegis.Primitives/releases/tag/v1.0.0
