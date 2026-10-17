# Stellaegis.Primitives

Stellaegis.Primitives is a small set of .NET building blocks shared by the Stellaegis modules:
operation results, argument guards, events, and a unit of work over ADO.NET. Each Stellaegis module is
a library published on its own. These primitives are the part they have in common, so that a module
does not carry its own copy of them.

You can use them without any other Stellaegis package. They know nothing of tenants or of a message
bus: those are decisions of the application that hosts them. HTTP is confined to a package of its own,
which only the modules exposing endpoints reference.

The library targets .NET 10 and is released under the MIT license.

## Packages

The library is split in three, so that code which never touches a database takes no data-access
dependency, and code that exposes no endpoint takes no dependency on ASP.NET Core.

| Package | Contents | Depends on |
| --- | --- | --- |
| `Stellaegis.Primitives` | `Result` and `Error`, `Guard`, `UserFacingException`, `Compartment`, `Space` and `Actor`, the module identity, the event marker and the publication port | .NET only |
| `Stellaegis.Primitives.Data` | The unit of work, its relational and in-memory implementations, a snake_case column map and type handlers for Dapper | `Stellaegis.Primitives`, Dapper |
| `Stellaegis.Primitives.AspNetCore` | The routes' module, the request context port, giving the space and the actor of a request; the operations that let an application secure a module's routes and data; the translation of errors to HTTP answers | `Stellaegis.Primitives`, the ASP.NET Core shared framework |

Depending on `Stellaegis.Primitives.Data` therefore says something about a library: it reads or
writes a relational database. Depending on `Stellaegis.Primitives.AspNetCore` says that it maps HTTP
endpoints.

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

The documentation of each category gives the HTTP status it corresponds to — 404 for `NotFound`, for
instance. `Stellaegis.Primitives` itself never produces an HTTP response: the translation lives in
`Stellaegis.Primitives.AspNetCore` (see *Errors as HTTP answers*).

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

## Compartments and actors

A module partitions its data by **compartment**: a tenant, an organization, or the single compartment
of a project that needs only one. A compartment isolates — what is in one is invisible from another —, it
does not classify: an application that must cross the data of two has chosen the wrong boundary. Nor is a
person a compartment: the records a user owns belong to the user within a compartment, by an ownership the
application checks. A module records who performs an operation as an
**actor**. Both are strings on the wire and in a database. In code they are distinct types, so that a
compartment cannot be passed where an actor or a document type is expected.

```csharp
var acme = Compartment.Parse("acme");          // a value the code knows to be valid
if (!Compartment.TryParse(routeValue, out var compartment))
    return Results.NotFound();                  // a value read from a request

var actor = Actor.Parse("job:purge");
```

A compartment is an identifier of 3 to 63 characters: a lowercase letter first, then lowercase letters,
digits and single hyphens, ending with a letter or a digit. The type knows no special value: you name
every compartment. To compartment by a technical identifier, prefix it — `o` followed by an
organization's GUID in the `N` format, for instance.

Data that belongs to no compartment is not stored in a compartment set apart for it, since any name the
rule accepts could also be a real compartment. A module that holds such data has a **shared space**. A
`Space` is where a module acts: a compartment, or `Space.Shared`.

```csharp
Space space = compartment;                      // the space of a compartment
if (space.IsShared) { ... }                     // otherwise space.Compartment is not null
```

The shared space is not a compartment, so its `Compartment` is `null`. A `null` compartment converts to
a `null` space, never to the shared space: a compartment that could not be resolved never grants access
to shared data. A module that has a shared space decides how it stores it — a `null` column, for
instance; `Space` has no serialization, and nothing reads a space back from text.

Where a space becomes part of an address — a vault path, a blob path, a cache key — every module projects
it the same way, with `ToSegment()`: the compartment identifier, or `_shared` (`Space.SharedSegment`),
which no compartment can carry since the rule refuses the underscore.

```csharp
var path = $"messaging.{space.ToSegment()}";   // messaging.acme, or messaging._shared
```

Uppercase is rejected rather than converted. Two equal compartments therefore always carry the same
string, and an ordinal comparison is enough in a database, a cache key or a file path. The cost falls
on the code that reads the input: converting a user-typed value to lowercase is its job.

An actor comes from a token whose format is not under your control, so it follows no grammar: 1 to 128
characters, no control character, no leading or trailing whitespace. It does not tell users, machines
and scheduled tasks apart. If your records need to, prefix the value, as `job:purge` does.

Both types implement `IParsable<T>`: `Parse` throws a `FormatException` on an invalid value, `TryParse`
returns `false`. Both serialize to JSON as a plain string, also as a dictionary key, with reflection or
with source generation.

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
back, whether with `await using` or a plain `using`; disposing it twice does nothing.

