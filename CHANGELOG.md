# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] — unreleased

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

[1.0.0]: https://github.com/Stellaegis/Stellaegis.Primitives/releases/tag/v1.0.0
