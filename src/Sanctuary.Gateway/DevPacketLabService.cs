using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Gateway;

public sealed class DevPacketLabService : BackgroundService
{
    private readonly ILogger<DevPacketLabService> _logger;
    private readonly IZoneManager _zoneManager;
    private readonly IResourceManager _resourceManager;
    private readonly string _commandPath = Path.Combine(AppContext.BaseDirectory, "packet-lab.txt");
    private const int DefaultHardPointRespawnSeconds = 5;
    private const float DefaultHardPointLift = 0.0f;
    private readonly List<CollectionSpawnPointDefinition> _draftCollectionHardPoints = [];

    private DateTime _lastWriteUtc;

    public DevPacketLabService(
        ILogger<DevPacketLabService> logger,
        IZoneManager zoneManager,
        IResourceManager resourceManager)
    {
        _logger = logger;
        _zoneManager = zoneManager;
        _resourceManager = resourceManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Dev packet lab watching {path}", _commandPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ProcessCommandFile();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dev packet lab command failed.");
            }

            await Task.Delay(250, stoppingToken);
        }
    }

    private void ProcessCommandFile()
    {
        if (!File.Exists(_commandPath))
            return;

        var lastWriteUtc = File.GetLastWriteTimeUtc(_commandPath);

        if (lastWriteUtc <= _lastWriteUtc)
            return;

        _lastWriteUtc = lastWriteUtc;

        var command = File.ReadLines(_commandPath)
            .Select(x => x.Trim())
            .LastOrDefault(x => !string.IsNullOrEmpty(x) && !x.StartsWith('#'));

        if (string.IsNullOrWhiteSpace(command))
            return;

        HandleCommand(command);
    }

    private void HandleCommand(string command)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
            return;

        var zone = _zoneManager.StartingZone;

        if (zone is null)
        {
            _logger.LogInformation("Dev packet lab waiting for zones to load.");
            return;
        }

        var player = zone.Players.FirstOrDefault();

        if (player is null)
        {
            _logger.LogWarning("Dev packet lab has no logged-in player for command {command}", command);
            return;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "hex":
            case "inject":
                SendHex(player, command[(command.IndexOf(' ') + 1)..]);
                break;

            case "collection":
                SendCollectionProbe(player, parts);
                break;

            case "notify":
                SendNotificationProbe(player, parts);
                break;

            case "collect":
                SendCollectionSpawnCommand(zone, player, parts);
                break;

            case "rewardbundle":
                SendRewardBundleProbe(player, parts);
                break;

            case "showroom":
            case "piles":
                SendCollectionNodeShowroom(zone, player, parts);
                break;

            case "teleport":
            case "tp":
                SendTeleport(player, parts);
                break;

            default:
                _logger.LogWarning("Unknown dev packet lab command {command}", command);
                break;
        }
    }

    private void SendHex(Player player, string hexInput)
    {
        var hex = new string(hexInput.Where(Uri.IsHexDigit).ToArray());

        if (hex.Length == 0 || hex.Length % 2 != 0)
        {
            _logger.LogWarning("Dev packet lab rejected invalid hex. Length={length}", hex.Length);
            return;
        }

        var payload = Convert.FromHexString(hex);
        player.SendTunneled(new RawTunneledPacket(payload));

        _logger.LogInformation("Dev packet lab injected hex packet to {player}. Bytes={bytes}", player.Name, payload.Length);
    }

    private void SendCollectionProbe(Player player, string[] parts)
    {
        var variant = parts.Length > 1 ? parts[1].ToLowerInvariant() : "start1";
        var collectionId = parts.Length > 2 && int.TryParse(parts[2], out var parsedCollectionId)
            ? parsedCollectionId
            : 100001;
        var itemDefinitionId = parts.Length > 3 && int.TryParse(parts[3], out var parsedItemDefinitionId)
            ? parsedItemDefinitionId
            : 3106;

        switch (variant)
        {
            case "start1":
                SendCollectionStartProbe(player, collectionId, itemDefinitionId, 1);
                break;

            case "start2":
                SendCollectionStartProbe(player, collectionId, itemDefinitionId, 2);
                break;

            case "start3":
                SendCollectionStartProbe(player, collectionId, itemDefinitionId, 3);
                break;

            case "add1":
                SendCollectionAddEntryProbe(player, collectionId, itemDefinitionId, 1);
                break;

            case "add2":
                SendCollectionAddEntryProbe(player, collectionId, itemDefinitionId, 2);
                break;

            case "add3":
                SendCollectionAddEntryProbe(player, collectionId, itemDefinitionId, 3);
                break;

            case "remove1":
                SendCollectionRemoveEntryProbe(player, collectionId, itemDefinitionId);
                break;

            default:
                _logger.LogWarning("Unknown collection probe variant {variant}", variant);
                break;
        }

        _logger.LogInformation("Dev packet lab sent collection probe. Player={player} Variant={variant} Collection={collection} Item={item}",
            player.Name,
            variant,
            collectionId,
            itemDefinitionId);
    }

    private static void SendCollectionStartProbe(Player player, int collectionId, int itemDefinitionId, int shape)
    {
        using var writer = new PacketWriter();

        writer.Write((short)38);
        writer.Write((short)8);
        writer.Write(collectionId);

        if (shape >= 2)
        {
            writer.Write(3);
            writer.Write(92821);
            writer.Write(43804);
            writer.Write(607);
        }

        if (shape >= 3)
        {
            writer.Write(1);
            writer.Write(itemDefinitionId);
            writer.Write(0);
            writer.Write(false);
        }

        player.SendTunneled(new RawTunneledPacket(writer.Buffer));
    }

    private static void SendCollectionAddEntryProbe(Player player, int collectionId, int itemDefinitionId, int shape)
    {
        using var writer = new PacketWriter();

        writer.Write((short)38);
        writer.Write((short)10);
        writer.Write(collectionId);
        writer.Write(itemDefinitionId);

        if (shape >= 2)
            writer.Write(1);

        if (shape >= 3)
        {
            writer.Write(607);
            writer.Write(1254);
            writer.Write(true);
        }

        player.SendTunneled(new RawTunneledPacket(writer.Buffer));
    }

    private static void SendCollectionRemoveEntryProbe(Player player, int collectionId, int itemDefinitionId)
    {
        using var writer = new PacketWriter();

        writer.Write((short)38);
        writer.Write((short)11);
        writer.Write(collectionId);
        writer.Write(itemDefinitionId);

        player.SendTunneled(new RawTunneledPacket(writer.Buffer));
    }

    private void SendNotificationProbe(Player player, string[] parts)
    {
        var variant = parts.Length > 1 ? parts[1].ToLowerInvariant() : "list";
        var preset = parts.Length > 2 ? parts[2].ToLowerInvariant() : "mushroom";
        var sample = GetCollectionNotificationSample(preset);

        switch (variant)
        {
            case "list":
                _logger.LogInformation("Notification probes: notify start [mushroom|water], update, complete, rawstart, rawupdate, rawcomplete, funcupdate, dotupdate, clientupdate, handlerupdate, uimsg1, uimsg2, uimsg3, batch.");
                return;

            case "batch":
                SendCollectionNotificationBatch(player, sample);
                break;

            case "start":
                SendCollectionNotification(player, "NotificationHandler:StartCollection", sample, includeItem: false);
                break;

            case "update":
                SendCollectionNotification(player, "NotificationHandler:UpdateCollection", sample, includeItem: true);
                break;

            case "complete":
                SendCollectionNotification(player, "NotificationHandler:CompleteCollection", sample, includeItem: false);
                break;

            case "rawstart":
                SendRawCollectionNotification(player, "NotificationHandler:StartCollection", sample, includeItem: false);
                break;

            case "rawupdate":
                SendRawCollectionNotification(player, "NotificationHandler:UpdateCollection", sample, includeItem: true);
                break;

            case "rawcomplete":
                SendRawCollectionNotification(player, "NotificationHandler:CompleteCollection", sample, includeItem: false);
                break;

            case "funcupdate":
                SendCollectionNotification(player, "UpdateCollection", sample, includeItem: true);
                break;

            case "dotupdate":
                SendCollectionNotification(player, "NotificationHandler.UpdateCollection", sample, includeItem: true);
                break;

            case "clientupdate":
                SendCollectionNotification(player, "Client.NotificationHandler.UpdateCollection", sample, includeItem: true);
                break;

            case "handlerupdate":
                SendCollectionNotification(player, "HandlerNotification:UpdateCollection", sample, includeItem: true);
                break;

            case "uimsg1":
                SendUiMessageProbe(player, "NotificationHandler", "UpdateCollection", FormatCollectionNotificationParam(sample, includeItem: true));
                break;

            case "uimsg2":
                SendUiMessageProbe(player, "Main.wndNotificationQuests.swfNotificationQuests", "UpdateCollection", FormatCollectionNotificationParam(sample, includeItem: true));
                break;

            case "uimsg3":
                SendUiMessageProbe(player, "Main.wndNotificationQuests", "UpdateCollection", FormatCollectionNotificationParam(sample, includeItem: true));
                break;

            default:
                _logger.LogWarning("Unknown notification probe variant {variant}.", variant);
                return;
        }

        _logger.LogInformation("Dev packet lab sent notification probe. Player={player} Variant={variant} Preset={preset}",
            player.Name,
            variant,
            preset);
    }

    private static void SendCollectionNotificationBatch(Player player, CollectionNotificationSample sample)
    {
        SendCollectionNotification(player, "UpdateCollection", sample, includeItem: true);
        SendCollectionNotification(player, "NotificationHandler.UpdateCollection", sample, includeItem: true);
        SendCollectionNotification(player, "Client.NotificationHandler.UpdateCollection", sample, includeItem: true);
        SendCollectionNotification(player, "HandlerNotification:UpdateCollection", sample, includeItem: true);
        SendUiMessageProbe(player, "NotificationHandler", "UpdateCollection", FormatCollectionNotificationParam(sample, includeItem: true));
        SendUiMessageProbe(player, "Main.wndNotificationQuests.swfNotificationQuests", "UpdateCollection", FormatCollectionNotificationParam(sample, includeItem: true));
        SendUiMessageProbe(player, "Main.wndNotificationQuests", "UpdateCollection", FormatCollectionNotificationParam(sample, includeItem: true));
    }

    private CollectionNotificationSample GetCollectionNotificationSample(string preset)
    {
        var sample = preset switch
        {
            "water" or "sparklingwater" or "sparkling-water" => new CollectionNotificationSample(
                "Collection",
                "Sparkling Water",
                "Sparkling Water",
                729,
                0,
                729,
                0),
            _ => new CollectionNotificationSample(
                "Collection",
                "Briarwood Mushrooms",
                "Purple Spotted Mushroom",
                2124,
                99,
                2124,
                99)
        };

        if (!int.TryParse(preset, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemDefinitionId) ||
            !_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition))
            return sample;

        return sample with
        {
            ItemText = $"Item {itemDefinition.Id}",
            ItemImageSetId = itemDefinition.Icon.Id,
            ItemImageTint = itemDefinition.Icon.TintId
        };
    }

    private static void SendCollectionNotification(
        Player player,
        string script,
        CollectionNotificationSample sample,
        bool includeItem)
    {
        var intParams = new List<int>
        {
            sample.CollectionImageSetId,
            sample.CollectionImageTint
        };

        var stringParams = new List<string>
        {
            sample.Title,
            sample.CollectionText
        };

        if (includeItem)
        {
            intParams.Add(sample.ItemImageSetId);
            intParams.Add(sample.ItemImageTint);
            stringParams.Add(sample.ItemText);
        }

        player.SendTunneled(new ExecuteScriptWithStringParamsPacket
        {
            Script = script,
            Params = intParams,
            StringParams = stringParams
        });
    }

    private static void SendRawCollectionNotification(
        Player player,
        string script,
        CollectionNotificationSample sample,
        bool includeItem)
    {
        var args = includeItem
            ? $@"""{EscapeLua(sample.Title)}"", ""{EscapeLua(sample.CollectionText)}"", {sample.CollectionImageSetId}, {sample.CollectionImageTint}, ""{EscapeLua(sample.ItemText)}"", {sample.ItemImageSetId}, {sample.ItemImageTint}"
            : $@"""{EscapeLua(sample.Title)}"", ""{EscapeLua(sample.CollectionText)}"", {sample.CollectionImageSetId}, {sample.CollectionImageTint}";

        player.SendTunneled(new ExecuteScriptPacket
        {
            Script = $"{script}({args})"
        });
    }

    private static void SendUiMessageProbe(Player player, string target, string callback, string param)
    {
        using var writer = new PacketWriter();

        writer.Write((short)47);
        writer.Write((byte)15);
        writer.Write(target);
        writer.Write(callback);
        writer.Write(param);

        player.SendTunneled(new RawTunneledPacket(writer.Buffer));
    }

    private static string FormatCollectionNotificationParam(CollectionNotificationSample sample, bool includeItem)
    {
        return includeItem
            ? string.Join('|',
                sample.Title,
                sample.CollectionText,
                sample.CollectionImageSetId.ToString(CultureInfo.InvariantCulture),
                sample.CollectionImageTint.ToString(CultureInfo.InvariantCulture),
                sample.ItemText,
                sample.ItemImageSetId.ToString(CultureInfo.InvariantCulture),
                sample.ItemImageTint.ToString(CultureInfo.InvariantCulture))
            : string.Join('|',
                sample.Title,
                sample.CollectionText,
                sample.CollectionImageSetId.ToString(CultureInfo.InvariantCulture),
                sample.CollectionImageTint.ToString(CultureInfo.InvariantCulture));
    }

    private static string EscapeLua(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private void SendCollectionSpawnCommand(StartingZone zone, Player player, string[] parts)
    {
        var action = parts.Length > 1 ? parts[1].ToLowerInvariant() : "reload";

        switch (action)
        {
            case "clear":
                {
                    var removed = zone.ClearCollectionSpawnRegionNodes();
                    _logger.LogInformation("Dev packet lab cleared collection spawn nodes. Removed={removed}", removed);
                    return;
                }

            case "reload":
            case "spawn":
                {
                    _resourceManager.CollectionSpawnRegions.Load(ResourceManager.CollectionSpawnRegionsFile);
                    _resourceManager.CollectionNodes.Load(ResourceManager.CollectionNodesFile);

                    var count = zone.ReloadCollectionSpawnRegionNodes();
                    _logger.LogInformation("Dev packet lab reloaded collection spawn regions. Count={count}", count);
                    return;
                }

            case "here":
                {
                    if (parts.Length < 3)
                    {
                        _logger.LogWarning("Invalid collect here command. Use: collect here <nodeKey> [count] [radius] [respawnSeconds]");
                        return;
                    }

                    var nodeKey = parts[2].ToLowerInvariant();
                    var count = parts.Length > 3 && int.TryParse(parts[3], out var parsedCount)
                        ? Math.Clamp(parsedCount, 1, 50)
                        : 8;
                    var radius = parts.Length > 4 && float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRadius)
                        ? Math.Clamp(parsedRadius, 1.0f, 100.0f)
                        : 20.0f;
                    var respawnSeconds = parts.Length > 5 && int.TryParse(parts[5], out var parsedRespawnSeconds)
                        ? Math.Clamp(parsedRespawnSeconds, 5, 600)
                        : 45;

                    var spawned = zone.SpawnAdHocCollectionRegion(player.Position, nodeKey, count, radius, respawnSeconds);

                    _logger.LogInformation(
                        "Dev packet lab spawned ad-hoc collection region. Player={player} Node={node} Count={count} Radius={radius} RespawnSeconds={respawnSeconds} Spawned={spawned}",
                        player.Name,
                        nodeKey,
                        count,
                        radius,
                        respawnSeconds,
                        spawned);
                    return;
                }

            case "visual":
                {
                    var nodeKey = parts.Length > 2 ? parts[2].ToLowerInvariant() : "flowers";
                    var useEffects = parts.Length <= 3 || !parts[3].Equals("nofx", StringComparison.OrdinalIgnoreCase);
                    var count = zone.SpawnCollectionNodeVisualProbe(player.Position, nodeKey, useEffects);

                    _logger.LogInformation(
                        "Dev packet lab spawned collection visual probe. Player={player} Node={node} Effects={effects} Count={count}",
                        player.Name,
                        nodeKey,
                        useEffects,
                        count);
                    return;
                }

            case "visualpoint":
                {
                    var nodeKey = parts.Length > 2 ? parts[2].ToLowerInvariant() : "flowers";
                    var lift = parts.Length > 3 && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLift)
                        ? Math.Clamp(parsedLift, -5.0f, 10.0f)
                        : DefaultHardPointLift;
                    var position = GetPlacementPosition(player.Position, player.Rotation, lift);
                    var count = zone.SpawnCollectionNodeVisualProbeAt(position, nodeKey, true);

                    _logger.LogInformation(
                        "Dev packet lab spawned collection visual point probe. Player={player} Node={node} Position=({x}, {y}, {z}) Count={count}",
                        player.Name,
                        nodeKey,
                        position.X,
                        position.Y,
                        position.Z,
                        count);
                    return;
                }

            case "visualself":
                {
                    var nodeKey = parts.Length > 2 ? parts[2].ToLowerInvariant() : "flowers";
                    var lift = parts.Length > 3 && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLift)
                        ? Math.Clamp(parsedLift, -5.0f, 10.0f)
                        : 0.0f;
                    var mode = parts.Length > 4 ? parts[4].ToLowerInvariant() : "plain";
                    var isInteractable = mode is "interact" or "range" or "cursor" or "full";
                    var interactRange = mode is "range" or "cursor" or "full" ? 12 : 0;
                    var cursorId = mode is "cursor" or "full" ? (byte)18 : (byte)0;
                    var position = player.Position + new Vector4(0, lift, 0, 0);
                    var count = zone.SpawnCollectionNodeVisualProbeAt(position, nodeKey, true, isInteractable, interactRange, cursorId);

                    _logger.LogInformation(
                        "Dev packet lab spawned collection visual self probe. Player={player} Node={node} Mode={mode} Position=({x}, {y}, {z}) Count={count}",
                        player.Name,
                        nodeKey,
                        mode,
                        position.X,
                        position.Y,
                        position.Z,
                        count);
                    return;
                }

            case "hard":
            case "point":
            case "stamp":
                HandleCollectionHardPointCommand(zone, player, parts);
                return;

            case "list":
                {
                    var nodeKeys = string.Join(", ", _resourceManager.CollectionNodes.Keys.Order());
                    var regionKeys = string.Join(", ", _resourceManager.CollectionSpawnRegions.Keys.Order());

                    _logger.LogInformation("Collection nodes: {nodes}", nodeKeys);
                    _logger.LogInformation("Collection spawn regions: {regions}", regionKeys);
                    return;
                }

            case "node":
            case "def":
                UpsertRuntimeCollectionNode(parts);
                return;

            case "nameplate":
            case "tag":
                UpdateCollectionNodeNameplate(parts);
                return;

            default:
                _logger.LogWarning("Unknown collect command {action}. Use: collect reload | collect clear | collect here <nodeKey> [count] [radius] [respawnSeconds] | collect node <key> <modelId> [effectId] [scale] [name] | collect nameplate <nodeKey> [subTextNameId] [unknown36] [temporaryAppearance] [nameColor] [nameScale] [nameplateImageId] [unknown33] [unknown34] | collect list", action);
                return;
        }
    }

    private void UpdateCollectionNodeNameplate(string[] parts)
    {
        if (parts.Length < 3)
        {
            _logger.LogWarning("Invalid collect nameplate command. Use: collect nameplate <nodeKey> [subTextNameId] [unknown36] [temporaryAppearance] [nameColor] [nameScale] [nameplateImageId] [unknown33] [unknown34]");
            return;
        }

        var key = parts[2].ToLowerInvariant();

        if (!_resourceManager.CollectionNodes.TryGetValue(key, out var node))
        {
            _logger.LogWarning("Collection node {key} not found.", key);
            return;
        }

        node.SubTextNameId = ParseIntOrExisting(parts, 3, node.SubTextNameId);
        node.Unknown36 = ParseIntOrExisting(parts, 4, node.Unknown36);
        node.TemporaryAppearance = ParseIntOrExisting(parts, 5, node.TemporaryAppearance);
        node.NameColor = ParseFloatOrExisting(parts, 6, node.NameColor);
        node.NameScale = ParseFloatOrExisting(parts, 7, node.NameScale);
        node.NameplateImageId = ParseIntOrExisting(parts, 8, node.NameplateImageId);
        node.Unknown33 = ParseBoolOrExisting(parts, 9, node.Unknown33);
        node.Unknown34 = ParseBoolOrExisting(parts, 10, node.Unknown34);

        _logger.LogInformation(
            "Updated runtime collection node nameplate. Key={key} Unknown33={unknown33} Unknown34={unknown34} SubText={subText} Unknown36={unknown36} TempAppearance={temporaryAppearance} NameColor={nameColor} NameScale={nameScale} NameplateImage={nameplateImage}.",
            key,
            node.Unknown33,
            node.Unknown34,
            node.SubTextNameId,
            node.Unknown36,
            node.TemporaryAppearance,
            node.NameColor,
            node.NameScale,
            node.NameplateImageId);
    }

    private static int ParseIntOrExisting(string[] parts, int index, int existing)
    {
        return parts.Length > index && int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : existing;
    }

    private static bool ParseBoolOrExisting(string[] parts, int index, bool existing)
    {
        if (parts.Length <= index)
            return existing;

        return parts[index].ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => existing
        };
    }

    private static float ParseFloatOrExisting(string[] parts, int index, float existing)
    {
        return parts.Length > index && float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : existing;
    }

    private void UpsertRuntimeCollectionNode(string[] parts)
    {
        if (parts.Length < 4)
        {
            _logger.LogWarning("Invalid collect node command. Use: collect node <key> <modelId> [effectId] [scale] [name]");
            return;
        }

        var key = parts[2].ToLowerInvariant();

        if (!int.TryParse(parts[3], out var modelId))
        {
            _logger.LogWarning("Invalid collect node model id {modelId}", parts[3]);
            return;
        }

        _resourceManager.CollectionNodes.TryGetValue(key, out var existing);
        var effectIds = parts.Length > 4
            ? ParseEffectIds(parts[4])
            : [];
        var effectId = effectIds.FirstOrDefault() > 0
            ? effectIds[0]
            : existing?.CompositeEffectId ?? 5742;
        var extraEffectIds = effectIds.Length > 1
            ? effectIds.Skip(1).ToArray()
            : existing?.ExtraCompositeEffectIds ?? [];
        var scale = parts.Length > 5 && float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedScale)
            ? Math.Clamp(parsedScale, 0.01f, 25.0f)
            : existing?.Scale ?? 1.0f;
        var name = parts.Length > 6
            ? string.Join(' ', parts.Skip(6))
            : existing?.Name ?? key;
        var rewards = existing?.Rewards
            ?? (_resourceManager.CollectionNodes.TryGetValue("water", out var waterNode)
                ? waterNode.Rewards
                : _resourceManager.CollectionNodes.TryGetValue("flowers", out var flowerNode)
                    ? flowerNode.Rewards
                    : []);

        _resourceManager.CollectionNodes[key] = new CollectionNodeDefinition
        {
            Key = key,
            Name = name,
            CollectionId = existing?.CollectionId ?? 100001,
            CollectionEntryIds = existing?.CollectionEntryIds ?? [],
            NameId = existing?.NameId ?? 0,
            SubTextNameId = existing?.SubTextNameId ?? 0,
            Unknown33 = existing?.Unknown33 ?? false,
            Unknown34 = existing?.Unknown34 ?? false,
            Unknown36 = existing?.Unknown36 ?? 0,
            TemporaryAppearance = existing?.TemporaryAppearance ?? 0,
            NameColor = existing?.NameColor ?? 0,
            NameScale = existing?.NameScale ?? 0,
            NameplateImageId = existing?.NameplateImageId ?? 0,
            ModelId = modelId,
            RareModelId = existing?.RareModelId ?? 0,
            Scale = scale,
            PositionW = existing?.PositionW,
            CompositeEffectId = effectId,
            ExtraCompositeEffectIds = extraEffectIds,
            RareCompositeEffectId = existing?.RareCompositeEffectId ?? 5740,
            InteractRange = existing?.InteractRange ?? 12,
            CursorId = existing?.CursorId ?? 18,
            HideNamePlate = existing?.HideNamePlate ?? false,
            Rewards = rewards
        };

        _logger.LogInformation(
            "Updated runtime collection node. Key={key} Model={model} Effect={effect} ExtraEffects={extraEffects} Scale={scale} Name={name}. This is in-memory only; copy to CollectionNodes.json to persist.",
            key,
            modelId,
            effectId,
            string.Join(",", extraEffectIds),
            scale,
            name);
    }

    private static int[] ParseEffectIds(string value)
    {
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var effectId) ? effectId : 0)
            .Where(x => x > 0)
            .ToArray();
    }

    private void HandleCollectionHardPointCommand(StartingZone zone, Player player, string[] parts)
    {
        var isStampAlias = parts[1].Equals("stamp", StringComparison.OrdinalIgnoreCase);
        var action = parts.Length > 2
            ? isStampAlias ? "add" : parts[2].ToLowerInvariant()
            : isStampAlias ? "add" : "list";

        switch (action)
        {
            case "add":
            case "place":
                {
                    var nodeKeyIndex = isStampAlias ? 2 : 3;
                    var respawnSecondsIndex = isStampAlias ? 3 : 4;
                    var liftIndex = isStampAlias ? 4 : 5;
                    var nodeKey = parts.Length > nodeKeyIndex
                        ? parts[nodeKeyIndex].ToLowerInvariant()
                        : "flowers";
                    var respawnSeconds = parts.Length > respawnSecondsIndex && int.TryParse(parts[respawnSecondsIndex], out var parsedRespawnSeconds)
                        ? Math.Clamp(parsedRespawnSeconds, 5, 600)
                        : DefaultHardPointRespawnSeconds;
                    var lift = parts.Length > liftIndex && float.TryParse(parts[liftIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLift)
                        ? Math.Clamp(parsedLift, -5.0f, 5.0f)
                        : DefaultHardPointLift;

                    var position = GetPlacementPosition(player.Position, player.Rotation, lift);
                    var point = new CollectionSpawnPointDefinition
                    {
                        NodeKey = nodeKey,
                        X = position.X,
                        Y = position.Y,
                        Z = position.Z,
                        RespawnSeconds = respawnSeconds
                    };

                    if (!zone.SpawnCollectionHardPoint(new Vector4(point.X, point.Y, point.Z, 1), nodeKey, respawnSeconds))
                    {
                        _logger.LogWarning("Failed to stamp collection hard point. Node={node}", nodeKey);
                        return;
                    }

                    _draftCollectionHardPoints.Add(point);

                    _logger.LogInformation(
                        "Stamped collection hard point. Index={index} Node={node} Position=({x}, {y}, {z}) RespawnSeconds={respawnSeconds}",
                        _draftCollectionHardPoints.Count,
                        point.NodeKey,
                        point.X,
                        point.Y,
                        point.Z,
                        point.RespawnSeconds);
                    return;
                }

            case "undo":
                {
                    if (_draftCollectionHardPoints.Count == 0)
                    {
                        _logger.LogInformation("No draft collection hard points to undo.");
                        return;
                    }

                    var removed = _draftCollectionHardPoints[^1];
                    _draftCollectionHardPoints.RemoveAt(_draftCollectionHardPoints.Count - 1);
                    zone.ClearCollectionSpawnRegionNodes();

                    foreach (var point in _draftCollectionHardPoints)
                        zone.SpawnCollectionHardPoint(new Vector4(point.X, point.Y, point.Z, 1), point.NodeKey, point.RespawnSeconds);

                    _logger.LogInformation(
                        "Undid collection hard point. Removed Node={node} Position=({x}, {y}, {z}) Remaining={remaining}",
                        removed.NodeKey,
                        removed.X,
                        removed.Y,
                        removed.Z,
                        _draftCollectionHardPoints.Count);
                    return;
                }

            case "clear":
                {
                    var pointCount = _draftCollectionHardPoints.Count;
                    _draftCollectionHardPoints.Clear();
                    var nodeCount = zone.ClearCollectionSpawnRegionNodes();
                    _logger.LogInformation("Cleared draft collection hard points. Points={points} Nodes={nodes}", pointCount, nodeCount);
                    return;
                }

            case "list":
                {
                    _logger.LogInformation("Draft collection hard points: {count}", _draftCollectionHardPoints.Count);

                    for (var i = 0; i < _draftCollectionHardPoints.Count; i++)
                    {
                        var point = _draftCollectionHardPoints[i];
                        _logger.LogInformation(
                            "#{index} {node} ({x}, {y}, {z}) respawn={respawn}",
                            i + 1,
                            point.NodeKey,
                            point.X,
                            point.Y,
                            point.Z,
                            point.RespawnSeconds);
                    }

                    return;
                }

            case "save":
                {
                    if (_draftCollectionHardPoints.Count == 0)
                    {
                        _logger.LogWarning("No draft collection hard points to save.");
                        return;
                    }

                    var regionKey = parts.Length > 3
                        ? parts[3]
                        : $"hard-points-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                    var activeCount = parts.Length > 4 && int.TryParse(parts[4], out var parsedActiveCount)
                        ? Math.Clamp(parsedActiveCount, 1, _draftCollectionHardPoints.Count)
                        : _draftCollectionHardPoints.Count;

                    SaveCollectionHardPoints(regionKey, activeCount);
                    _resourceManager.CollectionSpawnRegions.Load(ResourceManager.CollectionSpawnRegionsFile);

                    _logger.LogInformation("Saved collection hard points. Region={region} Points={points} ActiveCount={activeCount}", regionKey, _draftCollectionHardPoints.Count, activeCount);
                    return;
                }

            default:
                _logger.LogWarning("Unknown collection hard point command {action}. Use: collect hard add <nodeKey> [respawnSeconds] | undo | clear | list | save <regionKey> [activeCount]", action);
                return;
        }
    }

    private static Vector4 GetPlacementPosition(Vector4 playerPosition, Quaternion playerRotation, float lift)
    {
        return playerPosition + new Vector4(0, lift, 0, 0);
    }

    private void SaveCollectionHardPoints(string regionKey, int activeCount)
    {
        var filePath = ResourceManager.CollectionSpawnRegionsFile;
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        var regions = File.Exists(filePath)
            ? JsonSerializer.Deserialize<List<CollectionSpawnRegionDefinition>>(File.ReadAllText(filePath), options) ?? []
            : [];

        var center = _draftCollectionHardPoints[0];
        var savedRegion = new CollectionSpawnRegionDefinition
        {
            Key = regionKey,
            ZoneId = 1,
            CenterX = center.X,
            CenterY = center.Y,
            CenterZ = center.Z,
            Radius = 0,
            Count = activeCount,
            RespawnSeconds = DefaultHardPointRespawnSeconds,
            ShuffleOnRespawn = false,
            NodeKeys = _draftCollectionHardPoints
                .Select(x => x.NodeKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Points = _draftCollectionHardPoints.ToArray()
        };

        regions.RemoveAll(x => string.Equals(x.Key, regionKey, StringComparison.OrdinalIgnoreCase));
        regions.Add(savedRegion);

        File.WriteAllText(filePath, JsonSerializer.Serialize(regions, options));
    }

    private void SendRewardBundleProbe(Player player, string[] parts)
    {
        var itemDefinitionId = parts.Length > 1 && int.TryParse(parts[1], out var parsedItemDefinitionId)
            ? parsedItemDefinitionId
            : 3106;

        if (!_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition))
        {
            _logger.LogWarning("Unknown reward bundle item definition {item}", itemDefinitionId);
            return;
        }

        var probeId = Environment.TickCount & int.MaxValue;

        player.SendTunneled(new RewardBundlePacket
        {
            SourceGuid = player.Guid ^ (uint)probeId,
            PlayerGuid = player.Guid,
            IconId = itemDefinition.Icon.Id,
            NameId = itemDefinition.NameId,
            Quantity = 1,
            EntryIconId = itemDefinition.Icon.Id,
            EntryNameId = itemDefinition.NameId,
            EntryQuantity = 1,
            ItemDefinitionId = itemDefinition.Id,
            Tint = itemDefinition.Icon.TintId,
            ItemGuid = probeId,
            EntryUnknown5 = itemDefinition.DescriptionId
        });

        _logger.LogInformation("Dev packet lab sent reward bundle probe. Player={player} Item={item}", player.Name, itemDefinitionId);
    }

    private void SendCollectionNodeShowroom(StartingZone zone, Player player, string[] parts)
    {
        var args = parts.Skip(1).Select(x => x.ToLowerInvariant()).ToArray();
        var layout = args.Contains("scatter") ? "scatter" : "grid";
        var batch = args.FirstOrDefault(x => x is "all" or "base" or "rare" or "jobs" or "misc" or "fossils" or "springs" or "soccer" or "water" or "loot" or "chests") ?? "all";
        var useEffects = args.Any(x => x is "fx" or "effects" or "sparkle" or "sparkles");

        if (args.Contains("clear"))
        {
            var removed = zone.ClearCollectionNodeShowroom();
            _logger.LogInformation("Dev packet lab cleared collection node showroom. Removed={removed}", removed);
            return;
        }

        if (args.Contains("reloadmodels") || args.Contains("reload-models"))
        {
            var loaded = _resourceManager.Models.Load(ResourceManager.ModelsFile);
            _logger.LogInformation("Dev packet lab reloaded Models.txt. Loaded={loaded} Count={count}", loaded, _resourceManager.Models.Count);
            return;
        }

        var modelIndex = Array.IndexOf(args, "model");
        if (modelIndex >= 0)
        {
            if (args.Length <= modelIndex + 1 || !int.TryParse(args[modelIndex + 1], out var modelId))
            {
                _logger.LogWarning("Invalid showroom model command. Use: showroom model <modelId> [fx]");
                return;
            }

            var modelCount = zone.SpawnCollectionNodeShowroomModel(player.Position, modelId, useEffects);
            _logger.LogInformation(
                "Dev packet lab spawned collection node showroom model. Player={player} Model={model} Effects={effects} Count={count}",
                player.Name,
                modelId,
                useEffects,
                modelCount);
            return;
        }

        var modelsIndex = Array.IndexOf(args, "models");
        if (modelsIndex >= 0)
        {
            var ids = ParseModelIdSelections(args.Skip(modelsIndex + 1))
                .Take(100)
                .ToArray();

            if (ids.Length == 0)
            {
                _logger.LogWarning("Invalid showroom models command. Use: showroom models <id>[,<id>|-<id>]... [fx] [grid|scatter]");
                return;
            }

            var entries = ids.Select(GetModelShowroomEntry).ToArray();
            var explicitCount = zone.SpawnModelShowroom(player.Position, entries, layout, useEffects);
            _logger.LogInformation("Dev packet lab spawned explicit model showroom. Player={player} Count={count} Ids={ids}", player.Name, explicitCount, string.Join(",", ids));
            return;
        }

        var aroundIndex = Array.IndexOf(args, "around");
        if (aroundIndex >= 0)
        {
            if (args.Length <= aroundIndex + 1 || !int.TryParse(args[aroundIndex + 1], out var centerModelId))
            {
                _logger.LogWarning("Invalid showroom around command. Use: showroom around <modelId> [idRadius] [fx] [grid|scatter]");
                return;
            }

            var idRadius = args.Length > aroundIndex + 2 && int.TryParse(args[aroundIndex + 2], out var parsedRadius)
                ? Math.Clamp(parsedRadius, 1, 250)
                : 25;
            var entries = _resourceManager.Models.Values
                .Where(x => Math.Abs(x.Id - centerModelId) <= idRadius)
                .OrderBy(x => x.Id)
                .Take(100)
                .Select(ToModelShowroomEntry)
                .ToArray();
            var aroundCount = zone.SpawnModelShowroom(player.Position, entries, layout, useEffects);

            _logger.LogInformation("Dev packet lab spawned model-id neighborhood. Player={player} Center={center} Radius={radius} Count={count}", player.Name, centerModelId, idRadius, aroundCount);
            return;
        }

        var searchIndex = Array.IndexOf(args, "search");
        if (searchIndex < 0)
            searchIndex = Array.IndexOf(args, "find");

        if (searchIndex >= 0)
        {
            var (terms, limit) = ParseModelSearch(args.Skip(searchIndex + 1));

            if (terms.Length == 0)
            {
                _logger.LogWarning("Invalid showroom search command. Use: showroom search <term> [term...] [limit] [fx] [grid|scatter]");
                return;
            }

            var entries = SearchModels(terms, limit)
                .Select(ToModelShowroomEntry)
                .ToArray();
            var searchCount = zone.SpawnModelShowroom(player.Position, entries, layout, useEffects);

            _logger.LogInformation("Dev packet lab spawned model search showroom. Player={player} Terms={terms} Limit={limit} Count={count}", player.Name, string.Join(" ", terms), limit, searchCount);
            return;
        }

        if (layout is not ("grid" or "scatter"))
        {
            _logger.LogWarning("Unknown collection node showroom layout {layout}", layout);
            return;
        }

        var count = zone.SpawnCollectionNodeShowroom(player.Position, batch, layout, useEffects);

        _logger.LogInformation(
            "Dev packet lab spawned collection node showroom. Player={player} Batch={batch} Layout={layout} Effects={effects} Count={count}",
            player.Name,
            batch,
            layout,
            useEffects,
            count);
    }

    private IEnumerable<ModelDefinition> SearchModels(string[] terms, int limit)
    {
        return _resourceManager.Models.Values
            .Where(model =>
            {
                var haystack = string.Join(' ', model.ModelFileName, model.Description, model.Descriptor).ToLowerInvariant();
                return terms.All(haystack.Contains);
            })
            .OrderBy(x => x.Id)
            .Take(limit);
    }

    private (string[] Terms, int Limit) ParseModelSearch(IEnumerable<string> rawArgs)
    {
        var terms = new List<string>();
        var limit = 42;

        foreach (var arg in rawArgs)
        {
            if (IsShowroomOption(arg))
                continue;

            if (int.TryParse(arg, out var parsedLimit))
            {
                limit = Math.Clamp(parsedLimit, 1, 100);
                continue;
            }

            terms.Add(arg);
        }

        return (terms.ToArray(), limit);
    }

    private static IEnumerable<int> ParseModelIdSelections(IEnumerable<string> rawArgs)
    {
        foreach (var arg in rawArgs)
        {
            if (IsShowroomOption(arg))
                continue;

            foreach (var token in arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var rangeParts = token.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (rangeParts.Length == 2 &&
                    int.TryParse(rangeParts[0], out var start) &&
                    int.TryParse(rangeParts[1], out var end))
                {
                    if (end < start)
                        (start, end) = (end, start);

                    for (var id = start; id <= end && id - start < 100; id++)
                        yield return id;

                    continue;
                }

                if (int.TryParse(token, out var modelId))
                    yield return modelId;
            }
        }
    }

    private (int ModelId, string Name, bool IsRare) GetModelShowroomEntry(int modelId)
    {
        return _resourceManager.Models.TryGetValue(modelId, out var model)
            ? ToModelShowroomEntry(model)
            : (modelId, "unknown", false);
    }

    private static (int ModelId, string Name, bool IsRare) ToModelShowroomEntry(ModelDefinition model)
    {
        var name = Path.GetFileNameWithoutExtension(model.ModelFileName);

        if (!string.IsNullOrWhiteSpace(model.Description))
            name = $"{name} - {model.Description}";

        return (model.Id, name, false);
    }

    private static bool IsShowroomOption(string value)
    {
        return value is "fx" or "effects" or "sparkle" or "sparkles" or "grid" or "scatter";
    }

    private void SendTeleport(Player player, string[] parts)
    {
        if (!TryGetTeleportDestination(player, parts, out var position))
        {
            _logger.LogWarning("Invalid teleport command. Use: teleport safe | teleport spawn | teleport forward [meters] | teleport up [meters] | teleport <x> <y> <z>");
            return;
        }

        var rotation = player.Rotation;

        player.UpdatePosition(position, rotation);
        player.Mount?.UpdatePosition(position, rotation);

        player.SendTunneled(new ClientUpdatePacketUpdateLocation
        {
            Position = position,
            Rotation = rotation,
            Teleport = true
        });

        _logger.LogInformation(
            "Dev packet lab teleported player. Player={player} Position={position}",
            player.Name,
            position);
    }

    private static bool TryGetTeleportDestination(Player player, string[] parts, out Vector4 position)
    {
        position = default;

        if (parts.Length < 2)
            return false;

        switch (parts[1].ToLowerInvariant())
        {
            case "demo":
            case "southeast":
                position = new Vector4(-1414.636f, -27.631f, 351.567f, 1f);
                return true;

            case "east":
                position = new Vector4(-1384.636f, -27.631f, 351.567f, 1f);
                return true;

            case "north":
                position = new Vector4(-1414.636f, -27.631f, 381.567f, 1f);
                return true;

            case "safe":
                position = new Vector4(-1414.636f, -27.631f, 351.567f, 1f);
                return true;

            case "spawn":
                position = new Vector4(-1904.883f, -39.7098f, 412.6024f, 1f);
                return true;

            case "forward":
            {
                var meters = TryGetFloat(parts, 2, out var value) ? value : 10f;
                position = new Vector4(player.Position.X, player.Position.Y, player.Position.Z + meters, 1f);
                return true;
            }

            case "back":
            {
                var meters = TryGetFloat(parts, 2, out var value) ? value : 10f;
                position = new Vector4(player.Position.X, player.Position.Y, player.Position.Z - meters, 1f);
                return true;
            }

            case "right":
            {
                var meters = TryGetFloat(parts, 2, out var value) ? value : 10f;
                position = new Vector4(player.Position.X + meters, player.Position.Y, player.Position.Z, 1f);
                return true;
            }

            case "left":
            {
                var meters = TryGetFloat(parts, 2, out var value) ? value : 10f;
                position = new Vector4(player.Position.X - meters, player.Position.Y, player.Position.Z, 1f);
                return true;
            }

            case "up":
            {
                var meters = TryGetFloat(parts, 2, out var value) ? value : 5f;
                position = new Vector4(player.Position.X, player.Position.Y + meters, player.Position.Z, 1f);
                return true;
            }

            case "down":
            {
                var meters = TryGetFloat(parts, 2, out var value) ? value : 5f;
                position = new Vector4(player.Position.X, player.Position.Y - meters, player.Position.Z, 1f);
                return true;
            }
        }

        if (parts.Length < 4)
            return false;

        if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;

        if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            return false;

        position = new Vector4(x, y, z, 1f);
        return true;
    }

    private static bool TryGetFloat(string[] parts, int index, out float value)
    {
        value = default;
        return parts.Length > index && float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private sealed record CollectionNotificationSample(
        string Title,
        string CollectionText,
        string ItemText,
        int CollectionImageSetId,
        int CollectionImageTint,
        int ItemImageSetId,
        int ItemImageTint);

    private sealed class RawTunneledPacket(byte[] payload) : ISerializablePacket
    {
        public byte[] Serialize()
        {
            return payload;
        }
    }
}