`Repo<T>()` builds the repository on first use and returns the same instance afterwards. The type it
returns is checked by the compiler. Whether a factory was registered for that type is only checked
when the code runs: a missing registration throws `RepositoryNotRegisteredException` on the first
call. That is the price of resolving repositories on demand instead of listing them as properties of
a dedicated unit-of-work class per database.

### Opening a unit of work in a space

A module with compartments opens its units of work in a space, for an actor, through
`ISpaceUnitOfWorkFactory<TDatabase>`. The space may choose the database, and both are set in the
connection's **session context**, so that the database itself knows who works where: a row-level
security policy, an audit trigger or a function of the application reads them.

```csharp
// one database per compartment
var factory = new AdoSpaceUnitOfWorkFactory<InvoicingDatabase>(
    (space, ct) => tenantDatabases.OpenAsync(space.ToSegment(), ct), registry, SessionContext.PostgreSql, publisher);

// or one database for every space
var factory = new AdoSpaceUnitOfWorkFactory<InvoicingDatabase>(dataSource, registry, SessionContext.SqlServer, publisher);

await using var uow = await factory.OpenAsync(space, actor, ct);
```

The context is set as the connection opens, for the session: it holds without a transaction and across
successive ones, exactly as the unit of work is used otherwise. An application with a single compartment
opens in that compartment.

| | Set as | Read back as |
| --- | --- | --- |
| SQL Server, `SessionContext.SqlServer` | `sp_set_session_context`, read-only | `SESSION_CONTEXT(N'stellaegis.space')`, `SESSION_CONTEXT(N'stellaegis.actor')` |
| PostgreSQL, `SessionContext.PostgreSql` | `set_config(…, false)` | `current_setting('stellaegis.space', true)`, `current_setting('stellaegis.actor', true)` |

The space is `space.ToSegment()` — the compartment, or `_shared` — and the actor its value. A session
without them sees nothing, so a policy written that way fails closed. `SessionContext.None` sets nothing,
for an engine without a session context such as SQLite.

Both engines reset a pooled connection before its next user, so a context never outlives its unit of
work; the tests prove it against real engines. Npgsql can be told not to reset, with
`No Reset On Close=true`: `SessionContext.PostgreSql` refuses such a connection rather than hand one
user's identity to the next. Under SQL Server, the read-only context cannot be changed inside the unit
of work, and Microsoft.Data.SqlClient offers no way to skip the reset.

`IUnitOfWorkFactory<TDatabase>` stays, without a space, for code that has none: a database of its own,
with no compartment.

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

## Compartments and actors in Dapper

`PrimitiveTypeHandlers.Register()` lets Dapper read a `Compartment` or an `Actor` from a string column
and send one as a parameter, in properties as in positional records. Register it once, next to the type
maps:

```csharp
static DocumentStore()
{
    PrimitiveTypeHandlers.Register();
    SnakeCaseTypeMap<DocumentRow>.Register();
}
```

A parameter is sent exactly as Dapper sends a string, so replacing a `string` by one of these types
leaves the SQL unchanged. A `NULL` column reads as `null`. A stored value that breaks the type's rule
fails the read with a `DataException` naming the column: it means corrupted data, or a column without
the constraint that should have refused it.

Dapper expands a list parameter without type handlers, and the provider then rejects the elements. Pass
a list as its values:

```csharp
new { compartments = compartments.Select(c => c.Value) }   // where compartment in @compartments
```

## The identity of a module

A module declares its identity once, as a type carrying its name. Code that needs to know a module takes the
type, never a string, so the name is written in one place:

```csharp
public sealed class DocumentsModule : IStellaegisModule
{
    public static string Name => "documents";
}
```

The name is 1 to 63 lowercase letters, digits and single hyphens, starting with a letter and ending with a
letter or a digit. `IStellaegisModule` lives in `Stellaegis.Primitives`: a module without endpoints declares
its identity the same way.

A module that exposes endpoints declares that its routes belong to it, on the group it maps onto — with or
without compartments:

```csharp
var group = routes.MapGroup("/rendering").BelongsTo<RenderingModule>();
```

Every route labelled with an `Operation` belongs to a module, and the startup check fails on one that does
not; an application labelling routes of its own declares a module of its own. Two module types carrying the
same name fail the startup too. `BelongsTo` fails when the startup check is not registered: a module without
compartments registers it from its own registration with `AddStellaegisStartupCheck()`, and
`AddStellaegisRequestContext` registers it for the others.

## The request context of an HTTP module

A module that exposes endpoints needs two things from each request: the space it acts in — a compartment,
or the shared space — and the actor performing it. Where the compartment and the actor come from is the
application's decision: a route segment, a claim, a subdomain, a constant. The module reads them through a
port, `IRequestContext`. The application registers the source it is read from.

```csharp
builder.Services.AddStellaegisRequestContext(c => c.SingleCompartment("acme"));
```

