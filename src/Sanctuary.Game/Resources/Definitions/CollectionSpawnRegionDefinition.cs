namespace Sanctuary.Game.Resources.Definitions;

public sealed class CollectionSpawnRegionDefinition
{
    public string Key { get; set; } = string.Empty;
    public int ZoneId { get; set; } = 1;
    public float CenterX { get; set; }
    public float CenterY { get; set; }
    public float CenterZ { get; set; }
    public float Radius { get; set; } = 20.0f;
    public int Count { get; set; } = 8;
    public int RespawnSeconds { get; set; } = 45;
    public bool ShuffleOnRespawn { get; set; } = true;
    public int Seed { get; set; }
    public string[] NodeKeys { get; set; } = [];
    public CollectionSpawnPointDefinition[] Points { get; set; } = [];
}
