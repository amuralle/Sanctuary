using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class CommandPacketInteractRequestHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;
    private static readonly Random Random = new();

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(CommandPacketInteractRequestHandler));
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!CommandPacketInteractRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(CommandPacketInteractRequest));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(CommandPacketInteractRequest), packet);

        if (!connection.Player.Zone.TryGetEntity(packet.Guid, out var entity))
            return true;

        entity.OnInteract(connection.Player);

        if (entity is Npc npc && npc.MockCollectionRewardItemDefinitionIds.Count > 0)
        {
            if (!GrantMockCollectionReward(connection, npc))
                return true;

            if (npc.Zone is StartingZone startingZone)
                startingZone.CollectMockCollectionNode(npc);
            else
                npc.Dispose();
        }

        return true;
    }

    private static bool GrantMockCollectionReward(GatewayConnection connection, Npc npc)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);
        var dbCharacter = dbContext.Characters
            .Include(x => x.Items)
            .SingleOrDefault(x => x.Id == characterId);

        if (dbCharacter is null)
        {
            _logger.LogWarning("Mock collection reward requested for missing character. {character}", characterId);
            return false;
        }

        var rewardDefinitionId = ChooseMockCollectionReward(npc, dbCharacter);

        if (!_resourceManager.ClientItemDefinitions.TryGetValue(rewardDefinitionId, out var clientItemDefinition))
        {
            _logger.LogWarning("Mock collection node has unknown reward definition. {definition}", rewardDefinitionId);
            return false;
        }

        var tint = clientItemDefinition.Icon.TintId;
        var dbItem = dbCharacter.Items.SingleOrDefault(i => i.Definition == rewardDefinitionId && i.Tint == tint);

        if (dbItem is not null)
        {
            dbItem.Count += 1;
        }
        else
        {
            var lastItemId = dbContext.Items.Where(i => i.CharacterId == characterId)
                .Select(i => (int?)i.Id)
                .Max() ?? 0;

            dbItem = new DbItem
            {
                Id = lastItemId + 1,
                CharacterId = characterId,
                Definition = rewardDefinitionId,
                Tint = tint,
                Count = 1
            };

            dbCharacter.Items.Add(dbItem);
        }

        dbCharacter.Coins += 1;

        if (dbContext.SaveChanges() <= 0)
        {
            _logger.LogWarning("Failed to save mock collection reward. {definition}", rewardDefinitionId);
            return false;
        }

        var clientItem = connection.Player.Items.SingleOrDefault(x => x.Definition == rewardDefinitionId && x.Tint == tint);
        var addItem = clientItem is null;

        if (clientItem is null)
        {
            clientItem = new ClientItem
            {
                Id = dbItem.Id,
                Definition = dbItem.Definition,
                Tint = dbItem.Tint,
                Count = dbItem.Count
            };

            connection.Player.Items.Add(clientItem);
        }
        else
        {
            clientItem.Count = dbItem.Count;
        }

        SendItemDefinition(connection, clientItemDefinition);

        if (addItem)
            SendItemAdd(connection, clientItem);
        else
            SendItemUpdate(connection, clientItem);

        var collectionEntryId = ChooseMockCollectionEntry(npc, rewardDefinitionId);
        SendCollectionAddEntry(connection, npc.MockCollectionId, collectionEntryId);

        connection.Player.Coins = dbCharacter.Coins;

        connection.SendTunneled(new ClientUpdatePacketCoinCount
        {
            Coins = connection.Player.Coins
        });

        SendCollectionRewardToast(connection, clientItem, clientItemDefinition, npc);

        _logger.LogInformation(
            "Granted mock collection reward. Player={player} Node={node} Item={item} Count={count} Coins={coins}",
            connection.Player.Guid,
            npc.Guid,
            rewardDefinitionId,
            clientItem.Count,
            connection.Player.Coins);

        return true;
    }

    private static void SendItemDefinition(GatewayConnection connection, ClientItemDefinition clientItemDefinition)
    {
        using var writer = new PacketWriter();
        writer.Write(new[] { clientItemDefinition });

        connection.SendTunneled(new PlayerUpdatePacketItemDefinitions
        {
            Payload = writer.Buffer
        });
    }

    private static void SendItemAdd(GatewayConnection connection, ClientItem clientItem)
    {
        using var writer = new PacketWriter();
        clientItem.Serialize(writer);

        connection.SendTunneled(new ClientUpdatePacketItemAdd
        {
            Payload = writer.Buffer
        });
    }

    private static void SendItemUpdate(GatewayConnection connection, ClientItem clientItem)
    {
        connection.SendTunneled(new ClientUpdatePacketItemUpdate
        {
            ItemGuid = clientItem.Id,
            Count = clientItem.Count
        });
    }

    private static void SendCollectionAddEntry(GatewayConnection connection, int collectionId, int itemDefinitionId)
    {
        using var writer = new PacketWriter();

        writer.Write((short)38);
        writer.Write((short)10);
        writer.Write(collectionId > 0 ? collectionId : 100001);
        writer.Write(itemDefinitionId);
        writer.Write(1);

        connection.SendTunneled(new RawTunneledPacket(writer.Buffer));
    }

    private static void SendCollectionRewardToast(
        GatewayConnection connection,
        ClientItem clientItem,
        ClientItemDefinition clientItemDefinition,
        Npc npc)
    {
        var probeId = Environment.TickCount & int.MaxValue;

        connection.SendTunneled(new RewardBundlePacket
        {
            SourceGuid = npc.Guid ^ (uint)probeId,
            PlayerGuid = connection.Player.Guid,
            IconId = clientItemDefinition.Icon.Id,
            NameId = clientItemDefinition.NameId,
            Quantity = 1,
            EntryIconId = clientItemDefinition.Icon.Id,
            EntryNameId = clientItemDefinition.NameId,
            EntryQuantity = 1,
            ItemDefinitionId = clientItem.Definition,
            Tint = clientItem.Tint,
            ItemGuid = probeId,
            EntryUnknown5 = clientItemDefinition.DescriptionId
        });
    }

    private static int ChooseMockCollectionReward(Npc npc, DbCharacter dbCharacter)
    {
        var collectionRewardIds = npc.MockCollectionEntryIds
            .Where(id => _resourceManager.ClientItemDefinitions.ContainsKey(id))
            .ToList();
        var candidateRewardIds = collectionRewardIds.Count > 0
            ? collectionRewardIds
            : npc.MockCollectionRewardItemDefinitionIds;
        var unownedRewardIds = candidateRewardIds
            .Where(rewardId => dbCharacter.Items.All(item => item.Definition != rewardId))
            .ToList();

        var rewardIds = unownedRewardIds.Count > 0
            ? unownedRewardIds
            : candidateRewardIds;

        return rewardIds[Random.Next(rewardIds.Count)];
    }

    private static int ChooseMockCollectionEntry(Npc npc, int rewardDefinitionId)
    {
        if (npc.MockCollectionEntryIds.Count == 0)
            return rewardDefinitionId;

        var rewardIndex = npc.MockCollectionRewardItemDefinitionIds.IndexOf(rewardDefinitionId);

        if (rewardIndex >= 0 && rewardIndex < npc.MockCollectionEntryIds.Count)
            return npc.MockCollectionEntryIds[rewardIndex];

        return npc.MockCollectionEntryIds[Random.Next(npc.MockCollectionEntryIds.Count)];
    }

    private sealed class RawTunneledPacket(byte[] payload) : ISerializablePacket
    {
        public byte[] Serialize() => payload;
    }
}