An application with a single compartment names it after the organization that owns the data, never a
placeholder such as `main`: the day it manages a second organization, the first keeps its name, and nothing
is migrated. It declares that compartment once, as a constant, which its work outside requests — a
scheduled task, an import — passes to the modules too:

```csharp
public static class Tenant
{
    public static readonly Compartment Owner = Compartment.Parse("acme");
}

builder.Services.AddStellaegisRequestContext(c => c.SingleCompartment(Tenant.Owner));
```

The configurable source takes one compartment source — `SingleCompartment`, `CompartmentFromRoute`,
`CompartmentFromClaim` or `CompartmentFrom` with a function of the `HttpContext` — and an optional actor
source, `ActorFromClaim` or `ActorFrom`. Declaring no compartment source, or two, fails at registration.

```csharp
builder.Services.AddStellaegisRequestContext(c => c
    .CompartmentFromRoute("org")      // /api/orgs/{org}/...
    .ActorFromClaim("oid"));          // the Microsoft Entra object id
```

An application with a resolution of its own, such as a tenant middleware, implements
`IRequestContextSource` and registers it instead. Both are registered per request.

```csharp
sealed class TenantSource(ITenantAccessor tenants, IUserAccessor users) : IRequestContextSource
{
    public Compartment? Compartment => tenants.Current?.Compartment;
    public Actor Actor => users.Current;
}

builder.Services.AddStellaegisRequestContext<TenantSource>();
```

The space is `null` when the compartment cannot be resolved — a missing or invalid route value, for
instance — and a module's endpoint then answers 404. There is no fallback compartment: a source that
wants one names it explicitly.

The actor is always present. By default it is read from the `NameIdentifier` claim, then `sub`, and it
is `anonymous` when no user is authenticated; `AnonymousActor` changes that label. A module never
refuses a request for lack of an actor: whether a request needs an authenticated user is decided by the
security the application attaches to the routes. An authenticated user without the claim, or with a
value that is not a valid `Actor`, fails the request: the claim chosen is the wrong one, and recording
the request as anonymous would attribute it to nobody.

### The shared space

A module that holds data belonging to no compartment serves it in the shared space. The application
chooses the routes that reach it, by marking a group:

```csharp
app.MapGroup("/api/orgs/{org}").MapDocumentsEndpoints().RequireAuthorization("member");
app.MapGroup("/api/shared").MapDocumentsEndpoints().InSharedSpace().RequireAuthorization("platform-admin");
```

On a marked route the space is `Space.Shared`, and the source's compartment is not read. A source knows
nothing of the shared space and cannot produce it: the mark is the only way in. A group mapped without the
mark reads the compartment as usual, and answers 404 where there is none.

A module mapped twice cannot name its routes: ASP.NET Core requires endpoint names unique across the host,
and fails every request on a duplicate. The startup check fails first, naming the routes.

### Inside a module

An endpoint receives the port by injection. The mapping method declares its group with `BelongsTo`, then
`RequireRequestContext`, which fails the startup when no request context is registered, naming the line
to add, and states whether the module accepts the shared space:

```csharp
public static RouteGroupBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder routes)
{
    var group = routes.MapGroup("/documents")
        .BelongsTo<DocumentsModule>()
        .RequireRequestContext(acceptsSharedSpace: true);
    group.MapGet("/{id}", (Guid id, IRequestContext context, DocumentService documents) =>
        context.Space is { } space ? documents.Get(space, id) : Results.NotFound());
    return group;
}
```

Not every module has a shared space, and refusal is the default: a module without one writes
`.RequireRequestContext()`, and an application marking it `InSharedSpace()` fails to start.
The declaration closest to a route counts, so a sub-group declares again to differ — administration
routes kept out of the shared space, for instance. A marked route that declares no request context fails
the startup too; an application serving routes of its own in the shared space declares them the same way.

The port serves the endpoints and filters only. The module's core takes the space and the actor as
parameters — a `Space`, never `null`: a singleton receiving the port would keep the context of the first
request, and work done outside a request — a scheduled task, an import — has none.

## Securing a module's routes and data

A module does not decide who may call it. It labels each route with an `Operation`, which says what the
route does, and the application attaches its own security to the group the module returns.

```csharp
public static readonly Operation ValidateInvoices = new("invoices", "validate", OperationKind.Write);

group.MapPost("/invoices/{id}/validate", ...).WithMetadata(ValidateInvoices);
```

An operation carries two things, and grants neither:

| | What it says | Example |
| --- | --- | --- |
| Its identity, `target.name` | which operation — the key an application grants | `invoices.validate` |
| Its kind | what it does to the data, declared rather than deduced from the HTTP verb | `Write` |

