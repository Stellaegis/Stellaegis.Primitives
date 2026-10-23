using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Stellaegis.Primitives.Data;

/// <summary>The engine a module's database runs on.</summary>
public enum DatabaseEngine
{
    /// <summary>PostgreSQL.</summary>
    PostgreSql,

    /// <summary>
    /// SQL Server, LocalDB included: installed and verified, without extensions, row-level security nor scripts
    /// following the frame, which wait for the first module that needs them.
    /// </summary>
    SqlServer,
}

/// <summary>A feature of the <see cref="ApplicationFrame"/> that a module's database follows with a script of its own.</summary>
public enum FrameFeature
{
    /// <summary><see cref="ApplicationFrame.OwnerIsolation"/>: the row-level security rule keeping each actor to its own rows.</summary>
    OwnerIsolation,
}

/// <summary>
/// A numbered script of a module's schema, <c>0001_initial.sql</c>: its version, its name and its text, with the
/// checksum the installation records and the verification reads back.
/// </summary>
public sealed class SqlScript
{
    /// <summary>Creates the script numbered <paramref name="version"/>, from 1, named <paramref name="name"/>.</summary>
    /// <exception cref="ArgumentException">The version is below 1, or the name or the text is blank.</exception>
    public SqlScript(int version, string name, string sql)
    {
        Version = Guard.Satisfies(version, v => v >= 1, "A script version starts at 1.");
        Name = Guard.NotWhiteSpace(name);
        Sql = Guard.NotWhiteSpace(sql);
        Checksum = ChecksumOf(sql);
    }

    /// <summary>The version, from 1, contiguous within a module.</summary>
    public int Version { get; }

    /// <summary>The name, such as <c>initial</c>.</summary>
    public string Name { get; }

    /// <summary>The text of the script.</summary>
    public string Sql { get; }

    /// <summary>The SHA-256 of the text, line endings and trailing whitespace set aside, in lowercase hexadecimal.</summary>
    public string Checksum { get; }

    /// <summary>The file name the script carries, <c>0001_initial.sql</c>.</summary>
    public string FileName => $"{Version:0000}_{Name}.sql";

    /// <summary>The checksum of a script text, as <see cref="Checksum"/> computes it.</summary>
    public static string ChecksumOf(string sql)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Guard.NotNull(sql).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd())));

    /// <inheritdoc/>
    public override string ToString() => FileName;
}

/// <summary>Reads the scripts a module embeds in its adapter.</summary>
public static partial class SqlScripts
{
    /// <summary>
    /// The numbered scripts embedded under <paramref name="resourcePrefix"/> in <paramref name="assembly"/>, such as
    /// <c>Stellaegis.Documents.PostgreSQL.Sql.PostgreSql</c> holding <c>0001_initial.sql</c>: ordered by version,
    /// contiguous from 1.
    /// </summary>
    /// <exception cref="InvalidOperationException">No script is found, or the versions are not contiguous from 1.</exception>
    public static IReadOnlyList<SqlScript> FromResources(Assembly assembly, string resourcePrefix)
    {
        Guard.NotNull(assembly);
        var prefix = Guard.NotWhiteSpace(resourcePrefix).TrimEnd('.') + ".";
        var scripts = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => (Name: name, Match: Numbered().Match(name[prefix.Length..])))
            .Where(resource => resource.Match.Success)
            .Select(resource => new SqlScript(
                int.Parse(resource.Match.Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture),
                resource.Match.Groups["name"].Value,
                ReadResource(assembly, resource.Name)))
            .OrderBy(script => script.Version)
            .ToList();
        if (scripts.Count == 0)
            throw new InvalidOperationException($"No numbered script is embedded under {prefix} in {assembly.GetName().Name}: expected 0001_<name>.sql.");
        return scripts;
    }

    /// <summary>The text of the embedded resource named <paramref name="resourceName"/> — a script following the frame, for instance.</summary>
    /// <exception cref="InvalidOperationException">The resource does not exist.</exception>
    public static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = Guard.NotNull(assembly).GetManifestResourceStream(Guard.NotWhiteSpace(resourceName))
            ?? throw new InvalidOperationException($"No embedded resource is named {resourceName} in {assembly.GetName().Name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"^(?<version>\d{4})_(?<name>[a-z0-9_]+)\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex Numbered();
}

