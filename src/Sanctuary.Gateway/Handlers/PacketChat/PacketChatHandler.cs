using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;
using Sanctuary.Packet.Common.Chat;
using Sanctuary.Packet.Common;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketChatHandler
{
    private static ILogger _logger = null!;
    private static ILogger _chatLogger = null!;
    private static IZoneManager _zoneManager = null!;
    private static IResourceManager _resourceManager = null!;
    private const int DefaultHardPointRespawnSeconds = 5;
    private const float DefaultHardPointLift = 0.0f;
    private static readonly List<CollectionSpawnPointDefinition> DraftCollectionHardPoints = [];

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketChatHandler));
        _chatLogger = loggerFactory.CreateLogger("Chat");

        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!PacketChat.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PacketChat));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PacketChat), packet);

        packet.FromGuid = connection.Player.Guid;
        packet.FromName = connection.Player.Name;

        if (TryHandlePacketInjection(connection, packet))
            return true;

        if (TryHandleCollectionHardPointCommand(connection, packet))
            return true;

        if (TryHandleCollectionProbe(connection, packet))
            return true;

        if (TryHandleToastProbe(connection, packet))
            return true;

        switch (packet.Channel)
        {
            case ChatChannel.Tell:
                {
                    if (_zoneManager.TryGetPlayer(packet.ToName.FullName, out var toPlayer))
                    {
                        _chatLogger.LogInformation("Tell|From: \"{FromName}\" ({FromGuid}), To: \"{ToName}\" ({ToGuid}), Msg: \"{Message}\"",
                            packet.FromName,
                            packet.FromGuid,
                            packet.ToName,
                            toPlayer.Guid,
                            packet.Message
                        );

                        if (!toPlayer.Ignores.Any(x => x.Guid == connection.Player.Guid))
                            toPlayer.SendTunneled(packet);

                        var tellEchoPacket = new TellEchoPacket();

                        tellEchoPacket.Name = packet.ToName;
                        tellEchoPacket.Message = packet.Message;

                        connection.Player.SendTunneled(tellEchoPacket);
                    }
                }
                break;

            case ChatChannel.WorldShout:
                {
                    _chatLogger.LogInformation("WorldShout|From: \"{FromName}\" ({FromGuid}), Msg: \"{Message}\"",
                        packet.FromName,
                        packet.FromGuid,
                        packet.Message
                    );

                    foreach (var zonePlayer in connection.Player.Zone.Players)
                    {
                        if (zonePlayer.Ignores.Any(x => x.Guid == connection.Player.Guid))
                            continue;

                        zonePlayer.SendTunneled(packet);
                    }
                }
                break;

            case ChatChannel.WorldTrade:
            case ChatChannel.WorldLfg:
            case ChatChannel.WorldArea:
            case ChatChannel.WorldMembersOnly:
                {
                    _chatLogger.LogInformation("{Channel}|Area: {AreaNameId}, From: \"{FromName}\" ({FromGuid}), Msg: \"{Message}\"",
                        packet.Channel,
                        packet.AreaNameId,
                        packet.FromName,
                        packet.FromGuid,
                        packet.Message
                    );

                    connection.Player.SendTunneled(packet);

                    foreach (var visiblePlayer in connection.Player.VisiblePlayers)
                    {
                        if (visiblePlayer.Value.ChatChannelStatus.TryGetValue(packet.Channel, out var channelStatus) && !channelStatus)
                            continue;

                        if (visiblePlayer.Value.Ignores.Any(x => x.Guid == connection.Player.Guid))
                            continue;

                        visiblePlayer.Value.SendTunneled(packet);
                    }
                }
                break;

            default:
                {
                    _chatLogger.LogInformation("{Channel}|From: \"{FromName}\" ({FromGuid}), Msg: \"{Message}\"",
                        packet.Channel,
                        packet.FromName,
                        packet.FromGuid,
                        packet.Message
                    );

                    connection.Player.SendTunneled(packet);

                    foreach (var visiblePlayer in connection.Player.VisiblePlayers)
                    {
                        if (visiblePlayer.Value.Ignores.Any(x => x.Guid == connection.Player.Guid))
                            continue;

                        visiblePlayer.Value.SendTunneled(packet);
                    }
                }
                break;
        }

        return true;
    }

    private static bool TryHandleCollectionHardPointCommand(GatewayConnection connection, PacketChat packet)
    {
        if (packet.Message is null)
            return false;

        var message = packet.Message.Trim();
        var isCollectCommand = message.StartsWith("collect", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("/collect", StringComparison.OrdinalIgnoreCase);

        if (!isCollectCommand)
            return false;

        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var command = parts[0].TrimStart('/');

        if (!command.Equals("collect", StringComparison.OrdinalIgnoreCase))
            return false;

        if (connection.Player.Zone is not StartingZone zone)
        {
            _logger.LogInformation("Collection hard-point commands only work in the starting zone.");
            return true;
        }

        var action = parts.Length > 1 ? parts[1].ToLowerInvariant() : "help";

        switch (action)
        {
            case "stamp":
            case "place":
                {
                    var nodeKey = parts.Length > 2 ? parts[2].ToLowerInvariant() : "flowers";
                    var respawnSeconds = parts.Length > 3 && int.TryParse(parts[3], out var parsedRespawnSeconds)
                        ? Math.Clamp(parsedRespawnSeconds, 5, 600)
                        : DefaultHardPointRespawnSeconds;
                    var lift = parts.Length > 4 && float.TryParse(parts[4], out var parsedLift)
                        ? Math.Clamp(parsedLift, -5.0f, 5.0f)
                        : DefaultHardPointLift;

                    StampCollectionHardPoint(zone, connection, nodeKey, respawnSeconds, lift);
                    return true;
                }

            case "hard":
                HandleCollectionHardSubcommand(zone, connection, parts);
                return true;

            case "undo":
                UndoCollectionHardPoint(zone);
                return true;

            case "clear":
                {
                    var pointCount = DraftCollectionHardPoints.Count;
                    DraftCollectionHardPoints.Clear();
                    var nodeCount = zone.ClearCollectionSpawnRegionNodes();
                    _logger.LogInformation("Cleared draft collection hard points from chat. Points={points} Nodes={nodes}", pointCount, nodeCount);
                    return true;
                }

            case "list":
                LogDraftCollectionHardPoints();
                return true;

            case "save":
                {
                    var regionKey = parts.Length > 2 ? parts[2] : $"hard-points-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                    var activeCount = parts.Length > 3 && int.TryParse(parts[3], out var parsedActiveCount)
                        ? Math.Clamp(parsedActiveCount, 1, DraftCollectionHardPoints.Count)
                        : DraftCollectionHardPoints.Count;
                    SaveCollectionHardPoints(regionKey, activeCount);
                    _resourceManager.CollectionSpawnRegions.Load(ResourceManager.CollectionSpawnRegionsFile);
                    _logger.LogInformation("Saved collection hard points from chat. Region={region} Points={points} ActiveCount={activeCount}", regionKey, DraftCollectionHardPoints.Count, activeCount);
                    return true;
                }

            default:
                _logger.LogInformation("Collection commands: /collect stamp <nodeKey> [respawn] [lift], /collect undo, /collect clear, /collect list, /collect save <regionKey> [activeCount]");
                return true;
        }
    }

    private static void HandleCollectionHardSubcommand(StartingZone zone, GatewayConnection connection, string[] parts)
    {
        var action = parts.Length > 2 ? parts[2].ToLowerInvariant() : "list";

        switch (action)
        {
            case "add":
            case "place":
                {
                    var nodeKey = parts.Length > 3 ? parts[3].ToLowerInvariant() : "flowers";
                    var respawnSeconds = parts.Length > 4 && int.TryParse(parts[4], out var parsedRespawnSeconds)
                        ? Math.Clamp(parsedRespawnSeconds, 5, 600)
                        : DefaultHardPointRespawnSeconds;
                    var lift = parts.Length > 5 && float.TryParse(parts[5], out var parsedLift)
                        ? Math.Clamp(parsedLift, -5.0f, 5.0f)
                        : DefaultHardPointLift;

                    StampCollectionHardPoint(zone, connection, nodeKey, respawnSeconds, lift);
                    return;
                }

            case "undo":
                UndoCollectionHardPoint(zone);
                return;

            case "clear":
                DraftCollectionHardPoints.Clear();
                zone.ClearCollectionSpawnRegionNodes();
                _logger.LogInformation("Cleared draft collection hard points from chat.");
                return;

            case "list":
                LogDraftCollectionHardPoints();
                return;

            case "save":
                {
                    var regionKey = parts.Length > 3 ? parts[3] : $"hard-points-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                    var activeCount = parts.Length > 4 && int.TryParse(parts[4], out var parsedActiveCount)
                        ? Math.Clamp(parsedActiveCount, 1, DraftCollectionHardPoints.Count)
                        : DraftCollectionHardPoints.Count;
                    SaveCollectionHardPoints(regionKey, activeCount);
                    _resourceManager.CollectionSpawnRegions.Load(ResourceManager.CollectionSpawnRegionsFile);
                    _logger.LogInformation("Saved collection hard points from chat. Region={region} Points={points} ActiveCount={activeCount}", regionKey, DraftCollectionHardPoints.Count, activeCount);
                    return;
                }

            default:
                _logger.LogInformation("Collection hard commands: /collect hard add <nodeKey> [respawn] [lift], undo, clear, list, save <regionKey> [activeCount]");
                return;
        }
    }

    private static void StampCollectionHardPoint(StartingZone zone, GatewayConnection connection, string nodeKey, int respawnSeconds, float lift)
    {
        var position = GetPlacementPosition(connection.Player.Position, connection.Player.Rotation, lift);
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
            _logger.LogInformation("Failed to stamp collection hard point from chat. Unknown node={node}", nodeKey);
            return;
        }

        DraftCollectionHardPoints.Add(point);

        _logger.LogInformation(
            "Stamped collection hard point from chat. Index={index} Node={node} Position=({x}, {y}, {z}) RespawnSeconds={respawnSeconds}",
            DraftCollectionHardPoints.Count,
            point.NodeKey,
            point.X,
            point.Y,
            point.Z,
            point.RespawnSeconds);
    }

    private static Vector4 GetPlacementPosition(Vector4 playerPosition, Quaternion playerRotation, float lift)
    {
        return playerPosition + new Vector4(0, lift, 0, 0);
    }

    private static void UndoCollectionHardPoint(StartingZone zone)
    {
        if (DraftCollectionHardPoints.Count == 0)
        {
            _logger.LogInformation("No draft collection hard points to undo.");
            return;
        }

        var removed = DraftCollectionHardPoints[^1];
        DraftCollectionHardPoints.RemoveAt(DraftCollectionHardPoints.Count - 1);
        zone.ClearCollectionSpawnRegionNodes();

        foreach (var point in DraftCollectionHardPoints)
            zone.SpawnCollectionHardPoint(new Vector4(point.X, point.Y, point.Z, 1), point.NodeKey, point.RespawnSeconds);

        _logger.LogInformation(
            "Undid collection hard point from chat. Removed Node={node} Position=({x}, {y}, {z}) Remaining={remaining}",
            removed.NodeKey,
            removed.X,
            removed.Y,
            removed.Z,
            DraftCollectionHardPoints.Count);
    }

    private static void LogDraftCollectionHardPoints()
    {
        _logger.LogInformation("Draft collection hard points: {count}", DraftCollectionHardPoints.Count);

        for (var i = 0; i < DraftCollectionHardPoints.Count; i++)
        {
            var point = DraftCollectionHardPoints[i];
            _logger.LogInformation("#{index} {node} ({x}, {y}, {z}) respawn={respawn}", i + 1, point.NodeKey, point.X, point.Y, point.Z, point.RespawnSeconds);
        }
    }

    private static void SaveCollectionHardPoints(string regionKey, int activeCount)
    {
        if (DraftCollectionHardPoints.Count == 0)
        {
            _logger.LogInformation("No draft collection hard points to save.");
            return;
        }

        var filePath = ResourceManager.CollectionSpawnRegionsFile;
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        var regions = File.Exists(filePath)
            ? JsonSerializer.Deserialize<List<CollectionSpawnRegionDefinition>>(File.ReadAllText(filePath), options) ?? []
            : [];

        var center = DraftCollectionHardPoints[0];
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
            NodeKeys = DraftCollectionHardPoints
                .Select(x => x.NodeKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Points = DraftCollectionHardPoints.ToArray()
        };

        regions.RemoveAll(x => string.Equals(x.Key, regionKey, StringComparison.OrdinalIgnoreCase));
        regions.Add(savedRegion);

        File.WriteAllText(filePath, JsonSerializer.Serialize(regions, options));
    }

    private static bool TryHandleCollectionProbe(GatewayConnection connection, PacketChat packet)
    {
        if (packet.Message is null || !packet.Message.StartsWith("collection", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = packet.Message.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var variant = parts.Length > 1 ? parts[1].ToLowerInvariant() : "list";
        var collectionId = parts.Length > 2 && int.TryParse(parts[2], out var parsedCollectionId)
            ? parsedCollectionId
            : 100001;
        var itemDefinitionId = parts.Length > 3 && int.TryParse(parts[3], out var parsedItemDefinitionId)
            ? parsedItemDefinitionId
            : 3106;

        switch (variant)
        {
            case "list":
                _logger.LogInformation("Collection probe variants: start1, start2, start3, add1, add2, add3, remove1.");
                break;

            case "start1":
                SendCollectionStartProbe(connection, collectionId, itemDefinitionId, 1);
                break;

            case "start2":
                SendCollectionStartProbe(connection, collectionId, itemDefinitionId, 2);
                break;

            case "start3":
                SendCollectionStartProbe(connection, collectionId, itemDefinitionId, 3);
                break;

            case "add1":
                SendCollectionAddEntryProbe(connection, collectionId, itemDefinitionId, 1);
                break;

            case "add2":
                SendCollectionAddEntryProbe(connection, collectionId, itemDefinitionId, 2);
                break;

            case "add3":
                SendCollectionAddEntryProbe(connection, collectionId, itemDefinitionId, 3);
                break;

            case "remove1":
                SendCollectionRemoveEntryProbe(connection, collectionId, itemDefinitionId);
                break;

            default:
                _logger.LogInformation("Unknown collection probe variant. Variant={variant}", variant);
                break;
        }

        _logger.LogInformation("Sent collection probe. Variant={variant} Collection={collection} Item={item}",
            variant,
            collectionId,
            itemDefinitionId);

        return true;
    }

    private static void SendCollectionStartProbe(
        GatewayConnection connection,
        int collectionId,
        int itemDefinitionId,
        int shape)
    {
        using var writer = new PacketWriter();

        writer.Write((short)38);
        writer.Write((short)8);
        writer.Write(collectionId);

        if (shape >= 2)
        {
            writer.Write(3); // Flora category.
            writer.Write(92821); // Sunflowers-ish string id candidate.
            writer.Write(43804); // "This item can be added to the Sunflowers Collection" candidate.
            writer.Write(607); // Sunflower icon.
        }

        if (shape >= 3)
        {
            writer.Write(1);
            writer.Write(itemDefinitionId);
            writer.Write(0);
            writer.Write(false);
        }

        connection.SendTunneled(new RawTunneledPacket(writer.Buffer));
    }

    private static void SendCollectionAddEntryProbe(
        GatewayConnection connection,
        int collectionId,
        int itemDefinitionId,
        int shape)
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

        connection.SendTunneled(new RawTunneledPacket(writer.Buffer));
    }

    private static void SendCollectionRemoveEntryProbe(GatewayConnection connection, int collectionId, int itemDefinitionId)
    {
        using var writer = new PacketWriter();

        writer.Write((short)38);
        writer.Write((short)11);
        writer.Write(collectionId);
        writer.Write(itemDefinitionId);

        connection.SendTunneled(new RawTunneledPacket(writer.Buffer));
    }

    private static bool TryHandlePacketInjection(GatewayConnection connection, PacketChat packet)
    {
        if (packet.Message is null)
            return false;

        var isInjection = packet.Message.StartsWith("inject", StringComparison.OrdinalIgnoreCase)
            || packet.Message.StartsWith("packet", StringComparison.OrdinalIgnoreCase);

        if (!isInjection)
            return false;

        var parts = packet.Message.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 3 || !parts[1].Equals("hex", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Packet injection usage: inject hex <packet bytes>");
            return true;
        }

        var hex = new string(parts[2].Where(Uri.IsHexDigit).ToArray());

        if (hex.Length == 0 || hex.Length % 2 != 0)
        {
            _logger.LogWarning("Packet injection rejected invalid hex. Length={length}", hex.Length);
            return true;
        }

        try
        {
            var payload = Convert.FromHexString(hex);
            connection.SendTunneled(new RawTunneledPacket(payload));

            _logger.LogInformation("Injected raw tunneled packet. Bytes={count} Hex={hex}", payload.Length, hex);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Packet injection rejected malformed hex.");
        }

        return true;
    }

    private static bool TryHandleToastProbe(GatewayConnection connection, PacketChat packet)
    {
        if (packet.Message is null)
            return false;

        var isSlashToast = packet.Message.StartsWith("/toast", StringComparison.OrdinalIgnoreCase);
        var isPlainToast = packet.Message.StartsWith("toast", StringComparison.OrdinalIgnoreCase);

        if (!isSlashToast && !isPlainToast)
            return false;

        var parts = packet.Message.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var variant = parts.Length > 1 ? parts[1].ToLowerInvariant() : "list";
        var itemDefinitionId = parts.Length > 2 && int.TryParse(parts[2], out var parsedItemDefinitionId)
            ? parsedItemDefinitionId
            : 3106;

        if (!_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition))
        {
            _logger.LogWarning("Toast probe requested unknown item definition. {definition}", itemDefinitionId);
            return true;
        }

        switch (variant)
        {
            case "list":
                _logger.LogInformation("Toast probe variants: reward, reward2, reward3, rewardbundle, loot, lootdot, lootraw, item, itemdot, system, systemdot.");
                break;

            case "reward":
                SendRewardProbe(connection, itemDefinition.Id, 1, itemDefinition.Icon.TintId);
                break;

            case "reward2":
                SendRewardProbe(connection, itemDefinition.Id, itemDefinition.Icon.TintId, 1);
                break;

            case "reward3":
                SendRewardProbe(connection, itemDefinition.Icon.Id, itemDefinition.Id, itemDefinition.Icon.TintId);
                break;

            case "rewardbundle":
                SendRewardBundleProbe(connection, itemDefinition);
                break;

            case "loot":
                SendLootTextProbe(connection, "Ui:ShowLootText", itemDefinition);
                break;

            case "lootdot":
                SendLootTextProbe(connection, "Ui.ShowLootText", itemDefinition);
                break;

            case "lootraw":
                SendRawScriptProbe(connection, "Ui.ShowLootText");
                break;

            case "item":
                SendItemReceivedProbe(connection, "NotificationHandler:ItemReceived", itemDefinition);
                break;

            case "itemdot":
                SendItemReceivedProbe(connection, "NotificationHandler.ItemReceived", itemDefinition);
                break;

            case "system":
                SendSystemMessageProbe(connection, "NotificationHandler:SystemMessage", itemDefinition);
                break;

            case "systemdot":
                SendSystemMessageProbe(connection, "NotificationHandler.SystemMessage", itemDefinition);
                break;

            case "hud":
                SendStringPacketProbe(connection, 35, 64, $"HUD collected item {itemDefinition.Id}");
                break;

            case "popup":
                SendStringPacketProbe(connection, 35, 70, $"Popup collected item {itemDefinition.Id}");
                break;

            case "notify":
                SendStringPacketProbe(connection, 38, 23, $"Notify collected item {itemDefinition.Id}");
                break;

            case "uimsg":
                SendByteStringPacketProbe(connection, 47, 15, $"UI collected item {itemDefinition.Id}");
                break;

            case "announce":
                SendAnnouncementProbe(connection);
                break;

            case "order":
                SendOrderResponseProbe(connection);
                break;

            default:
                _logger.LogInformation("Unknown toast probe variant. Variant={variant}", variant);
                break;
        }

        _logger.LogInformation("Sent toast probe. Variant={variant} Item={item} Icon={icon} Tint={tint}",
            variant,
            itemDefinition.Id,
            itemDefinition.Icon.Id,
            itemDefinition.Icon.TintId);

        return true;
    }

    private static void SendRewardProbe(GatewayConnection connection, int first, int second, int third)
    {
        connection.SendTunneled(new RewardNonBundledItemPacket
        {
            ItemDefinitionId = first,
            Count = second,
            Tint = third
        });
    }

    private static void SendRewardBundleProbe(GatewayConnection connection, ClientItemDefinition itemDefinition)
    {
        var probeId = Environment.TickCount & int.MaxValue;

        connection.SendTunneled(new RewardBundlePacket
        {
            SourceGuid = connection.Player.Guid ^ (uint)probeId,
            PlayerGuid = connection.Player.Guid,
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
    }

    private static void SendLootTextProbe(GatewayConnection connection, string script, ClientItemDefinition itemDefinition)
    {
        connection.SendTunneled(new ExecuteScriptWithStringParamsPacket
        {
            Script = script,
            Params =
            [
                itemDefinition.Icon.Id,
                1,
                0,
                1,
                itemDefinition.Icon.TintId
            ],
            StringParams =
            [
                $"Collected item {itemDefinition.Id}",
                string.Empty,
                "0x00ffff"
            ]
        });
    }

    private static void SendItemReceivedProbe(GatewayConnection connection, string script, ClientItemDefinition itemDefinition)
    {
        connection.SendTunneled(new ExecuteScriptWithStringParamsPacket
        {
            Script = script,
            Params =
            [
                itemDefinition.Icon.Id,
                1,
                0,
                1,
                itemDefinition.Icon.TintId
            ],
            StringParams =
            [
                $"Collected item {itemDefinition.Id}",
                string.Empty,
                "0x00ffff"
            ]
        });
    }

    private static void SendSystemMessageProbe(GatewayConnection connection, string script, ClientItemDefinition itemDefinition)
    {
        connection.SendTunneled(new ExecuteScriptWithStringParamsPacket
        {
            Script = script,
            Params =
            [
                0,
                5
            ],
            StringParams =
            [
                $"Collected item {itemDefinition.Id}",
                "0xCCCCFF"
            ]
        });
    }

    private static void SendRawScriptProbe(GatewayConnection connection, string functionName)
    {
        connection.SendTunneled(new ExecuteScriptPacket
        {
            Script = $@"{functionName}(""Collected item 3106"", """", 1, 1, 0, true, 0, ""0x00ffff"")"
        });
    }

    private static void SendStringPacketProbe(GatewayConnection connection, short opCode, short subOpCode, string message)
    {
        using var writer = new Sanctuary.Core.IO.PacketWriter();

        writer.Write(opCode);
        writer.Write(subOpCode);
        writer.Write(message);

        connection.SendTunneled(new RawTunneledPacket(writer.Buffer));
    }

    private static void SendByteStringPacketProbe(GatewayConnection connection, short opCode, byte subOpCode, string message)
    {
        using var writer = new Sanctuary.Core.IO.PacketWriter();

        writer.Write(opCode);
        writer.Write(subOpCode);
        writer.Write(message);

        connection.SendTunneled(new RawTunneledPacket(writer.Buffer));
    }

    private static void SendAnnouncementProbe(GatewayConnection connection)
    {
        using var writer = new Sanctuary.Core.IO.PacketWriter();

        writer.Write((short)193);
        writer.Write((byte)2);
        writer.Write(1);
        writer.Write(1);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(string.Empty);
        writer.Write(0);
        writer.Write(0);
        writer.Write("Sanctuary test announcement");
        writer.Write("Collected item probe");

        connection.SendTunneled(new RawTunneledPacket(writer.Buffer));
    }

    private static void SendOrderResponseProbe(GatewayConnection connection)
    {
        connection.SendTunneled(new PacketInGamePurchasePlaceOrderResponse
        {
            OrderTrackingId = Environment.TickCount & int.MaxValue,
            Result = 1,
            OrderId = "toast-probe",
            Discount = 0,
            Total = 0
        });
    }

    private sealed class RawTunneledPacket(byte[] payload) : ISerializablePacket
    {
        public byte[] Serialize()
        {
            return payload;
        }
    }
}
