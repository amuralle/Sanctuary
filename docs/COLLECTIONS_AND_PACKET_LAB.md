# Collections And Packet Lab Notes

This document captures the current collection-node prototype and what is known from local testing.

## Implemented Pieces

- Resource-backed collection node definitions in `src/Resources/CollectionNodes.json`.
- Resource-backed collection spawn regions in `src/Resources/CollectionSpawnRegions.json`.
- Runtime collection node spawning in `StartingZone`.
- Interactable node NPCs with collection reward metadata.
- Inventory grant on interaction.
- Reward bundle toast on interaction.
- Node despawn and respawn after collection.
- Dev-only packet lab service for live testing.
- Login-time mock collection rows in `ClientPcData`.
- A custom store category and bundle test item for mahogany hightops.

## Important Files

- `src/Sanctuary.Gateway/DevPacketLabService.cs`
- `src/Sanctuary.Game/Zones/StartingZone.cs`
- `src/Sanctuary.Gateway/Handlers/BaseCommandPacket/CommandPacketInteractRequestHandler.cs`
- `src/Sanctuary.Packet.Common/ClientPcData.cs`
- `src/Resources/CollectionNodes.json`
- `src/Resources/CollectionSpawnRegions.json`

## Node Definitions

Each node definition can specify:

- `Key`: command/resource key, for example `mushrooms` or `water`.
- `Name`: nameplate text.
- `CollectionId`: collection row id, if known.
- `CollectionEntryIds`: entry ids used by the collection row, if known.
- `ModelId`: base visible model.
- `RareModelId`: optional rare model.
- `Scale`: model scale.
- `PositionW`: optional `Vector4.W` override used for client rendering quirks.
- `CompositeEffectId`: primary effect.
- `ExtraCompositeEffectIds`: additional effects attached to the same node.
- `RareCompositeEffectId`: legacy rare effect field.
- `InteractRange`: cursor/interact range.
- `CursorId`: cursor type. `18` has worked for collection-style interactions.
- `HideNamePlate`: hides the label when true.
- `Rewards`: item definition ids that can be granted from the node.

## Current Node Keys

- `flowers`
- `mushrooms`
- `shells`
- `leaves`
- `junkpile`
- `springs`
- `fossils`
- `crate`
- `water`
- `water-rare`
- `water-invisible`
- `water-particle`
- `water-fishing1`
- `water-fishing2`
- `water-launcher`

The best water visuals found so far are:

- Normal Sparkling Water: model `271`, effect `15381`, plus sparkle `5742`.
- Rare Sparkling Water: model `271`, effect `15382`, plus rare sparkle `5740`.

`water-particle` model `1705` renders as a visible debug-colored rectangle and should not be used for final placement.

## Known Collection Mappings

### Briarwood Mushrooms

- Collection row id: `17054`
- Collection entry ids: `41..48`
- Reward item definitions: `11081..11088`
- Login-time row seeding works.

### Water

- Reward item definitions: `3911..3958`
- `CollectionId = 41125` is a temporary guess and appears to display as King Seasnail. Do not treat it as final.
- The correct Sparkling Water collection id still needs to be found.

## Packet Lab Commands

