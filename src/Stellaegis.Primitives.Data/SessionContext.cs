using System.Data.Common;

namespace Stellaegis.Primitives.Data;

/// <summary>
/// How a unit of work opened in a space tells the database who works where: it sets
/// <see cref="SpaceKey"/> to <see cref="Space.ToSegment"/> and <see cref="ActorKey"/> to the actor, for the
/// session, as soon as the connection is open — with or without a transaction. When someone acts on the actor's
/// behalf, <see cref="Actor.Via"/>, it sets <see cref="ViaKey"/> too; otherwise the key is absent.
/// </summary>
/// <remarks>
/// <para>
/// The database reads them back — <c>SESSION_CONTEXT(N'stellaegis.space')</c> under SQL Server,
/// <c>current_setting('stellaegis.space', true)</c> under PostgreSQL — from row-level security, an audit trigger,
/// or a function the application writes. A session without them sees nothing: a policy written that way fails
/// closed.
/// </para>
/// <para>
/// Both engines reset a pooled connection before handing it to its next user, so the context never outlives
/// the unit of work. Npgsql can be told not to, with <c>No Reset On Close=true</c>: <see cref="PostgreSql"/>
/// refuses such a connection rather than hand one user's identity to the next.
/// </para>
/// </remarks>
public sealed class SessionContext
{
    /// <summary>The session key holding the space, as <see cref="Space.ToSegment"/> gives it.</summary>
    public const string SpaceKey = "stellaegis.space";

    /// <summary>The session key holding the actor.</summary>
    public const string ActorKey = "stellaegis.actor";

    /// <summary>The session key holding who acts on the actor's behalf, set only when someone does.</summary>
    public const string ViaKey = "stellaegis.actor_via";

    private readonly string _name;
    private readonly string? _sql;
    private readonly string? _sqlWithVia;
    private readonly Action<DbConnection>? _check;

    private SessionContext(string name, string? sql, string? sqlWithVia, Action<DbConnection>? check)
    {
        _name = name;
        _sql = sql;
        _sqlWithVia = sqlWithVia;
        _check = check;
    }

    /// <summary>
    /// SQL Server: <c>sp_set_session_context</c>, read-only, so that nothing in the unit of work can change who
    /// works where.
    /// </summary>
    public static SessionContext SqlServer { get; } = new(
        "SQL Server",
        $"exec sp_set_session_context @key = N'{SpaceKey}', @value = @space, @read_only = 1; "
        + $"exec sp_set_session_context @key = N'{ActorKey}', @value = @actor, @read_only = 1;",
        $"exec sp_set_session_context @key = N'{SpaceKey}', @value = @space, @read_only = 1; "
        + $"exec sp_set_session_context @key = N'{ActorKey}', @value = @actor, @read_only = 1; "
        + $"exec sp_set_session_context @key = N'{ViaKey}', @value = @via, @read_only = 1;",
        check: null);

    /// <summary>
    /// PostgreSQL: <c>set_config</c> for the session. Refuses a connection whose string sets
    /// <c>No Reset On Close=true</c>, with which Npgsql would hand the context to the next user of the connection.
    /// </summary>
    public static SessionContext PostgreSql { get; } = new(
        "PostgreSQL",
        $"select set_config('{SpaceKey}', @space, false), set_config('{ActorKey}', @actor, false)",
        $"select set_config('{SpaceKey}', @space, false), set_config('{ActorKey}', @actor, false), set_config('{ViaKey}', @via, false)",
        RefuseNoResetOnClose);

    /// <summary>
    /// Sets nothing: an engine without a session context, such as SQLite, or an application whose database reads
    /// none. A database that expects the context and finds none sees nothing.
    /// </summary>
    public static SessionContext None { get; } = new("none", sql: null, sqlWithVia: null, check: null);

    /// <summary>The engine the context is written for.</summary>
    public override string ToString() => _name;

    /// <summary>Fails before the connection opens when it cannot hold the context safely.</summary>
    internal void EnsureSupports(DbConnection connection) => _check?.Invoke(connection);

    internal async Task ApplyAsync(DbConnection connection, Space space, Actor actor, CancellationToken cancellationToken)
    {
        if (_sql is null)
            return;

        await using var command = connection.CreateCommand();
        command.CommandText = actor.Via is null ? _sql : _sqlWithVia;
        AddParameter(command, "space", space.ToSegment());
        AddParameter(command, "actor", actor.Value);
        if (actor.Via is { } via)
            AddParameter(command, "via", via.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void RefuseNoResetOnClose(DbConnection connection)
    {
        var settings = new DbConnectionStringBuilder { ConnectionString = connection.ConnectionString };
        foreach (var key in (string[])["No Reset On Close", "NoResetOnClose"])
        {
            if (settings.TryGetValue(key, out var value) && bool.TryParse(value?.ToString(), out var noReset) && noReset)
            {
                throw new InvalidOperationException(
                    "The connection sets No Reset On Close=true: Npgsql would hand the session context — the space "
                    + "and the actor — to the next user of the pooled connection. Remove it from the connection "
                    + "string of a database opened in a space.");
            }
        }
    }
}