/// <summary>A row-level security policy the verification expects on a table, <c>doc.documents</c> and <c>pl_documents_space</c>.</summary>
public sealed record RowPolicy(string Table, string Policy)
{
    /// <summary>The schema-qualified table.</summary>
    public string Table { get; } = ModuleDatabase.CheckTable(Table);

    /// <summary>The policy name.</summary>
    public string Policy { get; } = ModuleDatabase.CheckIdentifier(Policy, "policy");
}

/// <summary>
/// A script a module's database follows the frame with: applied when the frame declares the feature, removed when
/// it does not, both replayable; and the policies that must exist exactly when the feature is declared.
/// </summary>
public sealed record FrameScript(FrameFeature Feature, string ApplyName, string ApplySql, string RemoveName, string RemoveSql, IReadOnlyList<RowPolicy> Policies);

/// <summary>
/// What a module's adapter declares about its database, so that an installer sets it up under an administration
/// role and the application verifies it at startup under its own: the engine, the schema, the numbered scripts,
/// the extensions, the tables under row-level security and their policies, and the scripts that follow the frame.
/// <code>
/// public static readonly ModuleDatabase Value = ModuleDatabase.PostgreSql&lt;DocumentsModule&gt;("doc")
///     .WithScripts(SqlScripts.FromResources(typeof(DocumentsDatabase).Assembly, "Stellaegis.Documents.PostgreSQL.Sql.PostgreSql"))
///     .RequiringExtension("pgcrypto")
///     .WithRowSecurity("doc.documents", "pl_documents_space")
///     .WithRowSecurity("doc.document_versions", "pl_document_versions_document")
///     .WhenFrameDeclares(FrameFeature.OwnerIsolation, "owner_isolation", applySql, "owner_isolation_remove", removeSql,
///         new RowPolicy("doc.documents", "pl_documents_owner"));
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The installation is the installer's, the CLI's or the provisioner's, never the application's: it runs under a
/// role that owns the schema, and the application runs under a role that has no DDL right. The installation
/// records each script with its checksum in <c>{schema}._schema_migrations</c>, which it creates itself, and grants
/// the application role what it needs; the verification reads that table and the catalogue of the engine, nothing
/// more. A script applied with another content fails both: while no real data exists, a script is corrected in
/// place and the database reinstalled.
/// </para>
/// <para>
/// The adapter registers the declaration as a singleton of this type, beside its capabilities; the executants read
/// every registered <see cref="ModuleDatabase"/>. Names are identifiers: a schema, a policy, an extension follow
/// <see cref="NameRules.IdentifierRule"/>, a table is <c>schema.table</c>, so that no name reaches the engine
/// unquoted and unchecked.
/// </para>
/// <para>Immutable: each <c>With…</c> method returns a new declaration.</para>
/// </remarks>
public sealed class ModuleDatabase
{
    private ModuleDatabase(
        Type module, string name, DatabaseEngine engine, string schema,
        ImmutableList<SqlScript> scripts, ImmutableList<string> extensions, ImmutableList<string> securedTables,
        ImmutableList<RowPolicy> policies, ImmutableList<FrameScript> frameScripts)
    {
        Module = module;
        Name = name;
        Engine = engine;
        Schema = schema;
        Scripts = scripts;
        Extensions = extensions;
        SecuredTables = securedTables;
        Policies = policies;
        FrameScripts = frameScripts;
    }

