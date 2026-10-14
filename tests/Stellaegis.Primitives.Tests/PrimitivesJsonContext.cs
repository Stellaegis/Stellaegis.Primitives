using System.Text.Json.Serialization;

namespace Stellaegis.Primitives.Tests;

// Source-generated serialization runs in the consumer's assembly: this context checks that it
// reaches the internal converters through the [JsonConverter] attribute.
public sealed record PrimitivesEnvelope(Compartment Compartment, Actor Actor);

[JsonSerializable(typeof(PrimitivesEnvelope))]
internal sealed partial class PrimitivesJsonContext : JsonSerializerContext;
