# Zone Lock Challenge — Stardew Valley SMAPI Mod

A multiplayer-compatible challenge mod that locks all zones except the farm. Unlock them permanently via bundles (gold + items) or buy daily tickets for temporary access. Designed for co-op challenge runs.

## How It Works

- **At game start**, all areas except the farm are locked. Walking into a locked zone warps you back.
- **Zone plates** appear in the world at configurable locations. Walk up to a plate and interact with it to open the purchase menu for that zone.
- **Press K** (configurable) anywhere to open the **Zone Overview** menu (read-only view of all zones).
- **Permanent unlocks** cost gold and items — once bought, the zone stays open forever. The plate disappears after purchase.
- **Ticket zones** (like Pelican Town) require buying a daily ticket each in-game day.
- **Prerequisites**: some zones require others to be unlocked first, and some require a collective skill level across all players.
- **Multiplayer**: zone unlocks are shared across all players. Any player can buy unlocks. The host's save stores all data and syncs to farmhands automatically.
- **Festivals**: on festival days all zone locks are suspended so every player can attend.

## Features

### Core
- Zone locking with warp-back for unauthorized entry
- Permanent unlocks and daily ticket system
- Zone prerequisite chains (e.g. Mines requires Mountain)
- Collective skill requirements (e.g. Mines requires collective Mining level 5)
- Cost scaling — each zone unlocked increases the price of remaining zones (configurable percentage)
- Full multiplayer sync (host-authoritative, farmhands send purchase requests)
- **Purchase notifications** — when any player unlocks a zone or completes a bundle, all players see a HUD notification (e.g. "Player X unlocked The Beach!")
- **Group unlock** — in multiplayer, players can pool gold toward a permanent zone unlock instead of one person paying the full cost. A "Contribute Gold" button and progress bar appear for locked permanent zones. The zone auto-unlocks when the pooled total meets the cost (the contributing player who pushes it over must also have any required items)

### Mine Floor Gating
- Every 25 mine levels (25, 50, 75, 100) is gated by the group's collective Mining skill level
- Default thresholds: Floor 25 → Mining 3, Floor 50 → Mining 5, Floor 75 → Mining 8, Floor 100 → Mining 10
- Fully configurable in `config.json` or in-game via the zone editor (when editing the Mine zone)
- Players who enter a gated floor are warped back with a message showing current vs required level

### Beach Travel Bypass
- A Mountain ↔ Beach warp sign appears once the Beach zone is unlocked, letting players travel between the beach and mountain without going through town
- Configurable sign positions in `config.json` via `BeachMinecart`
- Optional secondary bypass (`SecondaryBeachBypass`) — disabled by default, lets hosts add a second warp pair between the beach and any other location

### Friendship Decay Prevention
- NPC friendship points never decrease overnight (enabled by default via `PreventFriendshipDecay`)
- In-day friendship changes from gifts, events, and dialogue still apply normally
- Designed for zone-lock challenge runs where players rarely see most NPCs

### Custom Bundles (Host Only)
- **Create new bundles** from the Zone Overview menu — click "+ New Bundle" at the bottom of the sidebar
- **Edit existing bundles** by clicking "Edit Bundle" in the bundle detail panel
- Set a display name, description, gold cost, required items, and reward items
- **Item search by name** — when adding items to a bundle, type to search all game items by name (no need to know item IDs). Results show item icons and are sorted by relevance
- **Delete bundles** from the edit menu
- Any player can complete a bundle by paying its gold/item cost. Rewards are given on completion
- Bundles are synced to all players and stored in the host's save data

### In-Game Zone Editing (Host Only)
- **Edit Zone**: change gold costs, item requirements, and item rewards from the menu (no config file editing needed)
- **Move Plate**: click "Move Plate" then click any tile in the world to reposition a zone's plate
- **Reorder Zones**: up/down arrows in the sidebar let the host reorder how zones appear in the menu
- **Mine Gate Editing**: when editing the Mine zone, adjust or add/remove mine floor gates and their required mining levels
- All changes are saved to the host's save data, override `config.json` defaults, and sync to all farmhands

