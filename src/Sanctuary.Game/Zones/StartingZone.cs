using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using Sanctuary.Core.Extensions;
using Sanctuary.Core.IO;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Zones;

public sealed class StartingZone : BaseZone
{
    private readonly IZoneManager _zoneManager;
    private readonly IResourceManager _resourceManager;
    private readonly StartingZoneDefinition _zoneDefinition;
    private const int MockCollectionRespawnSeconds = 30;
    private bool _mockCollectionNodesSpawned;
    private readonly Random _collectionRandom = new();
    private readonly List<Npc> _collectionSpawnRegionNodes = [];
    private readonly Dictionary<string, HashSet<int>> _activeCollectionPointIndexesByRegion = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Npc> _collectionNodeShowroom = [];
    private static readonly MockCollectionNodeDefinition[] MockCollectionNodeDefinitions =
    [
        new()
        {
            Key = "flowers",
            Name = "Mock Flowers",
            ModelId = 248,
            Offset = new Vector4(8, -2.5f, 8, 0),
            Rewards = [3106, 3107, 3108, 3961, 3850]
        },
        new()
        {
            Key = "mushrooms",
            Name = "Mock Mushrooms",
            ModelId = 249,
            Offset = new Vector4(-8, -2.5f, 10, 0),
            Rewards = [3274, 3275, 3276, 3289, 3858]
        },
        new()
        {
            Key = "shells",
            Name = "Mock Shells",
            ModelId = 317,
            Offset = new Vector4(12, -2.5f, -8, 0),
            Rewards = [3250, 3251, 3256, 3257, 3866]
        },
        new()
        {
            Key = "leaves",
            Name = "Mock Leaves",
            ModelId = 326,
            Offset = new Vector4(-12, -2.5f, -8, 0),
            Rewards = [3226, 3229, 3245, 3249, 23295]
        }
    ];
    private static readonly CollectionNodeShowroomDefinition[] CollectionNodeShowroomDefinitions =
    [
        new("flowers", 248, "base"),
        new("mushrooms", 249, "base"),
        new("shells", 317, "base"),
        new("leaves", 326, "base"),
        new("junkpile", 333, "base"),
        new("rare leaves", 650, "rare", true),
        new("rare flowers", 670, "rare", true),
        new("rare junkpile", 671, "rare", true),
        new("rare shells", 672, "rare", true),
        new("rare mushrooms", 674, "rare", true),
        new("crate", 691, "base"),
        new("archer", 696, "jobs"),
        new("rare archer", 697, "jobs", true),
        new("blacksmith", 698, "jobs"),
        new("rare blacksmith", 699, "jobs", true),
        new("chef", 700, "jobs"),
        new("rare chef", 701, "jobs", true),
        new("demo derby", 702, "jobs"),
        new("rare demo derby", 703, "jobs", true),
        new("fighter", 704, "jobs"),
        new("rare fighter", 705, "jobs", true),
        new("medic", 706, "jobs"),
        new("rare medic", 707, "jobs", true),
        new("miner", 708, "jobs"),
        new("rare miner", 709, "jobs", true),
        new("ninja", 710, "jobs"),
        new("rare ninja", 711, "jobs", true),
        new("postman", 712, "jobs"),
        new("rare postman", 713, "jobs", true),
        new("racecar", 714, "jobs"),
        new("rare racecar", 715, "jobs", true),
        new("warrior", 716, "jobs"),
        new("rare warrior", 717, "jobs", true),
        new("wizard", 718, "jobs"),
        new("rare wizard", 719, "jobs", true),
        new("rare spring", 722, "misc", true),
        new("springs", 723, "misc"),
        new("fossils", 724, "misc"),
        new("rare fossils", 845, "misc", true),
        new("soccer", 1625, "misc"),
        new("rare soccer", 1626, "misc", true),
        new("invisible cube", 271, "water"),
        new("cauldron bubble", 432, "water"),
        new("rare spring collection", 722, "water", true),
        new("spring collection", 723, "water"),
        new("spring pickup anim", 851, "water"),
        new("fishing bobber old", 1063, "water"),
        new("stillwater reed", 1594, "water"),
        new("fishing chest", 1624, "water"),
        new("bobber 01", 1641, "water"),
        new("bobber 02", 1642, "water"),
        new("bobber 03", 1643, "water"),
        new("bobber 04", 1644, "water"),
        new("bobber 05", 1645, "water"),
        new("fishing lure", 1673, "water"),
        new("fishing node 01", 1684, "water"),
        new("fishing node 02", 1685, "water"),
        new("reticle inner", 1690, "water"),
        new("reticle outer", 1691, "water"),
        new("reticle cast inner", 1697, "water"),
        new("reticle cast outer", 1698, "water"),
        new("reticle no cast inner", 1699, "water"),
        new("reticle no cast outer", 1700, "water"),
        new("fishing area bubbles", 1704, "water"),
        new("fishing run school", 1727, "water"),
        new("invisible particle launcher", 4071, "water"),
        new("invisible particle launcher anim", 4072, "water"),
        new("invisible particle launcher box", 4073, "water"),
        new("treasure chest", 151, "loot"),
        new("loot chest", 720, "loot"),
        new("big loot bag", 807, "loot"),
        new("small loot bag", 808, "loot"),
        new("loot coins", 841, "loot"),
        new("brown bag", 844, "loot"),
        new("mystery chest 1", 1511, "loot"),
        new("mystery chest 2", 1512, "loot"),
        new("mystery chest 3", 876, "loot"),
        new("bronze pirate chest", 1662, "loot"),
        new("silver pirate chest", 1664, "loot"),
        new("gold pirate chest", 1663, "loot"),
        new("fishing treasure chest", 1624, "loot"),
        new("mining node common", 259, "loot"),
        new("mining node rare", 260, "loot", true),
        new("farming node common", 274, "loot"),
        new("farming node rich", 275, "loot", true),
        new("fishing node 1", 1684, "loot"),
        new("fishing node 2", 1685, "loot"),
        new("dirt pile", 693, "loot"),
        new("trash pile", 862, "loot"),
        new("scrap pile", 1576, "loot"),
        new("soda crate", 1629, "loot"),
        new("present 1", 17, "loot"),
        new("present 2", 18, "loot"),
        new("treasure chest 01", 151, "chests"),
        new("loot chest 01", 720, "chests"),
        new("mystery chest 03", 876, "chests"),
        new("mystery chest 01", 1511, "chests"),
        new("mystery chest 02", 1512, "chests"),
        new("fishing treasure chest", 1624, "chests"),
        new("pirate bronze chest", 1662, "chests"),
        new("pirate gold chest", 1663, "chests"),
        new("pirate silver chest", 1664, "chests"),
        new("pirate treasure chest", 1786, "chests"),
        new("housing chest 01", 1832, "chests"),
        new("treasure chest 02", 1905, "chests"),
        new("pirate dock treasure chest", 1923, "chests"),
        new("housing chest 02", 2102, "chests"),
        new("mystery chest 04", 4135, "chests"),
        new("mystery chest 05", 4460, "chests"),
        new("treasure chest tintable", 4607, "chests"),
    ];

