using System.Reflection;
using Dapper;

namespace Stellaegis.Primitives.Data;

/// <summary>
/// Dapper type map that resolves database column names using two strategies (in priority order):
/// <list type="number">
///   <item><see cref="ColumnMapAttribute"/> — explicit override (e.g., <c>[ColumnMap("operation_unique_id")]</c>)</item>
///   <item>Automatic PascalCase → snake_case conversion (e.g., <c>OperationType</c> → <c>operation_type</c>)</item>
/// </list>
/// Both settable properties and constructor parameters — positional records — are mapped. Register per row
/// type, in a static constructor of the store:
/// <code>
/// static MyStore() => SnakeCaseTypeMap&lt;MyRow&gt;.Register();
/// </code>
/// On a positional record, place the override on the property: <c>[property: ColumnMap("operation_unique_id")]</c>.
/// </summary>
public sealed class SnakeCaseTypeMap<T> : SqlMapper.ITypeMap
{
    private readonly Dictionary<string, PropertyInfo> _columnToProperty;
    private readonly SqlMapper.ITypeMap _fallback = new DefaultTypeMap(typeof(T));

    /// <summary>
    /// Initializes a new instance of <see cref="SnakeCaseTypeMap{T}"/>.
    /// </summary>
    public SnakeCaseTypeMap()
    {
        _columnToProperty = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = prop.GetCustomAttribute<ColumnMapAttribute>();
            var columnName = attr?.ColumnName ?? ToSnakeCase(prop.Name);
            _columnToProperty[columnName] = prop;
        }
    }

    /// <summary>Registers this type map with Dapper for <typeparamref name="T"/>, replacing any previous one.</summary>
    public static void Register() => SqlMapper.SetTypeMap(typeof(T), new SnakeCaseTypeMap<T>());

    /// <inheritdoc />
    /// <remarks>
    /// Columns are translated into property names before Dapper looks for the constructor, so a record whose
    /// parameters are named after its properties matches snake_case columns.
    /// </remarks>
    public ConstructorInfo? FindConstructor(string[] names, Type[] types) =>
        _fallback.FindConstructor(Array.ConvertAll(names, ToMemberName), types);

    /// <inheritdoc />
    public ConstructorInfo? FindExplicitConstructor() =>
        _fallback.FindExplicitConstructor();

    /// <inheritdoc />
    public SqlMapper.IMemberMap? GetConstructorParameter(ConstructorInfo constructor, string columnName) =>
        _fallback.GetConstructorParameter(constructor, ToMemberName(columnName));

    /// <inheritdoc />
    public SqlMapper.IMemberMap? GetMember(string columnName)
    {
        if (_columnToProperty.TryGetValue(columnName, out var prop))
            return new SimpleMemberMap(columnName, prop);

        return _fallback.GetMember(columnName);
    }

    private string ToMemberName(string columnName) =>
        _columnToProperty.TryGetValue(columnName, out var prop) ? prop.Name : columnName;

    internal static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // Pre-calculate capacity: worst case every char is uppercase → original + (n-1) underscores
        var builder = new System.Text.StringBuilder(name.Length + name.Length / 2);
        builder.Append(char.ToLowerInvariant(name[0]));

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                // Avoid double underscore for consecutive capitals (e.g., "MFA" → "mfa", not "m_f_a")
                var prevIsLower = char.IsLower(name[i - 1]);
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);

                if (prevIsLower || nextIsLower)
                    builder.Append('_');

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private sealed class SimpleMemberMap(string columnName, PropertyInfo property) : SqlMapper.IMemberMap
    {
        public string ColumnName => columnName;
        public Type MemberType => property.PropertyType;
        public PropertyInfo Property => property;
        public FieldInfo? Field => null;
        public ParameterInfo? Parameter => null;
    }
}
