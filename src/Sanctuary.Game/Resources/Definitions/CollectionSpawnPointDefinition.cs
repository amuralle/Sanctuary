namespace Sanctuary.Game.Resources.Definitions;

public sealed class CollectionSpawnPointDefinition
{
    public string NodeKey { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int RespawnSeconds { get; set; }
}