    public StartingZone(StartingZoneDefinition zoneDefinition, IServiceProvider serviceProvider)
        : base(zoneDefinition, serviceProvider)
    {
        _zoneDefinition = zoneDefinition;

        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
    }

    public override void OnClientFinishedLoading(Player player)
    {
        if (!_mockCollectionNodesSpawned)
        {
            SpawnConfiguredCollectionRegions();
            _mockCollectionNodesSpawned = true;
        }
    }

    private void SpawnConfiguredCollectionRegions()
    {
        foreach (var region in _resourceManager.CollectionSpawnRegions.Values.Where(x => x.ZoneId == Id))
            SpawnCollectionSpawnRegion(region);
    }

    private void SpawnMockCollectionNodes(Vector4 center)
    {
        foreach (var node in MockCollectionNodeDefinitions)
            SpawnMockCollectionNode(node, center);
    }

    public void CollectMockCollectionNode(Npc npc)
    {
        var nodeKey = npc.MockCollectionNodeKey;
        var center = npc.MockCollectionSpawnCenter;
        var radius = npc.MockCollectionSpawnRadius;
        var shuffleOnRespawn = npc.MockCollectionShuffleOnRespawn;
        var respawnSeconds = npc.MockCollectionRespawnSeconds;
        var regionKey = npc.MockCollectionRegionKey;
        var pointIndex = npc.MockCollectionPointIndex;
        var wasRegionNode = _collectionSpawnRegionNodes.Remove(npc);

        if (!string.IsNullOrWhiteSpace(regionKey) &&
            pointIndex >= 0 &&
            _activeCollectionPointIndexesByRegion.TryGetValue(regionKey, out var activePointIndexes))
        {
            activePointIndexes.Remove(pointIndex);
        }

        npc.Dispose();

        if (!string.IsNullOrWhiteSpace(nodeKey) && respawnSeconds > 0)
        {
            if (!string.IsNullOrWhiteSpace(regionKey) && pointIndex >= 0)
                _ = RespawnCollectionPointAsync(regionKey, respawnSeconds);
            else
                _ = RespawnMockCollectionNodeAsync(nodeKey, center, radius, respawnSeconds, shuffleOnRespawn, wasRegionNode);
        }
    }

    private async Task RespawnMockCollectionNodeAsync(string nodeKey, Vector4 center, float radius, int respawnSeconds, bool shuffleOnRespawn, bool trackAsRegionNode)
    {
        await Task.Delay(TimeSpan.FromSeconds(respawnSeconds));

        if (!_resourceManager.CollectionNodes.TryGetValue(nodeKey, out var node))
            return;

        var position = shuffleOnRespawn && radius > 0
            ? GetRandomPositionInRegion(center, radius)
            : center;

        SpawnCollectionNode(node, nodeKey, null, center, radius, respawnSeconds, shuffleOnRespawn, position, trackAsRegionNode);
    }

    private void SpawnMockCollectionNode(MockCollectionNodeDefinition node, Vector4 center)
    {
        if (!TryCreateNpc(out var npc))
            return;

        npc.Name = node.Name;
        npc.ModelId = node.ModelId;
        npc.Scale = 1.5f;
        npc.Visible = true;
        npc.IsInteractable = true;
        npc.InteractRange = 12;
        npc.CursorId = 18;
        npc.Disposition = 1;
        npc.MockCollectionNodeKey = node.Key;
        npc.MockCollectionId = 100001;
        npc.MockCollectionSpawnCenter = center;
        npc.MockCollectionRespawnSeconds = MockCollectionRespawnSeconds;
        npc.MockCollectionRewardItemDefinitionIds.AddRange(node.Rewards);
        npc.UpdatePosition(center + node.Offset, SpawnRotation);
    }

    private int SpawnCollectionSpawnRegion(CollectionSpawnRegionDefinition region)
    {
        if (region.Points.Length > 0)
            return SpawnCollectionSpawnRegionPoints(region);

        if (region.NodeKeys.Length == 0 || region.Count <= 0)
            return 0;

        var center = new Vector4(region.CenterX, region.CenterY, region.CenterZ, 0);
        var spawned = 0;
        var random = region.Seed == 0
            ? _collectionRandom
            : new Random(region.Seed);

        for (var i = 0; i < region.Count; i++)
        {
            var nodeKey = region.NodeKeys[i % region.NodeKeys.Length];

            if (!_resourceManager.CollectionNodes.TryGetValue(nodeKey, out var node))
                continue;

            var position = GetRandomPositionInRegion(center, region.Radius, random);

            if (SpawnCollectionNode(node, nodeKey, region.Key, center, region.Radius, region.RespawnSeconds, region.ShuffleOnRespawn, position, true))
                spawned++;
        }

        return spawned;
    }

    private int SpawnCollectionSpawnRegionPoints(CollectionSpawnRegionDefinition region)
    {
        ClearActiveCollectionPointIndexes(region.Key);

        var activeCount = region.Count > 0
            ? Math.Min(region.Count, region.Points.Length)
            : region.Points.Length;
        var spawned = 0;
        var pointIndexes = Enumerable.Range(0, region.Points.Length)
            .OrderBy(_ => _collectionRandom.Next())
            .Take(activeCount);

        foreach (var pointIndex in pointIndexes)
        {
            if (TrySpawnCollectionRegionPoint(region, pointIndex))
                spawned++;
        }

        return spawned;
    }

    private async Task RespawnCollectionPointAsync(string regionKey, int respawnSeconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(respawnSeconds));

        if (!_resourceManager.CollectionSpawnRegions.TryGetValue(regionKey, out var region) || region.Points.Length == 0)
            return;

        var activePointIndexes = GetActiveCollectionPointIndexes(region.Key);
        var inactivePointIndexes = Enumerable.Range(0, region.Points.Length)
            .Where(x => !activePointIndexes.Contains(x))
            .OrderBy(_ => _collectionRandom.Next())
            .ToArray();

        if (inactivePointIndexes.Length == 0)
            return;

