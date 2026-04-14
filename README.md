# Zone Lock Challenge — Stardew Valley SMAPI Mod

A multiplayer-compatible challenge mod that locks all zones except the farm. Unlock them permanently via bundles (gold + items) or buy daily tickets for temporary access.

## How It Works

- **At game start**, all areas except the farm are locked. Walking into a locked zone warps you back.
- **Press K** (configurable) anywhere to open the **Zone Control Board** menu.
- **Permanent unlocks** cost gold and items — once bought, the zone stays open forever.
- **Ticket zones** (like Pelican Town) require buying a daily ticket each in-game day.
- **Prerequisites**: some zones require others to be unlocked first (e.g., Mines require Mountain).
- **Multiplayer**: zone unlocks are shared across all players. Any player can buy unlocks. The host's save stores all data.

## Default Zone Setup

| Zone              | Type      | Gold Cost | Items Required                | Requires     |
|-------------------|-----------|-----------|-------------------------------|--------------|
| Backwoods         | Permanent | 1,000g    | —                             | —            |
| Bus Stop          | Permanent | 2,000g    | —                             | —            |
| Cindersap Forest  | Permanent | 5,000g    | —                             | —            |
| Pelican Town      | **Ticket**| 5,000g/day| —                             | —            |
| The Beach         | Permanent | 7,500g    | —                             | —            |
| The Mountain      | Permanent | 10,000g   | 100 Wood, 100 Stone           | —            |
| The Mines         | Permanent | 15,000g   | 5 Copper Bars                 | Mountain     |
| Railroad & Spa    | Permanent | 12,000g   | —                             | Mountain     |
| Calico Desert     | Permanent | 25,000g   | 5 Iridium Bars                | Bus Stop     |
| Ginger Island     | Permanent | 50,000g   | 10 Iridium Bars, 5 Batteries  | Beach        |

All of these are fully configurable in `config.json`.

## Installation

### Prerequisites
- [Stardew Valley](https://store.steampowered.com/app/413150/Stardew_Valley/) (Steam, v1.6+)
- [SMAPI](https://smapi.io/) (v4.0+)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for building)

### Building the Mod

1. Install the .NET 8.0 SDK if you haven't already.
2. Clone or download this folder (`ZoneLockChallenge/`).
3. Open a terminal in the folder and run:
   ```
   dotnet build --configuration Release
   ```
4. The build output will automatically be placed in your Stardew Valley `Mods/ZoneLockChallenge/` folder (the SMAPI mod build config handles this).
5. If auto-deploy doesn't work, manually copy the `bin/Release/net8.0/` contents + `manifest.json` into `Stardew Valley/Mods/ZoneLockChallenge/`.

### Multiplayer Setup

**All players** need the mod installed with the same `config.json`. The host's save stores the unlock state. Farmhands receive synced state automatically.

Easiest approach: have one person set up the mod, then share the entire `ZoneLockChallenge` folder with your friends.

## Configuration

After first run, a `config.json` is generated in the mod folder. You can edit:

- **`OpenMenuKey`**: Keybind to open the Zone Board (default: `K`). Uses [SMAPI button names](https://stardewvalleywiki.com/Modding:Player_Guide/Key_Bindings).
- **`ShowBlockedMessage`**: Whether to show a HUD message when blocked (default: `true`).
- **`Zones`**: The full list of zone definitions. Each zone has:
  - `ZoneId`: Unique identifier (used in save data).
  - `DisplayName`: Shown in the menu.
  - `Description`: Flavour text.
  - `UnlockType`: `"permanent"` or `"ticket"`.
  - `MoneyCost`: Gold required.
  - `Items`: List of `{ ItemId, DisplayName, Count }`. Uses [qualified item IDs](https://stardewvalleywiki.com/Modding:Common_data_field_types#Item_ID).
  - `LocationNames`: Exact game location names in this zone.
  - `LocationPrefixes`: Prefix matches (e.g., `"UndergroundMine"` matches all mine floors).
  - `RequiresZone`: Another `ZoneId` that must be permanently unlocked first.

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
| Ancient Fruit   | (O)454       |

### Adding Custom Zones

You can add new zones or modify existing ones in `config.json`. For example, to make the Beach a ticket zone costing 3,000g:

```json
{
    "ZoneId": "Beach",
    "DisplayName": "The Beach",
    "Description": "Sandy shores and Willy's shop.",
    "UnlockType": "ticket",
    "MoneyCost": 3000,
    "Items": [],
    "LocationNames": ["Beach", "FishShop", "ElliottHouse"],
    "LocationPrefixes": [],
    "RequiresZone": null
}
```

## Troubleshooting

- **"Zone Board doesn't open"**: Make sure no other menu is open. Check the SMAPI console for key binding conflicts.
- **"I'm stuck in a locked zone"**: The mod warps you back to the farm. If somehow stuck, use SMAPI console: `debug warp Farm 64 15`.
- **"Items aren't being detected"**: Make sure you're using qualified item IDs with the `(O)` prefix. Check the wiki for correct IDs.
- **"Farmhand can't buy unlocks"**: The purchase request goes to the host. Make sure the host is online and has the same mod version.

## Known Limitations

- Festival warps may bypass the zone lock (festivals use special warp logic). Consider this a feature — your farmers can attend festivals in locked zones!
- Warp totems and the return scepter are caught by the warp interceptor.
- If you remove a zone from the config after unlocking it, locations in that zone become freely accessible (since they no longer match any zone definition).

## License

Free to use, modify, and share. Made for a challenge run between friends.