### Item Rewards
- Zones can grant item rewards upon purchase (e.g. a fishing rod when unlocking the Beach)
- Configurable via the in-game zone editor or `config.json` overrides
- Items are added to inventory, or dropped as debris if inventory is full

## Default Zone Setup

| Zone              | Type      | Gold Cost | Items Required           | Rewards         | Requires     | Skill Req         |
|-------------------|-----------|-----------|--------------------------|-----------------|--------------|-------------------|
| Backwoods         | Permanent | 500g      | —                        | Training Rod    | —            | —                 |
| Bus Stop          | Permanent | 1,000g    | —                        | —               | —            | —                 |
| Pelican Town      | **Ticket**| 2,000g/day| —                        | —               | —            | —                 |
| Cindersap Forest  | Permanent | 3,000g    | —                        | —               | —            | —                 |
| The Beach         | Permanent | 5,000g    | —                        | Fiberglass Rod  | —            | —                 |
| The Mountain      | Permanent | 8,000g    | 50 Wood, 50 Stone        | —               | —            | —                 |
| The Mines         | Permanent | 12,000g   | 5 Copper Bars            | —               | Mountain     | Mining 5 (coll.)  |
| Railroad & Spa    | Permanent | 10,000g   | —                        | —               | Mountain     | —                 |
| Calico Desert     | Permanent | 20,000g   | 5 Gold Bars              | —               | Bus Stop     | —                 |
| Ginger Island     | Permanent | 40,000g   | 5 Iridium Bars, 3 Batteries | —            | Beach        | —                 |

All of these are fully configurable in `config.json`. The host can also override costs, items, and rewards in-game via the zone editor.

### Default Mine Floor Gates

| Floor | Required Collective Mining Level |
|-------|----------------------------------|
| 25    | 3                                |
| 50    | 5                                |
| 75    | 8                                |
| 100   | 10                               |

## Installation