        TrySpawnCollectionRegionPoint(region, inactivePointIndexes[0]);
    }

    private bool TrySpawnCollectionRegionPoint(CollectionSpawnRegionDefinition region, int pointIndex)
    {
        if ((uint)pointIndex >= (uint)region.Points.Length)
            return false;

        var activePointIndexes = GetActiveCollectionPointIndexes(region.Key);

        if (activePointIndexes.Contains(pointIndex))
            return false;

        var point = region.Points[pointIndex];
        var nodeKey = string.IsNullOrWhiteSpace(point.NodeKey)
            ? region.NodeKeys.FirstOrDefault()
            : point.NodeKey;

        if (string.IsNullOrWhiteSpace(nodeKey) || !_resourceManager.CollectionNodes.TryGetValue(nodeKey, out var node))
            return false;

        var center = new Vector4(region.CenterX, region.CenterY, region.CenterZ, 0);
        var respawnSeconds = point.RespawnSeconds > 0
            ? point.RespawnSeconds
            : region.RespawnSeconds;
        var position = new Vector4(point.X, point.Y, point.Z, 0);

        if (!SpawnCollectionNode(node, nodeKey, region.Key, center, 0, respawnSeconds, false, position, true, pointIndex))
            return false;

        activePointIndexes.Add(pointIndex);
        return true;
    }

    public int SpawnAdHocCollectionRegion(Vector4 center, string nodeKey, int count, float radius, int respawnSeconds)
    {
        if (!_resourceManager.CollectionNodes.TryGetValue(nodeKey, out var node))
            return 0;

        var spawned = 0;

        for (var i = 0; i < count; i++)
        {
            var position = GetRandomPositionInRegion(center, radius);

            if (SpawnCollectionNode(node, nodeKey, "dev-ad-hoc", center, radius, respawnSeconds, true, position, true))
                spawned++;
        }

        return spawned;
    }

    public bool SpawnCollectionHardPoint(Vector4 position, string nodeKey, int respawnSeconds)
    {
        if (!_resourceManager.CollectionNodes.TryGetValue(nodeKey, out var node))
            return false;

        ClearCollectionNodeShowroom();
        return SpawnCollectionNode(node, nodeKey, "dev-hard-points", position, 0, respawnSeconds, false, position, false, -1);
    }

    public int ClearCollectionSpawnRegionNodes()
    {
        var removed = _collectionSpawnRegionNodes.Count + _collectionNodeShowroom.Count;

        foreach (var npc in _collectionSpawnRegionNodes.ToArray())
            npc.Dispose();

        foreach (var npc in _collectionNodeShowroom.ToArray())
            npc.Dispose();

        _collectionSpawnRegionNodes.Clear();
        _collectionNodeShowroom.Clear();
        _activeCollectionPointIndexesByRegion.Clear();

        return removed;
    }

    public int ReloadCollectionSpawnRegionNodes()
    {
        ClearCollectionSpawnRegionNodes();
        var before = _collectionSpawnRegionNodes.Count;
        SpawnConfiguredCollectionRegions();
        return _collectionSpawnRegionNodes.Count - before;
    }

    private bool SpawnCollectionNode(
        CollectionNodeDefinition node,
        string nodeKey,
        string? regionKey,
        Vector4 center,
        float radius,
        int respawnSeconds,
        bool shuffleOnRespawn,
        Vector4 position,
        bool trackAsRegionNode,
        int pointIndex = -1)
    {
        if (!TryCreateCollectionNodeNpc(node, position, true, true, node.InteractRange, (byte)node.CursorId, "stamp", out var npc))
            return false;

        if (regionKey == "dev-hard-points")
            _collectionNodeShowroom.Add(npc);

        _ = AttachCollectionNodeMetadataAsync(
            npc,
            nodeKey,
            regionKey,
            pointIndex,
            center,
            radius,
            respawnSeconds,
            shuffleOnRespawn,
            node.CollectionId,
            node.CollectionEntryIds,
            node.Rewards);

        if (trackAsRegionNode)
            _collectionSpawnRegionNodes.Add(npc);

        return true;
    }

    private static async Task AttachCollectionNodeMetadataAsync(
        Npc npc,
        string nodeKey,
        string? regionKey,
        int pointIndex,
        Vector4 center,
        float radius,
        int respawnSeconds,
        bool shuffleOnRespawn,
        int collectionId,
        IEnumerable<int> collectionEntryIds,
        IEnumerable<int> rewards)
    {
        await Task.Delay(500);

        npc.MockCollectionNodeKey = nodeKey;
        npc.MockCollectionRegionKey = regionKey;
        npc.MockCollectionPointIndex = pointIndex;
        npc.MockCollectionId = collectionId;
        npc.MockCollectionSpawnCenter = center;
        npc.MockCollectionSpawnRadius = radius;
        npc.MockCollectionRespawnSeconds = respawnSeconds;
        npc.MockCollectionShuffleOnRespawn = shuffleOnRespawn;
        npc.MockCollectionEntryIds.AddRange(collectionEntryIds);
        npc.MockCollectionRewardItemDefinitionIds.AddRange(rewards);
    }

    private HashSet<int> GetActiveCollectionPointIndexes(string regionKey)
    {
        if (!_activeCollectionPointIndexesByRegion.TryGetValue(regionKey, out var indexes))
        {
            indexes = [];
            _activeCollectionPointIndexesByRegion[regionKey] = indexes;
        }

        return indexes;
    }

    private void ClearActiveCollectionPointIndexes(string regionKey)
    {
        GetActiveCollectionPointIndexes(regionKey).Clear();
    }

    private Vector4 GetRandomPositionInRegion(Vector4 center, float radius)
    {
        return GetRandomPositionInRegion(center, radius, _collectionRandom);
    }

    private static Vector4 GetRandomPositionInRegion(Vector4 center, float radius, Random random)
    {
        var angle = random.NextSingle() * MathF.Tau;
        var distance = MathF.Sqrt(random.NextSingle()) * radius;

        return center + new Vector4(
            MathF.Cos(angle) * distance,
            0.35f,
            MathF.Sin(angle) * distance,
            0);
    }

    public int SpawnCollectionNodeShowroom(Vector4 center, string batch, string layout, bool useEffects)
    {
        var definitions = CollectionNodeShowroomDefinitions
            .Where(x =>
                batch == "all"
                || x.Group == batch
                || batch == "rare" && x.IsRare
                || batch == "fossils" && x.Name.Contains("fossil", StringComparison.OrdinalIgnoreCase)
                || batch == "springs" && x.Name.Contains("spring", StringComparison.OrdinalIgnoreCase)
                || batch == "soccer" && x.Name.Contains("soccer", StringComparison.OrdinalIgnoreCase))
            .Select(x => (x.ModelId, x.Name, x.IsRare))
            .ToArray();

        return SpawnModelShowroom(center, definitions, layout, useEffects);
    }

    public int SpawnModelShowroom(
        Vector4 center,
        IEnumerable<(int ModelId, string Name, bool IsRare)> definitions,
        string layout,
        bool useEffects)
    {
        ClearCollectionNodeShowroom();

        var normalizedLayout = string.IsNullOrWhiteSpace(layout)
            ? "grid"
            : layout.ToLowerInvariant();
        var entries = definitions
            .Where(x => x.ModelId > 0)
            .Take(100)
            .ToArray();

        if (normalizedLayout is not ("grid" or "scatter"))
            normalizedLayout = "grid";

        for (var i = 0; i < entries.Length; i++)
        {
            var definition = entries[i];
            var offset = normalizedLayout == "scatter"
                ? GetShowroomScatterOffset(i)
                : GetShowroomGridOffset(i);

            if (!TryCreateNpc(out var npc))
                continue;

            npc.Name = $"Model {definition.ModelId} {definition.Name}";
            npc.ModelId = definition.ModelId;
            npc.Scale = 1.0f;
            npc.Visible = true;
            npc.IsInteractable = false;
            npc.InteractRange = 0;
            npc.CursorId = 0;
            npc.Disposition = 1;
            npc.CompositeEffectId = useEffects
                ? definition.IsRare ? 5740 : 5742
                : 0;
            npc.UpdatePosition(center + offset, SpawnRotation);

            _collectionNodeShowroom.Add(npc);
        }

        return _collectionNodeShowroom.Count;
    }

    public int SpawnCollectionNodeShowroomModel(Vector4 center, int modelId, bool useEffects)
    {
        return SpawnModelShowroom(center, [(modelId, $"model {modelId}", true)], "grid", useEffects);
    }

    public int SpawnCollectionNodeVisualProbe(Vector4 center, string nodeKey, bool useEffects)
    {
        return SpawnCollectionNodeVisualProbeAt(center + new Vector4(0, 0.35f, 8.0f, 0), nodeKey, useEffects);
    }

    public int SpawnCollectionNodeVisualProbeAt(Vector4 position, string nodeKey, bool useEffects)
    {
        return SpawnCollectionNodeVisualProbeAt(position, nodeKey, useEffects, false, 0, 0);
    }

    public int SpawnCollectionNodeVisualProbeAt(
        Vector4 position,
        string nodeKey,
        bool useEffects,
        bool isInteractable,
        int interactRange,
        byte cursorId)
    {
        ClearCollectionNodeShowroom();

        if (!_resourceManager.CollectionNodes.TryGetValue(nodeKey, out var node))
            return 0;

        if (!TryCreateCollectionNodeNpc(node, position, useEffects, isInteractable, interactRange, cursorId, "visual-probe", out var npc))
            return 0;

        _collectionNodeShowroom.Add(npc);

        return _collectionNodeShowroom.Count;
    }

    private bool TryCreateCollectionNodeNpc(
        CollectionNodeDefinition node,
        Vector4 position,
        bool useEffects,
        bool isInteractable,
        int interactRange,
        byte cursorId,
        string debugSource,
        out Npc npc)
    {
        if (!TryCreateNpc(out npc))
            return false;

        npc.NameId = node.NameId;
        npc.Name = node.NameId == 0 ? node.Name : null;
        npc.SubTextNameId = node.SubTextNameId;
        npc.Unknown33 = node.Unknown33;
        npc.Unknown34 = node.Unknown34;
        npc.Unknown36 = node.Unknown36;
        npc.TemporaryAppearance = node.TemporaryAppearance;
        npc.NameColor = node.NameColor;
        npc.NameScale = node.NameScale;
        npc.NameplateImageId = node.NameplateImageId;
        npc.ModelId = node.ModelId;
        npc.Scale = node.Scale;
        npc.Visible = true;
        npc.HideNamePlate = node.HideNamePlate;
        npc.IsInteractable = isInteractable;
        npc.InteractRange = interactRange;
        npc.CursorId = cursorId;
        npc.Disposition = 1;
        npc.CompositeEffectId = useEffects ? node.CompositeEffectId : 0;
        position.W = node.PositionW ?? 1.0f;
        npc.UpdatePosition(position, SpawnRotation);
        npc.MockCollectionCompanionNodes.AddRange(CreateCollectionNodeEffectAnchors(node, position, useEffects, debugSource));
        LogCollectionNodeAddNpcPacket(debugSource, npc);

        return true;
    }

    private List<Npc> CreateCollectionNodeEffectAnchors(
        CollectionNodeDefinition node,
        Vector4 position,
        bool useEffects,
        string debugSource)
    {
        var anchors = new List<Npc>();

        if (!useEffects || node.ExtraCompositeEffectIds.Length == 0)
            return anchors;

        foreach (var effectId in node.ExtraCompositeEffectIds.Where(x => x > 0))
        {
            if (!TryCreateNpc(out var anchor))
                continue;

            anchor.NameId = node.NameId;
            anchor.Name = node.NameId == 0 ? node.Name : null;
            anchor.NameplateImageId = node.NameplateImageId;
            anchor.ModelId = node.ModelId;
            anchor.Scale = node.Scale;
            anchor.Visible = true;
            anchor.HideNamePlate = true;
            anchor.IsInteractable = false;
            anchor.InteractRange = 0;
            anchor.CursorId = 0;
            anchor.Disposition = 1;
            anchor.CompositeEffectId = effectId;
            anchor.UpdatePosition(position, SpawnRotation);
            LogCollectionNodeAddNpcPacket($"{debugSource}-effect-anchor", anchor);

            anchors.Add(anchor);
        }

        return anchors;
    }

    private static void LogCollectionNodeAddNpcPacket(string source, Npc npc)
    {
        try
        {
            var packet = npc.GetAddNpcPacket();
            var bytes = packet.Serialize();
            var logPath = Path.Combine(AppContext.BaseDirectory, "collection-addnpc-debug.log");
            var line =
                $"{DateTimeOffset.UtcNow:O}\t{source}\tGuid={npc.Guid}\tModel={npc.ModelId}\tNameId={npc.NameId}\tName={npc.Name}\tUnknown33={npc.Unknown33}\tUnknown34={npc.Unknown34}\tSubText={npc.SubTextNameId}\tUnknown36={npc.Unknown36}\tTempAppearance={npc.TemporaryAppearance}\tNameColor={npc.NameColor}\tNameScale={npc.NameScale}\tNameplateImage={npc.NameplateImageId}\tPos=({npc.Position.X},{npc.Position.Y},{npc.Position.Z})\tScale={npc.Scale}\tInteract={npc.IsInteractable}\tRange={npc.InteractRange}\tCursor={npc.CursorId}\tEffect={npc.CompositeEffectId}\tBytes={bytes.Length}\tHex={Convert.ToHexString(bytes)}{Environment.NewLine}";

            File.AppendAllText(logPath, line);
        }
        catch
        {
            // Debug logging must never affect gameplay packet flow.
        }
    }

    public int ClearCollectionNodeShowroom()
    {
        var removed = _collectionNodeShowroom.Count;

        foreach (var npc in _collectionNodeShowroom.ToArray())
            npc.Dispose();

        _collectionNodeShowroom.Clear();

        return removed;
    }

    private static Vector4 GetShowroomGridOffset(int index)
    {
        const int columns = 7;
        const float spacing = 4.5f;
        const float forwardOffset = 10.0f;
        const float lift = 0.35f;

        var row = index / columns;
        var column = index % columns;

        return new Vector4(
            (column - (columns - 1) / 2.0f) * spacing,
            lift,
            forwardOffset + row * spacing,
            0);
    }

    private static Vector4 GetShowroomScatterOffset(int index)
    {
        const float goldenAngle = 2.3999631f;
        const float lift = 0.35f;
        const float spacing = 2.6f;

        var radius = 5.0f + MathF.Sqrt(index) * spacing;
        var angle = index * goldenAngle;

        return new Vector4(
            MathF.Cos(angle) * radius,
            lift,
            MathF.Sin(angle) * radius,
            0);
    }

    private sealed class MockCollectionNodeDefinition
    {
        public string Key { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int ModelId { get; init; }
        public Vector4 Offset { get; init; }
        public int[] Rewards { get; init; } = [];
    }

    private sealed class CollectionNodeShowroomDefinition(string name, int modelId, string group, bool isRare = false)
    {
        public string Name { get; } = name;
        public int ModelId { get; } = modelId;
        public string Group { get; } = group;
        public bool IsRare { get; } = isRare;
    }

    #region Client Is Ready

    public override void OnClientIsReady(Player player)
    {
        SendQuickChatData(player);

        SendPointOfInterests(player);

        SendUpdateStat(player);

        var clientUpdatePacketHitpoints = new ClientUpdatePacketHitpoints
        {
            CurrentHitpoints = 2500,
            MaxHitpoints = 2500
        };

        player.SendTunneled(clientUpdatePacketHitpoints);

        var clientUpdatePacketMana = new ClientUpdatePacketMana
        {
            CurrentMana = 100,
            MaxMana = 100
        };

        player.SendTunneled(clientUpdatePacketMana);

        SendReferenceData(player);

        SendCoinStoreItemList(player);

        SendAdventurersJournalInfo(player);

        SendWelcomeInfo(player);

        SendPlayerCustomizations(player);

        SendMembershipSubscriptionInfo(player);

        SendInGamePurchase(player);

        var packetZoneDoneSendingInitialData = new PacketZoneDoneSendingInitialData();

        player.SendTunneled(packetZoneDoneSendingInitialData);

        var clientUpdatePacketDoneSendingPreloadCharacters = new ClientUpdatePacketDoneSendingPreloadCharacters();

        player.SendTunneled(clientUpdatePacketDoneSendingPreloadCharacters);

        SendFriendList(player);
        SendIgnoreList(player);

        UpdateFriendStatus(player);
    }

    private void SendQuickChatData(Player player)
    {
        var quickChatSendDataPacket = new QuickChatSendDataPacket();

        quickChatSendDataPacket.QuickChats = _resourceManager.QuickChats.ToDictionary();

        player.SendTunneled(quickChatSendDataPacket);
    }

    private void SendPointOfInterests(Player player)
    {
        var packetPointOfInterestDefinitionReply = new PacketPointOfInterestDefinitionReply();
        using var writer = new PacketWriter();

        foreach (var pointOfInterest in _resourceManager.PointOfInterests.Values)
        {
            writer.Write(true);

            pointOfInterest.Serialize(writer);
        }

        writer.Write(false);

        packetPointOfInterestDefinitionReply.Payload = writer.Buffer;

        player.SendTunneled(packetPointOfInterestDefinitionReply);
    }

    private void SendUpdateStat(Player player)
    {
        var clientUpdatePacketUpdateStat = new ClientUpdatePacketUpdateStat();

        clientUpdatePacketUpdateStat.Guid = player.Guid;

        // TODO
        clientUpdatePacketUpdateStat.Stats.AddRange(
        [
            new CharacterStat(CharacterStatId.MaxHealth, 2500),
            new CharacterStat(CharacterStatId.MaxMovementSpeed, 8f),
            new CharacterStat(CharacterStatId.WeaponRange, 5f),
            new CharacterStat(CharacterStatId.HitPointRegen, 25),
            new CharacterStat(CharacterStatId.MaxMana, 100),
            new CharacterStat(CharacterStatId.ManaRegen, 4),
            new CharacterStat(CharacterStatId.MeleeChanceToHit, 100),
            new CharacterStat(CharacterStatId.MeleeWeaponDamageMultiplier, 1f),
            new CharacterStat(CharacterStatId.MeleeHandToHandDamage, 1),
            new CharacterStat(CharacterStatId.EquippedMeleeWeaponDamage, 1),
            new CharacterStat(CharacterStatId.MeleeAttackIntervalMs, 2000),
            new CharacterStat(CharacterStatId.DamageMultiplier, 1f),
            new CharacterStat(CharacterStatId.HealingMultiplier, 1f),
            new CharacterStat(CharacterStatId.AbilityCriticalHitMultiplier, 1f),
            new CharacterStat(CharacterStatId.HeadInflationPercent, 100),
            new CharacterStat(CharacterStatId.RangeMultiplier, 1f),
            new CharacterStat(CharacterStatId.FactoryProductionModifier, 1f),
            new CharacterStat(CharacterStatId.FactoryYieldModifier, 1f),
            new CharacterStat(CharacterStatId.InCombatHitPointRegen, 6),
            new CharacterStat(CharacterStatId.InCombatManaRegen, 4)
        ]);

        player.SendTunneled(clientUpdatePacketUpdateStat);
    }

    private void SendReferenceData(Player player)
    {
        var referenceDataPacketItemClassDefinitions = new ReferenceDataPacketItemClassDefinitions();

        referenceDataPacketItemClassDefinitions.ItemClasses = _resourceManager.ItemClasses.ToDictionary();

        player.SendTunneled(referenceDataPacketItemClassDefinitions);

        var referenceDataPacketItemCategoryDefinitions = new ReferenceDataPacketItemCategoryDefinitions();

        referenceDataPacketItemCategoryDefinitions.ItemCategories = _resourceManager.ItemCategories.ToDictionary();
        referenceDataPacketItemCategoryDefinitions.ItemCategoryGroups = _resourceManager.ItemCategoryGroups.ToDictionary();

        player.SendTunneled(referenceDataPacketItemCategoryDefinitions);

        var referenceDataPacketClientProfileData = new ReferenceDataPacketClientProfileData();

        referenceDataPacketClientProfileData.Profiles = _resourceManager.Profiles.ToDictionary();

        player.SendTunneled(referenceDataPacketClientProfileData);
    }

    private void SendCoinStoreItemList(Player player)
    {
        var coinStoreItemListPacket = new CoinStoreItemListPacket();

        coinStoreItemListPacket.StaticItems = _resourceManager.CoinStoreItems.ToDictionary();

        player.SendTunneled(coinStoreItemListPacket);

        var clientItemDefinitions = new List<ClientItemDefinition>();

        foreach (var coinStoreItem in _resourceManager.CoinStoreItems)
        {
            if (!_resourceManager.ClientItemDefinitions.TryGetValue(coinStoreItem.Key, out var clientItemDefinition))
                continue;

            clientItemDefinitions.Add(clientItemDefinition);
        }

        using var writer = new PacketWriter();

        writer.Write(clientItemDefinitions);

        var playerUpdatePacketItemDefinitions = new PlayerUpdatePacketItemDefinitions();

        playerUpdatePacketItemDefinitions.Payload = writer.Buffer;

        player.SendTunneled(playerUpdatePacketItemDefinitions);
    }

    private void SendAdventurersJournalInfo(Player player)
    {
        // DO NOT REMOVE even if it's not fully implemented. This packet is needed
        // due to an Area Definition called "Newbiezone" in FabledRealmsAreas.xml.

        var adventurersJournal = new AdventurersJournalInfoPacket();

        AdventurersJournalRegionDefinition[] regions =
        [
            new()
            {
                Id = 1,
                NameId = 5100069,
                DescriptionId = 5100031,
                TabImageId = 35449,
                ChapterMapImageId = 0,
                GeometryId = 244,
                CompletedStringId = 5101408
            },
            new()
            {
                Id = 2,
                NameId = 442123,
                DescriptionId = 5100032,
                TabImageId = 9532,
                ChapterMapImageId = 0,
                GeometryId = 5,
                CompletedStringId = 442681,
            },
            new()
            {
                Id = 3,
                NameId = 3501,
                DescriptionId = 2129,
                TabImageId = 9538,
                ChapterMapImageId = 0,
                GeometryId = 8,
                CompletedStringId = 5101409,
            },
            new()
            {
                Id = 4,
                NameId = 3505,
                DescriptionId = 442685,
                TabImageId = 9529,
                ChapterMapImageId = 0,
                GeometryId = 1,
                CompletedStringId = 442686,
            }
        ];

        adventurersJournal.Regions = regions.ToDictionary(x => x.Id);

        AdventurersJournalHubDefinition[] hubs =
        [
            new()
            {
                Id = 1,
                RegionId = 1,
                DisplayOrder = 1,
                NameId = 442216,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44310,
                CompletedDescriptionId = 5100071,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 2,
                RegionId = 1,
                DisplayOrder = 2,
                NameId = 18735,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44311,
                CompletedDescriptionId = 5100072,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 3,
                RegionId = 1,
                DisplayOrder = 3,
                NameId = 5100069,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44309,
                CompletedDescriptionId = 5100073,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 4,
                RegionId = 2,
                DisplayOrder = 1,
                NameId = 7262,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44941,
                CompletedDescriptionId = 442125,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 5,
                RegionId = 2,
                DisplayOrder = 2,
                NameId = 428995,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44942,
                CompletedDescriptionId = 442126,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 6,
                RegionId = 2,
                DisplayOrder = 3,
                NameId = 442124,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44945,
                CompletedDescriptionId = 442127,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 7,
                RegionId = 2,
                DisplayOrder = 4,
                NameId = 4428,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44943,
                CompletedDescriptionId = 442128,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 8,
                RegionId = 3,
                DisplayOrder = 1,
                NameId = 5101823,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 45267,
                CompletedDescriptionId = 5101824,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 9,
                RegionId = 3,
                DisplayOrder = 2,
                NameId = 5101825,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 45268,
                CompletedDescriptionId = 5101826,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 10,
                RegionId = 4,
                DisplayOrder = 1,
                NameId = 442623,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 45600,
                CompletedDescriptionId = 442687,
                MapX = 0,
                MapY = 0
            }
        ];

        adventurersJournal.Hubs = hubs.ToDictionary(x => x.Id);

        AdventurersJournalHubQuestDefinition[] hubQuests =
        [
            new()
            {
                HubId = 1,
                Id = 2514,
                Unknown = 2
            },
            new()
            {
                HubId = 1,
                Id = 2513,
                Unknown = 1
            },
            new()
            {
                HubId = 2,
                Id = 2521,
                Unknown = 2
            },
            new()
            {
                HubId = 2,
                Id = 2526,
                Unknown = 7
            },
            new()
            {
                HubId = 2,
                Id = 2522,
                Unknown = 3
            },
            new()
            {
                HubId = 2,
                Id = 2523,
                Unknown = 4
            },
            new()
            {
                HubId = 2,
                Id = 2524,
                Unknown = 5
            },
            new()
            {
                HubId = 2,
                Id = 2525,
                Unknown = 6
            },
            new()
            {
                HubId = 3,
                Id = 2529,
                Unknown = 3
            },
            new()
            {
                HubId = 3,
                Id = 2528,
                Unknown = 2
            },
            new()
            {
                HubId = 3,
                Id = 2527,
                Unknown = 1
            },
            new()
            {
                HubId = 3,
                Id = 2566,
                Unknown = 5
            },
            new()
            {
                HubId = 3,
                Id = 2530,
                Unknown = 4
            },
            new()
            {
                HubId = 4,
                Id = 2493,
                Unknown = 6
            },
            new()
            {
                HubId = 4,
                Id = 2492,
                Unknown = 5
            },
            new()
            {
                HubId = 4,
                Id = 2491,
                Unknown = 4
            },
            new()
            {
                HubId = 4,
                Id = 2490,
                Unknown = 3
            },
            new()
            {
                HubId = 4,
                Id = 2489,
                Unknown = 2
            },
            new()
            {
                HubId = 4,
                Id = 2538,
                Unknown = 1
            },
            new()
            {
                HubId = 5,
                Id = 2498,
                Unknown = 6
            },
            new()
            {
                HubId = 5,
                Id = 2497,
                Unknown = 5
            },
            new()
            {
                HubId = 5,
                Id = 2496,
                Unknown = 4
            },
            new()
            {
                HubId = 5,
                Id = 2495,
                Unknown = 3
            },
            new()
            {
                HubId = 5,
                Id = 2494,
                Unknown = 2
            },
            new()
            {
                HubId = 5,
                Id = 2531,
                Unknown = 1
            },
            new()
            {
                HubId = 6,
                Id = 2502,
                Unknown = 4
            },
            new()
            {
                HubId = 6,
                Id = 2501,
                Unknown = 3
            },
            new()
            {
                HubId = 6,
                Id = 2500,
                Unknown = 2
            },
            new()
            {
                HubId = 6,
                Id = 2499,
                Unknown = 1
            },
            new()
            {
                HubId = 6,
                Id = 2503,
                Unknown = 5
            },
            new()
            {
                HubId = 7,
                Id = 2533,
                Unknown = 7
            },
            new()
            {
                HubId = 7,
                Id = 2532,
                Unknown = 1
            },
            new()
            {
                HubId = 7,
                Id = 2504,
                Unknown = 2
            },
            new()
            {
                HubId = 7,
                Id = 2508,
                Unknown = 6
            },
            new()
            {
                HubId = 7,
                Id = 2507,
                Unknown = 5
            },
            new()
            {
                HubId = 7,
                Id = 2505,
                Unknown = 3
            },
            new()
            {
                HubId = 7,
                Id = 2506,
                Unknown = 4
            },
            new()
            {
                HubId = 8,
                Id = 2580,
                Unknown = 5
            },
            new()
            {
                HubId = 8,
                Id = 2578,
                Unknown = 3
            },
            new()
            {
                HubId = 8,
                Id = 2579,
                Unknown = 4
            },
            new()
            {
                HubId = 8,
                Id = 2577,
                Unknown = 2
            },
            new()
            {
                HubId = 8,
                Id = 2576,
                Unknown = 1
            },
            new()
            {
                HubId = 9,
                Id = 2585,
                Unknown = 10
            },
            new()
            {
                HubId = 9,
                Id = 2584,
                Unknown = 9
            },
            new()
            {
                HubId = 9,
                Id = 2583,
                Unknown = 8
            },
            new()
            {
                HubId = 9,
                Id = 2582,
                Unknown = 7
            },
            new()
            {
                HubId = 9,
                Id = 2581,
                Unknown = 6
            },
            new()
            {
                HubId = 9,
                Id = 2600,
                Unknown = 11
            },
            new()
            {
                HubId = 10,
                Id = 2595,
                Unknown = 6
            },
            new()
            {
                HubId = 10,
                Id = 2594,
                Unknown = 5
            },
            new()
            {
                HubId = 10,
                Id = 2591,
                Unknown = 4
            },
            new()
            {
                HubId = 10,
                Id = 2590,
                Unknown = 3
            },
            new()
            {
                HubId = 10,
                Id = 2596,
                Unknown = 7
            },
            new()
            {
                HubId = 10,
                Id = 2588,
                Unknown = 1
            },
            new()
            {
                HubId = 10,
                Id = 2599,
                Unknown = 10
            },
            new()
            {
                HubId = 10,
                Id = 2598,
                Unknown = 9
            },
            new()
            {
                HubId = 10,
                Id = 2597,
                Unknown = 8
            },
            new()
            {
                HubId = 10,
                Id = 2589,
                Unknown = 2
            }
        ];

        adventurersJournal.HubQuests = hubQuests.ToDictionary(x => x.Id);

        AdventurersJournalStickerDefinition[] stickers =
        [
            new()
            {
                Id = 1,
                RegionId = 1,
                DisplayOrder = 1,
                QuestId = 2563,
                NameId = 5100479,
                DescriptionId = 5100480,
                CompletedImageSetId = 43279,
                ImageSetId = 43278,
                Unknown = 0
            },
            new()
            {
                Id = 2,
                RegionId = 1,
                DisplayOrder = 2,
                QuestId = 2564,
                NameId = 5100483,
                DescriptionId = 5100484,
                CompletedImageSetId = 43287,
                ImageSetId = 43286,
                Unknown = 0
            },
            new()
            {
                Id = 3,
                RegionId = 1,
                DisplayOrder = 3,
                QuestId = 2565,
                NameId = 5100487,
                DescriptionId = 5100488,
                CompletedImageSetId = 43273,
                ImageSetId = 43272,
                Unknown = 0
            },
            new()
            {
                Id = 4,
                RegionId = 1,
                DisplayOrder = 4,
                QuestId = 2572,
                NameId = 5100772,
                DescriptionId = 5100773,
                CompletedImageSetId = 43281,
                ImageSetId = 43280,
                Unknown = 0
            },
            new()
            {
                Id = 5,
                RegionId = 1,
                DisplayOrder = 5,
                QuestId = 2573,
                NameId = 5100776,
                DescriptionId = 5100777,
                CompletedImageSetId = 43291,
                ImageSetId = 43290,
                Unknown = 0
            },
            new()
            {
                Id = 6,
                RegionId = 1,
                DisplayOrder = 6,
                QuestId = 2587,
                NameId = 5101187,
                DescriptionId = 5101188,
                CompletedImageSetId = 43283,
                ImageSetId = 43282,
                Unknown = 0
            },
            new()
            {
                Id = 16,
                RegionId = 2,
                DisplayOrder = 1,
                QuestId = 2568,
                NameId = 5100756,
                DescriptionId = 5100757,
                CompletedImageSetId = 43305,
                ImageSetId = 43304,
                Unknown = 0
            },
            new()
            {
                Id = 17,
                RegionId = 2,
                DisplayOrder = 2,
                QuestId = 2569,
                NameId = 5100760,
                DescriptionId = 5100761,
                CompletedImageSetId = 43287,
                ImageSetId = 43286,
                Unknown = 0
            },
            new()
            {
                Id = 18,
                RegionId = 2,
                DisplayOrder = 3,
                QuestId = 2570,
                NameId = 5100764,
                DescriptionId = 5100765,
                CompletedImageSetId = 43273,
                ImageSetId = 43272,
                Unknown = 0
            },
            new()
            {
                Id = 19,
                RegionId = 2,
                DisplayOrder = 4,
                QuestId = 2571,
                NameId = 5100768,
                DescriptionId = 5100769,
                CompletedImageSetId = 43279,
                ImageSetId = 43278,
                Unknown = 0
            },
            new()
            {
                Id = 20,
                RegionId = 2,
                DisplayOrder = 5,
                QuestId = 2574,
                NameId = 5100780,
                DescriptionId = 5100781,
                CompletedImageSetId = 43277,
                ImageSetId = 43276,
                Unknown = 0
            },
            new()
            {
                Id = 21,
                RegionId = 2,
                DisplayOrder = 6,
                QuestId = 2575,
                NameId = 5100784,
                DescriptionId = 5100785,
                CompletedImageSetId = 43283,
                ImageSetId = 43282,
                Unknown = 0
            },
            new()
            {
                Id = 32,
                RegionId = 3,
                DisplayOrder = 2,
                QuestId = 2602,
                NameId = 442851,
                DescriptionId = 442857,
                CompletedImageSetId = 43287,
                ImageSetId = 43286,
                Unknown = 0
            },
            new()
            {
                Id = 35,
                RegionId = 3,
                DisplayOrder = 5,
                QuestId = 2605,
                NameId = 442854,
                DescriptionId = 442860,
                CompletedImageSetId = 43279,
                ImageSetId = 43278,
                Unknown = 0
            },
            new()
            {
                Id = 36,
                RegionId = 3,
                DisplayOrder = 6,
                QuestId = 2606,
                NameId = 442855,
                DescriptionId = 442861,
                CompletedImageSetId = 43305,
                ImageSetId = 43304,
                Unknown = 0
            },
            new()
            {
                Id = 37,
                RegionId = 4,
                DisplayOrder = 1,
                QuestId = 2592,
                NameId = 0,
                DescriptionId = 0,
                CompletedImageSetId = 0,
                ImageSetId = 0,
                Unknown = 0
            }
        ];

        adventurersJournal.Stickers = stickers.ToDictionary(x => x.Id);

        player.SendTunneled(adventurersJournal);
    }

    private void SendWelcomeInfo(Player player)
    {
        var packetLoadWelcomeScreen = new PacketLoadWelcomeScreen();

        packetLoadWelcomeScreen.Contents.AddRange(
        [
            new ContentInfo
            {
                NameId = 6185,
                DescriptionId = 6186,
            },
            new ContentInfo
            {
                NameId = 6187,
                DescriptionId = 6188,
            },
            new ContentInfo
            {
                NameId = 6189,
                DescriptionId = 6190,
            }
        ]);

        packetLoadWelcomeScreen.ClaimCodes.AddRange(
        [
            new ClaimCodeInfo
            {
                Code = "MMMDONUT",
                NameId = 401519,
                DescriptionId = 401534,
                IconId = 929
            },
            new ClaimCodeInfo
            {
                Code = "BERRYCUPCAKE",
                NameId = 401517,
                DescriptionId = 401532,
                IconId = 939
            },
            new ClaimCodeInfo
            {
                Code = "SKELETAL",
                NameId = 409157,
                DescriptionId = 109132,
                IconId = 3459
            },
            new ClaimCodeInfo
            {
                Code = "STRAWBERRIES",
                NameId = 409158,
                DescriptionId = 108948,
                IconId = 3441
            },
            new ClaimCodeInfo
            {
                Code = "FROGGY",
                NameId = 409159,
                DescriptionId = 3141,
                IconId = 1258
            },
            new ClaimCodeInfo
            {
                Code = "SANDWICH",
                NameId = 409160,
                DescriptionId = 2430,
                IconId = 949
            }
        ]);

        player.SendTunneled(packetLoadWelcomeScreen);
    }

    private void SendPlayerCustomizations(Player player)
    {
        var playerUpdatePacketCustomizationData = new PlayerUpdatePacketCustomizationData();

        var customizations = new[]
        {
            new PlayerCustomizationData
            {
                Id = 0, // Head
                Param = player.HeadId,
                StringParam = player.Head
            },
            new PlayerCustomizationData
            {
                Id = 1, // Skin Tone
                Param = player.SkinToneId,
                StringParam = player.SkinTone
            },
            new PlayerCustomizationData
            {
                Id = 2, // Hair
                Param = player.HairId,
                StringParam = player.Hair
            },
            new PlayerCustomizationData
            {
                Id = 3, // Hair Color
                Param = player.HairColor
            },
            new PlayerCustomizationData
            {
                Id = 4, // Eye Color
                Param = player.EyeColor
            },
            new PlayerCustomizationData
            {
                Id = 5, // Model Customization
                Param = player.ModelCustomizationId,
                StringParam = player.ModelCustomization
            },
            new PlayerCustomizationData
            {
                Id = 6, // Face Paint
                Param = player.FacePaintId,
                StringParam = player.FacePaint
            },
            new PlayerCustomizationData
            {
                Id = 8, // Model
                Param = player.Model
            }
        };

        playerUpdatePacketCustomizationData.Customizations.AddRange(customizations);

        player.SendTunneled(playerUpdatePacketCustomizationData);
    }

    private void SendMembershipSubscriptionInfo(Player player)
    {
        var packetMembershipSubscriptionInfo = new PacketMembershipSubscriptionInfo
        {
            IsMember = player.MembershipStatus != 0
        };

        player.SendTunneled(packetMembershipSubscriptionInfo);
    }

    private void SendInGamePurchase(Player player)
    {
        var packetInGamePurchaseEnableMarketplace = new PacketInGamePurchaseEnableMarketplace
        {
            Enabled = true
        };

        player.SendTunneled(packetInGamePurchaseEnableMarketplace);

        var packetInGamePurchaseStoreEnablePaymentSources = new PacketInGamePurchaseStoreEnablePaymentSources
        {
            Sms = true,
            Paypal = true
        };

        player.SendTunneled(packetInGamePurchaseStoreEnablePaymentSources);

        var packetInGamePurchaseStoreBundleCategoryGroups = new PacketInGamePurchaseStoreBundleCategoryGroups();

        packetInGamePurchaseStoreBundleCategoryGroups.CategoryGroups = _resourceManager.StoreBundleCategoryGroups.ToDictionary();

        player.SendTunneled(packetInGamePurchaseStoreBundleCategoryGroups);

        var packetInGamePurchaseStoreBundleCategories = new PacketInGamePurchaseStoreBundleCategories();

        packetInGamePurchaseStoreBundleCategories.CategoryTree.Categories = _resourceManager.StoreBundleCategories.ToDictionary();

        player.SendTunneled(packetInGamePurchaseStoreBundleCategories);

        if (_resourceManager.Stores.TryGetValue(1, out var mainStore))
        {
            var packetInGamePurchaseStoreBundles = new PacketInGamePurchaseStoreBundles();

            packetInGamePurchaseStoreBundles.StoreId = mainStore.Id;

            packetInGamePurchaseStoreBundles.Store.Id = mainStore.Id;
            packetInGamePurchaseStoreBundles.Store.NameId = mainStore.NameId;
            packetInGamePurchaseStoreBundles.Store.DescriptionId = mainStore.DescriptionId;
            packetInGamePurchaseStoreBundles.Store.Image = mainStore.Image;

            foreach (var storeBundle in mainStore.Bundles.Values)
            {
                var valid = storeBundle.Entries.All(x => _resourceManager.ClientItemDefinitions.ContainsKey(x.MarketingItemId));

                if (valid)
                    packetInGamePurchaseStoreBundles.Store.Bundles.Add(storeBundle.Id, storeBundle);
            }

            player.SendTunneled(packetInGamePurchaseStoreBundles);
        }

        var packetInGamePurchaseStoreBundleGroups = new PacketInGamePurchaseStoreBundleGroups();

        packetInGamePurchaseStoreBundleGroups.BundleGroups = _resourceManager.StoreBundleGroups.ToDictionary();

        player.SendTunneled(packetInGamePurchaseStoreBundleGroups);

        /* var inGamePurchaseUpdateSaleDisplay = new InGamePurchaseUpdateSaleDisplay();

        inGamePurchaseUpdateSaleDisplay.Sales.Add(new SaleDisplayInfo
        {
            Id = 12951,
            IconId = 7866,
            TintId = 0,
            TitleId = 824,
            BodyId = 825,
            SecondsLeft = 1000,
            Unknown = 0,
            IsMembership = false
        });

        player.SendTunneled(inGamePurchaseUpdateSaleDisplay); */
    }

    private void SendFriendList(Player player)
    {
        var friendListPacket = new FriendListPacket();

        friendListPacket.Friends = player.Friends;

        player.SendTunneled(friendListPacket);
    }

    private void SendIgnoreList(Player player)
    {
        var ignoreListPacket = new IgnoreListPacket();

        ignoreListPacket.Ignores = player.Ignores;

        player.SendTunneled(ignoreListPacket);
    }

    private void UpdateFriendStatus(Player player)
    {
        var friendOnlinePacket = new FriendOnlinePacket();

        friendOnlinePacket.Guid = player.Guid;

        friendOnlinePacket.IsLocal = true;

        var friendStatusPacket = new FriendStatusPacket
        {
            Guid = player.Guid,
            Status =
            {
                ProfileId = player.ActiveProfile.Id,
                ProfileRank = player.ActiveProfile.Rank,
                ProfileIconId = player.ActiveProfile.Icon,
                ProfileNameId = player.ActiveProfile.NameId,
                ProfileBackgroundImageId = player.ActiveProfile.BadgeImageSet
            }
        };

        foreach (var friend in player.Friends)
        {
            if (!_zoneManager.TryGetPlayer(friend.Guid, out var friendPlayer))
                continue;

            var otherFriendPlayer = friendPlayer.Friends.FirstOrDefault(x => x.Guid == player.Guid);

            if (otherFriendPlayer is null || otherFriendPlayer.Online)
                continue;

            otherFriendPlayer.Online = true;

            friendPlayer.SendTunneled(friendOnlinePacket);
            friendPlayer.SendTunneled(friendStatusPacket);
        }
    }

    #endregion

    public int GetZoneAreaId(Vector4 position)
    {
        foreach (var areaDefinition in _zoneDefinition.AreaDefinitions)
        {
            if (areaDefinition.Shape == "Circle")
            {
                var circle = new Vector3(areaDefinition.X1, 0, areaDefinition.Z1);

                if (position.IsInCircle(circle, areaDefinition.Radius))
                    return areaDefinition.Id;
            }
            else if (areaDefinition.Shape == "Rectangle")
            {
                var p1 = new Vector3(areaDefinition.X1, 0, areaDefinition.Z1);
                var p2 = new Vector3(areaDefinition.X2, 0, areaDefinition.Z2);

                if (position.IsInRectangle(p1, p2))
                    return areaDefinition.Id;
            }
            else
            {
                throw new NotImplementedException(nameof(areaDefinition.Shape));
            }
        }

        return 0;
    }
}
