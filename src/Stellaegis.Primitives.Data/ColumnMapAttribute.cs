namespace Stellaegis.Primitives.Data;

/// <summary>
/// Maps a property to a specific database column name, overriding the default snake_case convention.
/// Use when the C# property name doesn't follow the standard PascalCase → snake_case conversion
/// (e.g., <c>UniqueId</c> mapping to <c>operation_unique_id</c> instead of <c>unique_id</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class ColumnMapAttribute(string columnName) : Attribute
{
    /// <summary>
    /// The exact database column name.
    /// </summary>
    public string ColumnName { get; } = columnName;
}