    /// <summary>The database of <typeparamref name="TModule"/> on PostgreSQL, in <paramref name="schema"/>.</summary>
    /// <exception cref="ArgumentException">The module name or the schema is not a valid identifier.</exception>
    public static ModuleDatabase PostgreSql<TModule>(string schema) where TModule : IStellaegisModule
        => Create<TModule>(DatabaseEngine.PostgreSql, schema);

    /// <summary>
    /// The database of <typeparamref name="TModule"/> on SQL Server, in <paramref name="schema"/>: numbered scripts,
    /// without extensions, row-level security nor scripts following the frame.
    /// </summary>
    /// <exception cref="ArgumentException">The module name or the schema is not a valid identifier.</exception>
    public static ModuleDatabase SqlServer<TModule>(string schema) where TModule : IStellaegisModule
        => Create<TModule>(DatabaseEngine.SqlServer, schema);

    /// <summary>The module type, which carries its identity.</summary>
    public Type Module { get; }

    /// <summary>The module name.</summary>
    public string Name { get; }

    /// <summary>The engine.</summary>
    public DatabaseEngine Engine { get; }

    /// <summary>The schema holding the module's tables, and its <c>_schema_migrations</c>.</summary>
    public string Schema { get; }

    /// <summary>The numbered scripts, contiguous from 1.</summary>
    public IReadOnlyList<SqlScript> Scripts { get; }

    /// <summary>The extensions the schema requires, created by the installation before the scripts.</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>The tables that must be under row-level security, forced.</summary>
    public IReadOnlyList<string> SecuredTables { get; }

    /// <summary>The policies that must exist whatever the frame.</summary>
    public IReadOnlyList<RowPolicy> Policies { get; }

    /// <summary>The scripts that follow the frame.</summary>
    public IReadOnlyList<FrameScript> FrameScripts { get; }

    /// <summary>The name of the table recording the scripts applied, <c>{schema}._schema_migrations</c>.</summary>
    public string MigrationsTable => $"{Schema}._schema_migrations";

    /// <summary>Declares the numbered scripts, contiguous from 1.</summary>
    /// <exception cref="ArgumentException">The versions are not contiguous from 1, or a script is <see langword="null"/>.</exception>
    public ModuleDatabase WithScripts(IEnumerable<SqlScript> scripts)
    {
        var declared = Guard.NotNull(scripts).Select(script => Guard.NotNull(script)).OrderBy(script => script.Version).ToImmutableList();
        for (var i = 0; i < declared.Count; i++)
        {
            if (declared[i].Version != i + 1)
            {
                throw new ArgumentException(
                    $"The scripts of {Name} are not contiguous from 1: expected version {i + 1}, found {declared[i].FileName}.", nameof(scripts));
            }
        }
        return new(Module, Name, Engine, Schema, declared, Extensions.ToImmutableList(), SecuredTables.ToImmutableList(), Policies.ToImmutableList(), FrameScripts.ToImmutableList());
    }

    /// <summary>Declares that the schema requires the extension <paramref name="extension"/>, such as <c>pgcrypto</c>.</summary>
    /// <exception cref="ArgumentException">The name is not a valid identifier.</exception>
    /// <exception cref="NotSupportedException">The database runs on SQL Server, which has no extensions.</exception>
    public ModuleDatabase RequiringExtension(string extension)
        => Engine == DatabaseEngine.SqlServer
            ? throw new NotSupportedException($"{Name}: SQL Server has no extensions; what an extension brings on PostgreSQL is written otherwise there.")
            : new(Module, Name, Engine, Schema, Scripts.ToImmutableList(), Extensions.ToImmutableList().Add(CheckIdentifier(extension, "extension")),
            SecuredTables.ToImmutableList(), Policies.ToImmutableList(), FrameScripts.ToImmutableList());

