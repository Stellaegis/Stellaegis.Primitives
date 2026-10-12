# Stellaegis.Primitives

Stellaegis.Primitives is a small set of .NET building blocks shared by the Stellaegis modules:
operation results, argument guards, events, and a unit of work over ADO.NET. Each Stellaegis module is
a library published on its own. These primitives are the part they have in common, so that a module
does not carry its own copy of them.

You can use them without any other Stellaegis package. They know nothing of tenants, of HTTP or of a
message bus: those are decisions of the application that hosts them.

The library targets .NET 10 and is released under the MIT license.

## Packages

The library is split in two, so that code which never touches a database takes no data-access
dependency.

| Package | Contents | Depends on |
| --- | --- | --- |
| `Stellaegis.Primitives` | `Result` and `Error`, `Guard`, `UserFacingException`, the event marker and the publication port | .NET only |
| `Stellaegis.Primitives.Data` | The unit of work, its relational and in-memory implementations, and a snake_case column map for Dapper | `Stellaegis.Primitives`, Dapper |

Depending on `Stellaegis.Primitives.Data` therefore says something about a library: it reads or
writes a relational database.

## Results and errors

An operation that can fail for an expected reason — invalid input, a missing record, a conflict —
returns a `Result` or a `Result<T>` instead of throwing. Exceptions remain for failures nobody can
plan for, such as a lost connection.

```csharp
public async Task<Result<Party>> GetAsync(Guid id, CancellationToken ct)
{
    var party = await store.FindAsync(id, ct);
    return party is null
        ? Result.Failure<Party>(Error.NotFound("Party.NotFound", $"Party {id} does not exist."))
        : Result.Success(party);
}
```

A result is either a success or a failure, never both: a successful result carries no error, and a
failed one always carries one. Reading `Value` on a failed `Result<T>` throws, so check `IsSuccess`
first. `ValueOrDefault` returns the default value instead, and it is the property written when a
result is serialized to JSON.

An `Error` has a code, a message and a category, `ErrorType`. Each category has a factory named after
it: `Error.Validation`, `Error.NotFound`, `Error.Conflict`, `Error.Unauthorized`, `Error.Forbidden`,
`Error.PreconditionFailed` and `Error.Failure`. Two errors are equal when their code and category are
equal. The message is not compared, so that rewording a message does not change how errors are matched.

The documentation of each category gives the HTTP status it usually corresponds to — 404 for
`NotFound`, for instance. That correspondence is a suggestion for the host. The library itself never
produces an HTTP response.

## Guards

`Guard` checks the arguments of a method and returns them, so that a check and an assignment fit on one
line. The parameter name in the exception is captured from the call site, without `nameof`.

```csharp
public Invoice(string number, Guid customerId)
{
    Number = Guard.NotWhiteSpace(number);
    CustomerId = Guard.NotEmpty(customerId);
}
```

The available checks are `NotNull`, `NotEmpty` (for a string or a `Guid`), `NotWhiteSpace` and
`Satisfies`, which takes a predicate describing the required condition. A failed guard throws an
`ArgumentException` or an `ArgumentNullException`. It signals a programming error, so it is not meant
to be shown to an end user.

## Errors meant for the end user: `UserFacingException`

When returning a `Result` is not practical, `UserFacingException` carries a message that is safe to
show to the end user, whatever the environment. It also carries an `ErrorType`, `Validation` by
default, so that the host can translate it into a status code with the same table it uses for
`Error`. A library that throws it does not need to know whether the caller is an HTTP endpoint, a
background worker or a command-line tool.

The cost of this choice is precision: a category cannot express every HTTP status, such as 422 or
429. A host that needs one of those statuses derives its own exception type and maps it explicitly.

## Events

An event is a fact that a library reports once it has happened. It implements `IEvent`, which
requires only `OccurredAt`. Everything a subscriber needs is a property of the event. The name the
event carries on a message bus, and who receives it, are declared by the host when it registers the
event type — the event itself holds no routing information.

```csharp
public sealed record InvoiceIssued(Guid InvoiceId, string Number, DateTimeOffset OccurredAt) : IEvent;

await publisher.PublishAsync(new InvoiceIssued(invoice.Id, invoice.Number, DateTimeOffset.UtcNow), ct);
```

A library publishes through `IEventPublisher`. The host connects that interface to its transport.
Without a transport, `NullEventPublisher.Instance` discards every event.

Publication is best effort. The committed data is the reference, and an event only lets subscribers
learn about a change sooner. An implementation of `IEventPublisher` does not throw when the transport
fails: it logs the failure and returns. The calling code therefore needs no `try/catch` around a
publication.

This guarantee is deliberately modest. An event can be lost if the process stops between the commit and
the publication. A host that needs every event delivered at least once uses a transactional outbox: the
event is written to a table in the same transaction as the data, and a separate process sends it
afterwards. The outbox needs a table, a background process and a serialization format, so it belongs
to the host, not to this library.

## Unit of work

