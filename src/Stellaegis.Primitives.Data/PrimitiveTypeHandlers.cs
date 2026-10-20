using System.Data;
using Dapper;

namespace Stellaegis.Primitives.Data;

/// <summary>
/// Dapper type handlers for the primitive types of <c>Stellaegis.Primitives</c>: <see cref="Compartment"/> and
/// <see cref="Actor"/> read from and write to a string column. Register once, in a static constructor of the
/// store:
/// <code>
/// static DocumentStore() => PrimitiveTypeHandlers.Register();
/// </code>
/// </summary>
/// <remarks>
/// A parameter is sent exactly as Dapper sends a <see cref="string"/> — <see cref="DbType.String"/>, size
/// 4000 — so replacing a string by one of these types leaves the SQL unchanged. An actor is sent as its value,
/// who answers for the operation; a store that records who acted on its behalf, <see cref="Actor.Via"/>, adds a
/// column of its own and sends <c>actor.Via?.Value</c>. A <c>NULL</c> column reads as
/// <see langword="null"/>; a value that breaks the type's rule fails the read — Dapper wraps the
/// <see cref="FormatException"/> in a <see cref="DataException"/> naming the column — since it means
/// corrupted data or a missing database constraint.
/// <para>
/// Dapper expands a list parameter (<c>in @compartments</c>) without type handlers, and the provider rejects
/// the elements. Pass a list as its values: <c>new { compartments = compartments.Select(c =&gt; c.Value) }</c>.
/// </para>
/// </remarks>
public static class PrimitiveTypeHandlers
{
    /// <summary>Registers the handlers with Dapper. Calling it again replaces them with equivalent ones.</summary>
    public static void Register()
    {
        SqlMapper.AddTypeHandler(new CompartmentTypeHandler());
        SqlMapper.AddTypeHandler(new ActorTypeHandler());
    }

    private static void SetString(IDbDataParameter parameter, string? value)
    {
        parameter.DbType = DbType.String;
        parameter.Size = DbString.DefaultLength;
        parameter.Value = value ?? (object)DBNull.Value;
    }

    internal sealed class CompartmentTypeHandler : SqlMapper.TypeHandler<Compartment>
    {
        public override void SetValue(IDbDataParameter parameter, Compartment? value)
            => SetString(parameter, value?.Value);

        public override Compartment Parse(object value) => Compartment.Parse((string)value);
    }

    internal sealed class ActorTypeHandler : SqlMapper.TypeHandler<Actor>
    {
        public override void SetValue(IDbDataParameter parameter, Actor? value)
            => SetString(parameter, value?.Value);

        public override Actor Parse(object value) => Actor.Parse((string)value);
    }
}
