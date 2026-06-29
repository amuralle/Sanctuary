namespace Sanctuary.Game.Resources.Definitions;

public sealed class CollectionNodeDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CollectionId { get; set; } = 100001;
    public int[] CollectionEntryIds { get; set; } = [];
    public int NameId { get; set; }
    public int SubTextNameId { get; set; }
    public bool Unknown33 { get; set; }
    public bool Unknown34 { get; set; }
    public int Unknown36 { get; set; }
    public int TemporaryAppearance { get; set; }
    public float NameColor { get; set; }
    public float NameScale { get; set; }
    public int NameplateImageId { get; set; }
    public int ModelId { get; set; }
    public int RareModelId { get; set; }
    public float Scale { get; set; } = 1.0f;
    public float? PositionW { get; set; }
    public int CompositeEffectId { get; set; }
    public int[] ExtraCompositeEffectIds { get; set; } = [];
    public int RareCompositeEffectId { get; set; }
    public int InteractRange { get; set; } = 12;
    public byte CursorId { get; set; } = 18;
    public bool HideNamePlate { get; set; }
    public int[] Rewards { get; set; } = [];
}