    /// <summary>
    /// Declares that <paramref name="table"/>, <c>doc.documents</c>, is under row-level security, forced, with the
    /// policies named — those the frame does not decide.
    /// </summary>
    /// <exception cref="ArgumentException">The table is not <c>schema.table</c>, or a policy is not a valid identifier.</exception>
    /// <exception cref="NotSupportedException">The database runs on SQL Server, where row-level security waits for its case.</exception>
    public ModuleDatabase WithRowSecurity(string table, params IEnumerable<string> policies)
    {
        RefuseRowSecurityOnSqlServer();
        var checkedTable = CheckTable(table);
        var declared = Guard.NotNull(policies).Select(policy => new RowPolicy(checkedTable, policy)).ToList();
        return new(Module, Name, Engine, Schema, Scripts.ToImmutableList(), Extensions.ToImmutableList(),
            SecuredTables.ToImmutableList().Add(checkedTable), Policies.ToImmutableList().AddRange(declared), FrameScripts.ToImmutableList());
    }

    /// <summary>
    /// Declares a script the database follows the frame with: <paramref name="applySql"/> is run when the frame
    /// declares <paramref name="feature"/>, <paramref name="removeSql"/> when it does not, both replayable; and the
    /// <paramref name="policies"/> that must exist exactly when the feature is declared.
    /// </summary>
    /// <exception cref="ArgumentException">A name or a text is blank, or the feature is declared twice.</exception>
    /// <exception cref="NotSupportedException">The database runs on SQL Server, where row-level security waits for its case.</exception>
    public ModuleDatabase WhenFrameDeclares(
        FrameFeature feature, string applyName, string applySql, string removeName, string removeSql, params IEnumerable<RowPolicy> policies)
    {
        RefuseRowSecurityOnSqlServer();
        if (FrameScripts.Any(script => script.Feature == feature))
            throw new ArgumentException($"{Name} already declares a script for {feature}.", nameof(feature));
        var script = new FrameScript(
            Guard.Satisfies(feature, Enum.IsDefined, "Unknown frame feature."),
            Guard.NotWhiteSpace(applyName), Guard.NotWhiteSpace(applySql),
            Guard.NotWhiteSpace(removeName), Guard.NotWhiteSpace(removeSql),
            Guard.NotNull(policies).Select(policy => Guard.NotNull(policy)).ToList());
        return new(Module, Name, Engine, Schema, Scripts.ToImmutableList(), Extensions.ToImmutableList(),
            SecuredTables.ToImmutableList(), Policies.ToImmutableList(), FrameScripts.ToImmutableList().Add(script));
    }

    /// <summary>The module name and its schema.</summary>
    public override string ToString() => $"{Name} ({Engine}, schema {Schema})";

    internal static string CheckIdentifier(string value, string what)
        => Guard.Satisfies(value, NameRules.IsIdentifier, $"'{value}' is not a valid {what} name: {NameRules.IdentifierRule}.");

    internal static string CheckTable(string table)
    {
        var parts = Guard.NotWhiteSpace(table).Split('.');
        if (parts.Length != 2 || !NameRules.IsIdentifier(parts[0]) || !NameRules.IsIdentifier(parts[1]))
            throw new ArgumentException($"'{table}' is not a schema-qualified table name, schema.table, each an identifier: {NameRules.IdentifierRule}.", nameof(table));
        return table;
    }

    // SQL Server holds one enabled security policy per table, with one filter predicate per operation: rules that add
    // up, as the policies of PostgreSQL do, become one predicate function that the frame replaces. The declaration
    // for it waits for the first module that runs its row-level security on SQL Server.
    private void RefuseRowSecurityOnSqlServer()
    {
        if (Engine == DatabaseEngine.SqlServer)
        {
            throw new NotSupportedException(
                $"{Name}: row-level security and the scripts following the frame are not declared on SQL Server yet: "
                + "one enabled security policy and one filter predicate per table, which this declaration does not express.");
        }
    }

    private static ModuleDatabase Create<TModule>(DatabaseEngine engine, string schema) where TModule : IStellaegisModule
        => new(typeof(TModule), NameRules.ModuleNameOf<TModule>(), engine, CheckIdentifier(schema, "schema"), [], [], [], [], []);
}