The target is a type of data, never an instance: one module may act on several targets, `invoices` and
`fiscal-years`. Target and name follow the rule of module names. The kind — `Read`, `Write` or `Delete` —
is a fact about the data, read for audit or to refuse writes during a maintenance as much as for security.
Administration is not a kind: it is an audience, and a module maps its administration routes separately.

| What | Who decides | How |
| --- | --- | --- |
| Which operation a route performs | the module | `Operation` metadata |
| Which module a route belongs to | the module | `BelongsTo<TModule>()` |
| Who may call a route | the application | `.RequireAuthorization(...)`, a policy per operation, or `.AllowAnonymous()` — one of them, always |
| Who may access a resource the route loads | the application | an authorization handler on the resource, or `.AllowAnyResource()` |

### A policy per operation

```csharp
app.MapGroup("/api").MapDocumentsEndpoints()
   .RequireAuthorizationPerOperation(op => op.ToString());   // "documents.read", "documents.write", ...
```

The application declares one policy per operation, which its rights system answers. An application without
rights of its own may choose by kind instead — `op => op.Kind == OperationKind.Read ? "reader" : "writer"` —
a coarse fallback, which a module's separate administration routes keep from opening a purge.

`RequireAuthorizationPerOperation` is a final convention: an ordinary group convention runs before a route
has its own metadata, and could not see the operation. A route without an operation is left to the
application's fallback policy, if it sets one; a route with one is not, since the fallback policy does not
know the operation.

ASP.NET Core runs final conventions when it builds its endpoints, at the first request. Do not add an
endpoint filter from one: ASP.NET Core ignores it without an error.

### Access to a resource

Once a route has loaded a resource, the module asks ASP.NET Core's resource-based authorization, with the
route's operation as the requirement:

```csharp
if (document is null || !await http.AuthorizeResourceAsync(document))
    return Results.NotFound();
```

The application answers with an ordinary handler — `AuthorizationHandler<Operation, Document>` — and a
refusal is answered as an absence. Refusal is the default: without a handler, nothing is accessible. An
application that lets any user reaching the routes access any resource they load declares it, as it would
declare `[AllowAnonymous]` — so does an application whose database already filters what a route loads, by
row-level security:

```csharp
app.MapGroup("/api").MapDocumentsEndpoints().RequireAuthorization().AllowAnyResource();
```

### Checked at startup

`AddStellaegisStartupCheck()`, which registering a request context calls too, registers a startup check
of the routes. The application fails to start, with a message naming the routes and the way out, when:

- two routes share a name — ASP.NET Core would fail every request;
- two module types carry the same name;
- a route marked `InSharedSpace()` has no declaration accepting the shared space;
- a route labelled with an operation belongs to no module;
- it declares no authorization — neither a requirement nor `AllowAnonymous()`, a fallback policy not
  counting;
- it has neither an authorization handler for operations nor `AllowAnyResource()`;
- a policy it requires is not registered.

Handler detection resolves the registered handlers, so a handler registered by a factory is seen. It
cannot know which resources a handler covers: one handler for operations is enough to pass, and a handler
written directly against `IAuthorizationHandler` counts. The check turns an unexplained 404 into a
message; the protection itself is refusal by default.

## Errors as HTTP answers

`Stellaegis.Primitives.AspNetCore` holds the one translation of errors to HTTP, so that every module
answers the same way.

| `ErrorType` | Status |
| --- | --- |
| `Validation` | 400 |
| `Unauthorized` | 401 |
| `Forbidden` | 403 |
| `NotFound` | 404 |
| `Conflict` | 409 |
| `PreconditionFailed` | 412 |
| `Failure` | 500 |

An endpoint returns its `Result` as an HTTP answer: 200 with the value, 204 without one, or problem
details ([RFC 9457](https://www.rfc-editor.org/rfc/rfc9457)) on failure.

```csharp
group.MapGet("/{id}", async (Guid id, DocumentService documents) =>
    (await documents.GetAsync(id)).ToHttpResult());
```

The problem details carry the error message as `detail` and the error code as a `code` extension; `type`
and `title` are ASP.NET Core's defaults for the status. The message of a `Failure` is hidden outside the
Development environment, since it usually describes a technical cause. The code is always kept, so that
a client can tell errors apart without parsing a message.

`AddErrorTranslation()` adds a filter to a group of routes, for the exceptions meant for the caller:

| Exception | Answer |
| --- | --- |
| `UserFacingException` | the status of its `ErrorType`, with its message |
| `ArgumentException`, from a guard for instance | 400, its message hidden outside Development: it names an internal parameter |
| any other | not handled: it goes on to the application's exception handler, usually a 500 |

`errorType.ToStatusCode()` and `error.ToProblemDetails()` are public too, for code that writes its answer
itself. `ToProblemDetails` copies the message as it is: hiding it is then the caller's decision.

## License

MIT — see [LICENSE](LICENSE). The packages reference third-party packages listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