`Stellaegis.Primitives.Data` provides a unit of work: an object that holds one database connection
and, when needed, one transaction, for the duration of one operation. The repositories of that
operation share the connection and the transaction. Committing or rolling back is decided by the
service that runs the operation, never by a repository.

### Declaring a database

Each database is designated by a marker type: an empty class that your library declares.

```csharp
public sealed class InvoicingDatabase;
```

The factory and the repository registry both carry that type: `IUnitOfWorkFactory<InvoicingDatabase>`
and `RepositoryRegistry<InvoicingDatabase>`. An application that combines several libraries, each with
its own database, registers one factory per database and resolves each of them by its type, without
named or keyed registrations.

### Registering repositories

A repository receives the connection and a function that returns the current transaction, or `null`
outside a transaction. It passes that transaction to each command, without having to know whether one
is active.

```csharp
var registry = new RepositoryRegistry<InvoicingDatabase>()
    .Add<IInvoiceRepository>((connection, transaction) => new InvoiceRepository(connection, transaction));
```

The registry is filled once at startup and shared by every unit of work. Registering the same
repository type twice throws.

### Opening a unit of work

`AdoUnitOfWorkFactory<TDatabase>` opens its connections in one of two ways:

- from a `DbDataSource`, for a database that does not change — an `NpgsqlDataSource`, for instance;
- from a function called each time a unit of work is opened. Use it when the database depends on the
  context of the call, for example the customer a web request belongs to.

```csharp
var factory = new AdoUnitOfWorkFactory<InvoicingDatabase>(dataSource, registry, publisher);

await using var uow = await factory.OpenAndBeginAsync(ct);
var invoices = uow.Repo<IInvoiceRepository>();
await invoices.InsertAsync(invoice, ct);
uow.Publish(new InvoiceIssued(invoice.Id, invoice.Number, DateTimeOffset.UtcNow));
await uow.CommitAsync(ct);
```

`OpenAsync` opens a unit of work without a transaction, for reads. `OpenAndBeginAsync` also begins a
transaction. Disposing a unit of work whose transaction was neither committed nor rolled back rolls it
back.

`Repo<T>()` builds the repository on first use and returns the same instance afterwards. The type it
returns is checked by the compiler. Whether a factory was registered for that type is only checked
when the code runs: a missing registration throws `RepositoryNotRegisteredException` on the first
call. That is the price of resolving repositories on demand instead of listing them as properties of
a dedicated unit-of-work class per database.

### Publishing from a unit of work

`uow.Publish(event)` holds the event until the transaction ends. After a successful commit, the unit
of work hands the events to the `IEventPublisher`, in the order they were published. On rollback, or if
the unit of work is disposed without committing, the events are discarded. A subscriber is therefore
never told about a change that was not saved.

`Publish` requires an active transaction and throws without one. Outside a transaction, publish
through `IEventPublisher` directly.

### In-memory unit of work, for tests

`InMemoryUnitOfWorkFactory<TDatabase>` has the same interface and resolves repositories from an
`InMemoryRepositoryRegistry<TDatabase>`. Code written against `IUnitOfWorkFactory<TDatabase>` runs
unchanged on either implementation, which lets tests exercise a service without a database.

There is no real transaction in memory. A rollback restores only the repositories that implement
`IRestorableRepository`: the unit of work asks each of them for a snapshot when the transaction begins,
and gives it back on rollback.

```csharp
internal sealed class InMemoryInvoiceRepository : IInvoiceRepository, IRestorableRepository
{
    private List<Invoice> _invoices = [];

    public object Snapshot() => _invoices.ToList();
    public void Restore(object snapshot) => _invoices = (List<Invoice>)snapshot;
    // ...
}
```

A snapshot copies the collection, not the objects in it. An object modified in place is not restored,
which is why the rows of an in-memory repository are best kept immutable, as records replaced on
update. Two units of work running at the same time on the same in-memory store are not isolated from
each other.

## Mapping snake_case columns with Dapper

Database columns are often named in snake_case (`display_name`) while C# properties use PascalCase
(`DisplayName`). `SnakeCaseTypeMap<T>` tells Dapper how to match them for one row type. Register it
once, for example in the static constructor of the repository that reads that type:

```csharp
static InvoiceRepository() => SnakeCaseTypeMap<InvoiceRow>.Register();
```

The map works for classes with settable properties and for positional records. When a column does not
follow the convention, `[ColumnMap]` names it explicitly. On a positional record, the attribute targets
the property:

```csharp
private sealed record InvoiceRow(
    [property: ColumnMap("invoice_unique_id")] Guid UniqueId,
    string Number,
    DateTimeOffset IssuedAt);
```

Dapper also offers a global setting, `DefaultTypeMap.MatchNamesWithUnderscores`. It applies to every type
in the process, including types that belong to other libraries, so a library should not set it. The
type map applies only to the types registered with it.

## License

MIT — see [LICENSE](LICENSE). The packages reference third-party packages listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