### Prerequisites
- [Stardew Valley](https://store.steampowered.com/app/413150/Stardew_Valley/) (Steam, v1.6+)
- [SMAPI](https://smapi.io/) (v4.0+)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for building)

### Quick Install (Windows)

Double-click **INSTALL.bat**. It will:
1. Find your Stardew Valley installation (or ask you for the path)
2. Check for / install the .NET 8.0 SDK
3. Build the mod
4. Copy files to `Stardew Valley/Mods/ZoneLockChallenge/`

### Manual Build

1. Install the .NET 8.0 SDK if you haven't already.
2. Clone or download this folder (`ZoneLockChallenge/`).
3. Open a terminal in the folder and run:
   ```
   dotnet build --configuration Release
   ```
4. The build output will automatically be placed in your Stardew Valley `Mods/ZoneLockChallenge/` folder (the SMAPI mod build config handles this).
5. If auto-deploy doesn't work, manually copy the `bin/Release/net8.0/` contents + `manifest.json` into `Stardew Valley/Mods/ZoneLockChallenge/`.

### Multiplayer Setup

**All players** need the mod installed with the same `config.json`. The host's save stores the unlock state (including any in-game overrides for costs, items, rewards, plate positions, zone order, mine gates, custom bundles, and pooled contributions). Farmhands receive synced state automatically.

Easiest approach: have one person set up the mod, then share the entire `ZoneLockChallenge` folder with your friends.

## Configuration

After first run, a `config.json` is generated in the mod folder. Key settings:

| Setting                   | Default  | Description                                                      |
|---------------------------|----------|------------------------------------------------------------------|
| `OpenMenuKey`             | `K`      | Keybind to open the Zone Overview menu                           |
| `ShowBlockedMessage`      | `true`   | Show HUD message when blocked from entering a zone               |
| `PreventFriendshipDecay`  | `true`   | Prevent NPC friendship from decreasing overnight                 |
| `CostScalingPercent`      | `10`     | Extra % added to zone cost per already-unlocked zone (0 = off)   |

### Zone Definitions

Each zone in the `Zones` list has:

- `ZoneId`: Unique identifier (used in save data).
- `DisplayName`: Shown in the menu and on plates.
- `BundleName`: Optional alternate name (shown in the detail panel header).
- `Description`: Flavour text.
- `UnlockType`: `"permanent"` or `"ticket"`.
- `MoneyCost`: Gold required.
- `Items`: List of `{ ItemId, DisplayName, Count }`. Uses [qualified item IDs](https://stardewvalleywiki.com/Modding:Common_data_field_types#Item_ID).
- `LocationNames`: Exact game location names in this zone.
- `LocationPrefixes`: Prefix matches (e.g., `"UndergroundMine"` matches all mine floors).
- `RequiresZone`: Another `ZoneId` that must be permanently unlocked first.
- `RequiredSkill` / `RequiredSkillLevel`: Collective skill gate (sum of all players' levels for that skill).
- `Plate`: `{ LocationName, X, Y }` — world tile where the zone's purchase plate appears.

### Mine Level Gates

The `MineLevelGates` list controls mine floor gating:

```json
"MineLevelGates": [
    { "FloorNumber": 25, "RequiredMiningLevel": 3 },
    { "FloorNumber": 50, "RequiredMiningLevel": 5 },
    { "FloorNumber": 75, "RequiredMiningLevel": 8 },
    { "FloorNumber": 100, "RequiredMiningLevel": 10 }
]
```

Set to an empty list `[]` to disable mine gating entirely.

### Beach Minecart

The `BeachMinecart` config controls the Mountain ↔ Beach warp signs:

```json
"BeachMinecart": {
    "Enabled": true,
    "MountainLocation": "Mountain",
    "MountainSignX": 126, "MountainSignY": 12,
    "BeachLocation": "Beach",
    "BeachSignX": 27, "BeachSignY": 4,
    "BeachArrivalX": 26, "BeachArrivalY": 4,
    "MountainArrivalX": 125, "MountainArrivalY": 12
}
```

An optional `SecondaryBeachBypass` with the same structure (but `Enabled: false` by default) can add a second warp pair between the beach and any other location.

### Common Item IDs

| Item            | Qualified ID |
|-----------------|--------------|
| Wood            | (O)388       |
| Stone           | (O)390       |
| Copper Bar      | (O)334       |
| Iron Bar        | (O)335       |
| Gold Bar        | (O)336       |
| Iridium Bar     | (O)337       |
| Battery Pack    | (O)787       |
| Diamond         | (O)72        |
| Prismatic Shard | (O)74        |
| Fishing Rod     | (T)FishingRod |

## Console Commands (SMAPI)

| Command | Description |
|---------|-------------|
| `zlc_moveplate <ZoneId>` | Enter plate placement mode — click any tile in-game to move that zone's plate. Use `zlc_moveplate list` to see zone IDs. Host only. |
| `zlc_unlock <ZoneId>` | Manually unlock a zone permanently (bypasses cost/items). Use `zlc_unlock list` to see all zones and their status. Host only. |
| `zlc_lock <ZoneId>` | Manually re-lock a previously unlocked zone. Use `zlc_lock list` to see all zones and their status. Host only. |

## Troubleshooting

- **"Zone Board doesn't open"**: Make sure no other menu is open. Check the SMAPI console for key binding conflicts.
- **"I'm stuck in a locked zone"**: The mod warps you back to the farm. If somehow stuck, use SMAPI console: `debug warp Farm 64 15`.
- **"Items aren't being detected"**: Make sure you're using qualified item IDs with the `(O)` prefix for objects, `(T)` for tools, etc. Check the wiki for correct IDs.
- **"Farmhand can't buy unlocks"**: The purchase request goes to the host. Make sure the host is online and has the same mod version.
- **"Mine floor gate not working"**: Check that the Mine zone is unlocked first — mine floor gates only apply within the unlocked Mine zone.

## Known Limitations

- Festival warps bypass zone locks intentionally — players can attend all festivals even in locked zones.
- Warp totems and the return scepter are caught by the warp interceptor.
- If you remove a zone from the config after unlocking it, locations in that zone become freely accessible (since they no longer match any zone definition).
- In-game zone overrides (costs, items, rewards, plate positions, zone order, mine gates) are stored in the host's save data. Changing `config.json` only affects defaults — overrides take precedence.

## License

Free to use, modify, and share. Made for a challenge run between friends.
