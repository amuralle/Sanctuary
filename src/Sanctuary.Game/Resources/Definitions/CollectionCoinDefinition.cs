using System;
using System.Numerics;
using System.Text.Json.Serialization;

namespace Sanctuary.Game.Resources.Definitions;

public sealed class CollectionCoinDefinition
{
    public int NpcDefinitionId { get; set; }
    public int CollectionId { get; set; }
    public int EntryId { get; set; }
    public int ZoneDefinitionId { get; set; }
    public float[]? Position { get; set; }
    public float Heading { get; set; }

    [JsonIgnore]
    public bool HasExplicitSpawn => ZoneDefinitionId > 0 && Position is { Length: 3 };

    [JsonIgnore]
    public Vector4 SpawnPosition => Position is { Length: 3 }
        ? new Vector4(Position[0], Position[1], Position[2], 1f)
        : throw new InvalidOperationException("The collection coin does not define a spawn position.");

    [JsonIgnore]
    public Quaternion SpawnRotation => new(MathF.Sin(Heading), 0f, MathF.Cos(Heading), 0f);
}