Run commands from the Sanctuary repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Send-PacketLabCommand.ps1 "COMMAND HERE"
```

### Collection Spawning

Reload configured regions:

```powershell
collect reload
```

Clear active spawned collection nodes:

```powershell
collect clear
```

Spawn an ad-hoc region around the player:

```powershell
collect here <nodeKey> [count] [radius] [respawnSeconds]
```

Examples:

```powershell
collect here mushrooms 8 20 5
collect here water 4 12 5
collect here water-rare 2 10 5
```

Spawn one visual probe at the player's position:

```powershell
collect visualself <nodeKey> [lift] [plain|interact|range|cursor|full]
```

The most useful mode while testing is:

```powershell
collect visualself mushrooms 0 full
```

### Placement Mode

Stamp a hard point at the player's current position:

```powershell
collect stamp <nodeKey> [respawnSeconds] [lift]
```

Examples:

```powershell
collect stamp water 5 0
collect stamp water-rare 5 0
collect stamp mushrooms 5 0
```

List draft hard points:

```powershell
collect hard list
```

Undo the last draft hard point:

```powershell
collect hard undo
```

Clear draft hard points:

```powershell
collect hard clear
```

Save draft hard points into `CollectionSpawnRegions.json`:

```powershell
collect hard save <regionKey> [activeCount]
```

Example:

```powershell
collect hard save stillwater-crossing-water 12
```

For development, hard-point spawns currently default to all active and a short respawn so placement can be visually checked.

### Showrooms

Show collection node batches:

```powershell
showroom water fx
showroom base fx
showroom chests fx
showroom models 271,1684,1685 fx
showroom search water 30 fx
```

Clear showroom spawns:

```powershell
showroom clear
```

### Teleport

```powershell
teleport safe
teleport spawn
teleport forward 10
teleport <x> <y> <z>
```

### Collection Packet Probes

These send guessed collection update packets:

```powershell
collection start1 <collectionId> <entryOrItemId>
collection start2 <collectionId> <entryOrItemId>
collection start3 <collectionId> <entryOrItemId>
collection add1 <collectionId> <entryOrItemId>
collection add2 <collectionId> <entryOrItemId>
collection add3 <collectionId> <entryOrItemId>
collection remove1 <collectionId> <entryOrItemId>
```

For Briarwood Mushrooms, the best known ids are:

```powershell
collection add1 17054 41
collection add1 17054 42
```

These packets have not yet caused the already-open Collections panel to tick live.

### Notification Probes

The client contains Lua bytecode for:

- `NotificationHandler:StartCollection`
- `NotificationHandler:UpdateCollection`
- `NotificationHandler:CompleteCollection`

The inferred `UpdateCollection` arguments are:

```text
title, collectionText, collectionImageSetId, collectionImageTint, itemText, itemImageSetId, itemImageTint
```

Packet-lab probes exist:

```powershell
notify update mushroom
notify rawupdate mushroom
notify batch mushroom
notify update water
notify rawupdate water
```

As of this note, these did not show the original top-center collection notification. The direct function names may be correct, but the network packet path or argument marshalling is still wrong.

## Known Working Behavior

- Collection nodes spawn and render.
- Water normal and rare visuals look good.
- Interact cursor appears.
- Interacting grants inventory items.
- Reward bundle toast appears for pickups.
- Node despawn/respawn works.
- Login-time collection rows can show Briarwood Mushrooms progress.

## Known Gaps

- Live update of an already-open Collections panel is not solved.
- Original collection pickup notification is not solved.
- Water collection id is not confirmed.
- Collection completion rewards are not implemented.
- Pickup should eventually consume/unify with collection tracker state rather than only granting inventory.
- Drop tables and regional spawn mapping need real data.

## Packet Log Findings

The imported historical packet logs were scanned for collection update packets:

- `38/8` `ClientUpdatePacketCollectionStart`
- `38/9` `ClientUpdatePacketCollectionRemove`
- `38/10` `ClientUpdatePacketCollectionAddEntry`
- `38/11` `ClientUpdatePacketCollectionRemoveEntry`

No real collection pickup samples were found in those captures. They do contain quest objective update packets and reward/inventory packets, but not the collection pickup transaction we need.

Client string/bytecode inspection did reveal the collection notification function names and the collection data source names:

- `BaseClient.CollectionData`
- `BaseClient.CollectionEntriesData`
- `BaseClient.CollectionData.Reward`
- `HandlerCollectionScreen`
- `NotificationHandler`

The next good investigation path is probably the live data-source mutation path for `BaseClient.CollectionData` and `BaseClient.CollectionEntriesData`, rather than more direct notification calls.
