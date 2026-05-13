using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace ZoneLockChallenge
{
    public class ModEntry : Mod
    {
        private ModConfig config;
        private ZoneStateManager stateManager;
        private ContentProvider contentProvider;
        private bool isWarpingBack;
        private int warpBackFramesLeft;

        // Track player's last tile position and location (updated before game processes warps)
        private string lastSafeLocationName = "Farm";
        private int lastSafeX = 64;
        private int lastSafeY = 15;

        // Plate rendering: animated bounce
        private float plateAnimTimer;

        // Plate repositioning mode
        private string platePlacementZoneId;

        // Friendship decay prevention: snapshot taken on DayEnding, restored on DayStarted
        private Dictionary<string, int> friendshipSnapshot = new();

        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<ModConfig>();
            contentProvider = new ContentProvider(helper, config);
            stateManager = new ZoneStateManager(helper, Monitor, config, contentProvider);

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.Saving += OnSaving;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.DayEnding += OnDayEnding;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            helper.Events.Player.Warped += OnPlayerWarped;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
            helper.Events.GameLoop.UpdateTicking += OnUpdateTicking;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;

            helper.ConsoleCommands.Add("zlc_moveplate",
                "Move a zone plate to your current cursor tile. Usage: zlc_moveplate <ZoneId>\nUse 'zlc_moveplate list' to see all zone IDs.",
                OnMovePlateCommand);

            helper.ConsoleCommands.Add("zlc_unlock",
                "Manually unlock a zone (host only). Usage: zlc_unlock <ZoneId>\nUse 'zlc_unlock list' to see all zone IDs and their status.",
                OnUnlockCommand);

            helper.ConsoleCommands.Add("zlc_lock",
                "Manually lock a zone (host only). Usage: zlc_lock <ZoneId>\nUse 'zlc_lock list' to see all zone IDs and their status.",
                OnLockCommand);

            Monitor.Log("Zone Lock Challenge loaded. Press " + config.OpenMenuKey + " to view zones. Visit zone plates to purchase.", LogLevel.Info);
        }

        // ── Lifecycle ────────────────────────────────────────────────

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var cpApi = Helper.ModRegistry.GetApi<IContentPatcherAPI>("Pathoschild.ContentPatcher");
            if (cpApi == null)
            {
                Monitor.Log("Content Patcher not found — CP tokens will not be available.", LogLevel.Info);
                return;
            }

            cpApi.RegisterToken(ModManifest, "IsZoneUnlocked", new ZoneUnlockedToken(stateManager));
            cpApi.RegisterToken(ModManifest, "UnlockedZones", new UnlockedZonesToken(stateManager));
            cpApi.RegisterToken(ModManifest, "IsZoneAccessible", new ZoneAccessibleToken(stateManager));
            cpApi.RegisterToken(ModManifest, "UnlockedZoneCount", new UnlockedZoneCountToken(stateManager));
            Monitor.Log("Registered 4 Content Patcher tokens (IsZoneUnlocked, UnlockedZones, IsZoneAccessible, UnlockedZoneCount).", LogLevel.Info);
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            stateManager.LoadState();
            stateManager.RecordStatsSnapshot(Game1.player);
            if (!Context.IsMainPlayer)
                stateManager.RequestSync();

            if (config.PreventFriendshipDecay)
            {
                var loaded = Helper.Data.ReadJsonFile<Dictionary<string, int>>(GetFriendshipFilePath());
                if (loaded != null)
                    friendshipSnapshot = loaded;
            }
        }

        private void OnSaving(object sender, SavingEventArgs e)
        {
            stateManager.SaveState();
            if (config.PreventFriendshipDecay && friendshipSnapshot.Count > 0)
                Helper.Data.WriteJsonFile(GetFriendshipFilePath(), friendshipSnapshot);
        }

        private string GetFriendshipFilePath() =>
            $"data/friendship_{Game1.player.UniqueMultiplayerID}.json";

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            var expired = Context.IsMainPlayer
                ? stateManager.CleanupExpiredTickets()
                : stateManager.GetLocalExpiredTicketZones();
            foreach (var zoneId in expired)
            {
                var zone = stateManager.GetZoneById(zoneId);
                if (zone != null)
                    Game1.addHUDMessage(new HUDMessage($"{zone.DisplayName} ticket expired. Visit the plate to buy a new one.", HUDMessage.error_type));
            }

            // Restore friendship points that decreased overnight (prevents daily decay)
            if (config.PreventFriendshipDecay && friendshipSnapshot.Count > 0)
            {
                int restored = 0;
                foreach (var kvp in friendshipSnapshot)
                {
                    if (Game1.player.friendshipData.ContainsKey(kvp.Key))
                    {
                        var friendship = Game1.player.friendshipData[kvp.Key];
                        if (friendship.Points < kvp.Value)
                        {
                            friendship.Points = kvp.Value;
                            restored++;
                        }
                    }
                }
                if (restored > 0)
                    Monitor.Log($"Restored friendship decay for {restored} NPC(s).", LogLevel.Trace);
                friendshipSnapshot.Clear();
                Helper.Data.WriteJsonFile<Dictionary<string, int>>(GetFriendshipFilePath(), null);
            }
        }

        private void OnDayEnding(object sender, DayEndingEventArgs e)
        {
            stateManager.RecordStatsSnapshot(Game1.player);

            if (!config.PreventFriendshipDecay) return;
            friendshipSnapshot.Clear();
            foreach (var kvp in Game1.player.friendshipData.Pairs)
                friendshipSnapshot[kvp.Key] = kvp.Value.Points;
        }

        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e) { isWarpingBack = false; warpBackFramesLeft = 0; friendshipSnapshot.Clear(); }

        private void OnPeerConnected(object sender, PeerConnectedEventArgs e)
        {
            if (Context.IsMainPlayer) stateManager.BroadcastState();
        }

        // ── Tick handlers ────────────────────────────────────────────

        /// <summary>Fires BEFORE the game update — save position while player is still in their current location.</summary>
        private void OnUpdateTicking(object sender, UpdateTickingEventArgs e)
        {
            if (!Context.IsWorldReady || isWarpingBack || Game1.player == null) return;

            string locName = Game1.currentLocation?.Name ?? "Farm";

            // Only record positions in locations the player legitimately has access to,
            // so a transient stay (e.g. post-death Hospital before block kicks in) doesn't
            // become the "safe" location to warp back to on the next block.
            if (!IsFarmLocation(locName))
            {
                var zone = stateManager.GetZoneForLocation(locName);
                if (zone != null && !stateManager.IsZoneAccessible(zone.ZoneId, Game1.player.UniqueMultiplayerID))
                    return;
            }

            lastSafeLocationName = locName;
            lastSafeX = (int)Game1.player.Tile.X;
            lastSafeY = (int)Game1.player.Tile.Y;
        }

        /// <summary>Fires AFTER the game update — animation timer and warp flag countdown.</summary>
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            plateAnimTimer += (float)(1.0 / 60.0);

            // Count down warp-back frames (wait for return warp to fully complete)
            if (warpBackFramesLeft > 0)
            {
                warpBackFramesLeft--;
                if (warpBackFramesLeft == 0)
                    isWarpingBack = false;
            }
        }

        // ── Warp interception ────────────────────────────────────────

        private void OnPlayerWarped(object sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer || isWarpingBack) return;

            string newLocationName = e.NewLocation.Name;
            string oldLocationName = e.OldLocation.Name;

            if (IsFarmLocation(newLocationName)) return;

            // During an active festival event, allow all warps so players can attend.
            // Using Game1.eventUp instead of isFestivalDay() so the bypass only applies
            // while the festival is actually happening, not the entire 24h calendar day.
            if (Utility.isFestivalDay() && Game1.eventUp)
                return;

            long farmerId = Game1.player.UniqueMultiplayerID;
            var zone = stateManager.GetZoneForLocation(newLocationName);
            if (zone == null) return;

            // Hospital is the death-warp destination. If Town is locked and we'd block
            // entry, the game would re-warp the player back to where they died (still
            // dead), causing a Hospital ↔ death-location loop. Send them home instead.
            if (newLocationName == "Hospital" && !stateManager.IsZoneAccessible(zone.ZoneId, farmerId))
            {
                Monitor.Log($"Hospital is in a locked zone — sending {Game1.player.Name} to the farm to break the death loop.", LogLevel.Info);
                isWarpingBack = true;
                warpBackFramesLeft = 3;
                Game1.warpFarmer("Farm", 64, 15, false);
                return;
            }

            // Mine floor gate check: even if the Mine zone is unlocked, specific floors may be gated
            if (stateManager.IsZoneAccessible(zone.ZoneId, farmerId))
            {
                int mineFloor = ParseMineFloor(newLocationName);
                if (mineFloor >= 0 && !stateManager.IsMineLevelAllowed(mineFloor))
                {
                    int required = stateManager.GetRequiredMiningLevelForFloor(mineFloor);
                    int current = stateManager.GetCollectiveSkillLevel("Mining");
                    Monitor.Log($"Blocked {Game1.player.Name} from mine floor {mineFloor} (need collective Mining {required}, have {current}).", LogLevel.Info);

                    if (config.ShowBlockedMessage)
                        Game1.addHUDMessage(new HUDMessage($"Floor {mineFloor} is gated! Need collective Mining level {required} (have {current}).", HUDMessage.error_type));

                    isWarpingBack = true;
                    warpBackFramesLeft = 3;
                    Game1.warpFarmer(oldLocationName, lastSafeX, lastSafeY, false);
                }
                return;
            }

            Monitor.Log($"Blocked {Game1.player.Name} from entering {newLocationName} (zone: {zone.ZoneId} is locked).", LogLevel.Info);

            if (config.ShowBlockedMessage)
                Game1.addHUDMessage(new HUDMessage($"{zone.DisplayName} is locked! Visit the zone plate to unlock it.", HUDMessage.error_type));

            isWarpingBack = true;
            warpBackFramesLeft = 3; // keep flag up long enough for the return warp to complete

            // Check if old location is safe to return to (e.g. died in mines → hospital blocked → mines also blocked)
            var oldZone = stateManager.GetZoneForLocation(oldLocationName);
            bool oldLocationSafe = IsFarmLocation(oldLocationName)
                || oldZone == null
                || stateManager.IsZoneAccessible(oldZone.ZoneId, farmerId);

            if (oldLocationSafe)
            {
                int warpX = lastSafeX;
                int warpY = lastSafeY;

                // If our tracked position is from a different location (can happen during warp transitions),
                // find the warp point in the old location that leads to the blocked zone
                if (lastSafeLocationName != oldLocationName)
                {
                    bool foundWarp = false;
                    foreach (var warp in e.OldLocation.warps)
                    {
                        if (warp.TargetName == newLocationName)
                        {
                            warpX = warp.X;
                            warpY = warp.Y;
                            foundWarp = true;
                            break;
                        }
                    }
                    if (!foundWarp)
                    {
                        // Fallback: use center of old map
                        warpX = e.OldLocation.Map.Layers[0].LayerWidth / 2;
                        warpY = e.OldLocation.Map.Layers[0].LayerHeight / 2;
                    }
                }

                Game1.warpFarmer(oldLocationName, warpX, warpY, false);
            }
            else
                Game1.warpFarmer("Farm", 64, 15, false);
        }

        private bool IsFarmLocation(string name) =>
            !string.IsNullOrEmpty(name) && (
                name == "Farm" || name == "FarmHouse" || name == "FarmCave" ||
                name == "Cellar" || name == "Greenhouse" ||
                name.StartsWith("Cellar") || name.StartsWith("Cabin"));

        /// <summary>Parse mine floor number from location name (e.g. "UndergroundMine25" → 25). Returns -1 if not a mine floor.</summary>
        private static int ParseMineFloor(string locationName)
        {
            if (locationName != null && locationName.StartsWith("UndergroundMine") && int.TryParse(locationName.AsSpan(15), out int floor))
                return floor;
            return -1;
        }

        // ── Input: K for read-only, action button for plates + minecart signs ─

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.activeClickableMenu != null) return;

            // Action button: check plates and minecart signs
            if (e.Button.IsActionButton())
            {
                var grabTile = e.Cursor.GrabTile;
                int tileX = (int)grabTile.X;
                int tileY = (int)grabTile.Y;
                // Plates have no collision, so the player can walk onto one. Accept
                // either the facing tile (GrabTile) or the standing tile.
                int standX = (int)Game1.player.Tile.X;
                int standY = (int)Game1.player.Tile.Y;
                string locName = Game1.currentLocation.Name;

                // Plate placement mode: place the plate at the clicked tile
                if (platePlacementZoneId != null)
                {
                    var zone = stateManager.GetZoneById(platePlacementZoneId);
                    if (zone != null)
                    {
                        var newPlate = new PlateTile { LocationName = locName, X = tileX, Y = tileY };
                        stateManager.SetPlateOverride(zone.ZoneId, newPlate);
                        Game1.addHUDMessage(new HUDMessage($"Plate for '{zone.DisplayName}' moved to {locName} ({tileX}, {tileY}).", HUDMessage.newQuest_type));
                        Game1.playSound("questcomplete");
                        Monitor.Log($"Plate for '{zone.ZoneId}' set to {locName} ({tileX}, {tileY}). Saved to state and synced.", LogLevel.Info);
                    }
                    platePlacementZoneId = null;
                    Helper.Input.Suppress(e.Button);
                    return;
                }

                // Check zone plates (use effective plate positions from save data or content)
                foreach (var zone in stateManager.GetContentZones())
                {
                    var plate = stateManager.GetEffectivePlate(zone);
                    if (plate == null) continue;
                    if (locName != plate.LocationName) continue;
                    bool onPlate = (tileX == plate.X && tileY == plate.Y)
                                || (standX == plate.X && standY == plate.Y);
                    if (!onPlate) continue;

                    // Plate found!
                    if (zone.UnlockType == "permanent" && stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
                    {
                        // Plate already completed — don't show anything (plate has "disappeared")
                        return;
                    }

                    Game1.activeClickableMenu = new BundleMenu(config, stateManager, purchaseEnabled: true, focusZoneId: zone.ZoneId,
                        onRequestPlatePlacement: Context.IsMainPlayer ? RequestPlatePlacement : null,
                        onRequestZoneEdit: Context.IsMainPlayer ? RequestZoneEdit : null,
                        onRequestBundleEdit: Context.IsMainPlayer ? RequestBundleEdit : null);
                    Game1.playSound("bigSelect");
                    Helper.Input.Suppress(e.Button);
                    return;
                }

                // Check beach minecart signs
                if (config.BeachMinecart.Enabled && stateManager.IsZonePermanentlyUnlocked("Beach"))
                {
                    var mc = config.BeachMinecart;

                    // Mountain sign → warp to beach
                    if (locName == mc.MountainLocation && tileX == mc.MountainSignX && tileY == mc.MountainSignY)
                    {
                        Game1.playSound("stoneStep");
                        Game1.warpFarmer(mc.BeachLocation, mc.BeachArrivalX, mc.BeachArrivalY, false);
                        Helper.Input.Suppress(e.Button);
                        return;
                    }

                    // Beach sign → warp back to mountain
                    if (locName == mc.BeachLocation && tileX == mc.BeachSignX && tileY == mc.BeachSignY)
                    {
                        Game1.playSound("stoneStep");
                        Game1.warpFarmer(mc.MountainLocation, mc.MountainArrivalX, mc.MountainArrivalY, false);
                        Helper.Input.Suppress(e.Button);
                        return;
                    }
                }

                // Check secondary beach bypass signs (configurable alternate route that avoids town)
                if (config.SecondaryBeachBypass != null && config.SecondaryBeachBypass.Enabled
                    && stateManager.IsZonePermanentlyUnlocked("Beach"))
                {
                    var sb = config.SecondaryBeachBypass;

                    if (locName == sb.OtherLocation && tileX == sb.OtherSignX && tileY == sb.OtherSignY)
                    {
                        Game1.playSound("stoneStep");
                        Game1.warpFarmer(sb.BeachLocation, sb.BeachArrivalX, sb.BeachArrivalY, false);
                        Helper.Input.Suppress(e.Button);
                        return;
                    }

                    if (locName == sb.BeachLocation && tileX == sb.BeachSignX && tileY == sb.BeachSignY)
                    {
                        Game1.playSound("stoneStep");
                        Game1.warpFarmer(sb.OtherLocation, sb.OtherArrivalX, sb.OtherArrivalY, false);
                        Helper.Input.Suppress(e.Button);
                        return;
                    }
                }
            }

            // K key: open read-only overview
            if (Enum.TryParse<SButton>(config.OpenMenuKey, ignoreCase: true, out SButton configuredKey)
                && e.Button == configuredKey)
            {
                Game1.activeClickableMenu = new BundleMenu(config, stateManager, purchaseEnabled: false,
                    onRequestPlatePlacement: Context.IsMainPlayer ? RequestPlatePlacement : null,
                    onRequestZoneEdit: Context.IsMainPlayer ? RequestZoneEdit : null,
                    onRequestBundleEdit: Context.IsMainPlayer ? RequestBundleEdit : null);
                Game1.playSound("bigSelect");
                Helper.Input.Suppress(e.Button);
            }
        }

        // ── Console commands ─────────────────────────────────────────

        private void OnMovePlateCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady)
            {
                Monitor.Log("You must be in-game to use this command.", LogLevel.Warn);
                return;
            }

            if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                Monitor.Log("Available zones:", LogLevel.Info);
                foreach (var zone in stateManager.GetContentZones())
                {
                    var plate = stateManager.GetEffectivePlate(zone);
                    string plateLoc = plate != null ? $"{plate.LocationName} ({plate.X}, {plate.Y})" : "none";
                    Monitor.Log($"  {zone.ZoneId} — {zone.DisplayName} — plate at: {plateLoc}", LogLevel.Info);
                }
                Monitor.Log("Usage: zlc_moveplate <ZoneId> — then click a tile in-game to place the plate.", LogLevel.Info);
                return;
            }

            if (!Context.IsMainPlayer)
            {
                Monitor.Log("Only the host can move plates.", LogLevel.Warn);
                return;
            }

            string zoneId = args[0];
            var targetZone = stateManager.GetContentZones().FirstOrDefault(z => z.ZoneId.Equals(zoneId, StringComparison.OrdinalIgnoreCase));
            if (targetZone == null)
            {
                Monitor.Log($"Unknown zone '{zoneId}'. Use 'zlc_moveplate list' to see valid zone IDs.", LogLevel.Warn);
                return;
            }

            platePlacementZoneId = targetZone.ZoneId;
            Game1.addHUDMessage(new HUDMessage($"Click a tile to place the '{targetZone.DisplayName}' plate.", HUDMessage.newQuest_type));
            Monitor.Log($"Plate placement mode active for '{targetZone.ZoneId}'. Click any tile in-game to set the plate location.", LogLevel.Info);
        }

        private void OnUnlockCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady) { Monitor.Log("You must be in-game to use this command.", LogLevel.Warn); return; }
            if (!Context.IsMainPlayer) { Monitor.Log("Only the host can unlock zones.", LogLevel.Warn); return; }
            if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                LogZoneStatus();
                return;
            }

            string zoneId = args[0];
            var zone = stateManager.GetContentZones().FirstOrDefault(z => z.ZoneId.Equals(zoneId, StringComparison.OrdinalIgnoreCase));
            if (zone == null) { Monitor.Log($"Unknown zone '{zoneId}'. Use 'zlc_unlock list' to see valid zone IDs.", LogLevel.Warn); return; }

            if (stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
            { Monitor.Log($"Zone '{zone.ZoneId}' is already unlocked.", LogLevel.Info); return; }

            stateManager.AdminUnlock(zone.ZoneId);
            Monitor.Log($"Zone '{zone.ZoneId}' ({zone.DisplayName}) has been manually unlocked.", LogLevel.Info);
        }

        private void OnLockCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady) { Monitor.Log("You must be in-game to use this command.", LogLevel.Warn); return; }
            if (!Context.IsMainPlayer) { Monitor.Log("Only the host can lock zones.", LogLevel.Warn); return; }
            if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                LogZoneStatus();
                return;
            }

            string zoneId = args[0];
            var zone = stateManager.GetContentZones().FirstOrDefault(z => z.ZoneId.Equals(zoneId, StringComparison.OrdinalIgnoreCase));
            if (zone == null) { Monitor.Log($"Unknown zone '{zoneId}'. Use 'zlc_lock list' to see valid zone IDs.", LogLevel.Warn); return; }

            if (!stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
            { Monitor.Log($"Zone '{zone.ZoneId}' is already locked.", LogLevel.Info); return; }

            stateManager.AdminLock(zone.ZoneId);
            Monitor.Log($"Zone '{zone.ZoneId}' ({zone.DisplayName}) has been manually locked.", LogLevel.Info);
        }

        private void LogZoneStatus()
        {
            Monitor.Log("Zone status:", LogLevel.Info);
            foreach (var zone in stateManager.GetContentZones())
            {
                string status = stateManager.IsZonePermanentlyUnlocked(zone.ZoneId) ? "UNLOCKED" : "LOCKED";
                Monitor.Log($"  {zone.ZoneId} — {zone.DisplayName} — {status}", LogLevel.Info);
            }
        }

        /// <summary>Called by BundleMenu to open the zone edit menu (host only).</summary>
        private void RequestZoneEdit(string zoneId)
        {
            var zone = stateManager.GetZoneById(zoneId);
            if (zone == null) return;
            Game1.activeClickableMenu = new ZoneEditMenu(zone, stateManager);
            Monitor.Log($"Opened zone editor for '{zone.ZoneId}'.", LogLevel.Info);
        }

        /// <summary>Called by BundleMenu to open the custom bundle editor (host only). Null = create new.</summary>
        private void RequestBundleEdit(string bundleId)
        {
            Game1.activeClickableMenu = new CustomBundleEditMenu(stateManager, bundleId);
            Monitor.Log($"Opened custom bundle editor{(bundleId != null ? $" for '{bundleId}'" : " (new)")}.", LogLevel.Info);
        }

        /// <summary>Called by BundleMenu to enter plate placement mode for a zone.</summary>
        private void RequestPlatePlacement(string zoneId)
        {
            platePlacementZoneId = zoneId;
            var zone = stateManager.GetZoneById(zoneId);
            string name = zone?.DisplayName ?? zoneId;
            Game1.addHUDMessage(new HUDMessage($"Click a tile to place the '{name}' plate.", HUDMessage.newQuest_type));
            Monitor.Log($"Plate placement mode active for '{zoneId}'. Click any tile in-game to set the plate location.", LogLevel.Info);
        }

        // ── Plate + sign rendering ───────────────────────────────────

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            string locName = Game1.currentLocation.Name;
            SpriteBatch b = e.SpriteBatch;

            // Draw zone plates in the current location
            foreach (var zone in stateManager.GetContentZones())
            {
                var plate = stateManager.GetEffectivePlate(zone);
                if (plate == null) continue;
                if (plate.LocationName != locName) continue;

                // Don't draw plate if permanently unlocked (plate "disappeared")
                if (zone.UnlockType == "permanent" && stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
                    continue;

                DrawPlateSprite(b, zone, plate);
            }

            // Draw minecart signs if beach is unlocked
            if (config.BeachMinecart.Enabled && stateManager.IsZonePermanentlyUnlocked("Beach"))
            {
                var mc = config.BeachMinecart;

                if (locName == mc.MountainLocation)
                    DrawSignSprite(b, mc.MountainSignX, mc.MountainSignY, "Beach");

                if (locName == mc.BeachLocation)
                    DrawSignSprite(b, mc.BeachSignX, mc.BeachSignY, "Mountain");
            }

            // Draw secondary bypass signs if beach is unlocked
            if (config.SecondaryBeachBypass != null && config.SecondaryBeachBypass.Enabled
                && stateManager.IsZonePermanentlyUnlocked("Beach"))
            {
                var sb = config.SecondaryBeachBypass;

                if (locName == sb.OtherLocation)
                    DrawSignSprite(b, sb.OtherSignX, sb.OtherSignY, "Beach");

                if (locName == sb.BeachLocation)
                    DrawSignSprite(b, sb.BeachSignX, sb.BeachSignY, sb.OtherLocation);
            }
        }

        private void DrawPlateSprite(SpriteBatch b, ZoneDefinition zone, PlateTile plate)
        {
            Vector2 worldPos = new(plate.X * 64, plate.Y * 64);
            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos);
            float bounce = (float)Math.Sin(plateAnimTimer * 3.0) * 4f;

            bool isTicketZone = zone.UnlockType == "ticket";
            var texture = contentProvider.GetSprites();
            Rectangle srcRect = isTicketZone ? new Rectangle(16, 0, 16, 16) : new Rectangle(0, 0, 16, 16);

            b.Draw(texture,
                new Vector2(screenPos.X + 8, screenPos.Y - 16 + bounce),
                srcRect, Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 0.99f);

            string label = !string.IsNullOrEmpty(zone.DisplayName) ? zone.DisplayName : zone.BundleName;
            Vector2 textSize = Game1.smallFont.MeasureString(label);
            float textScale = Math.Min(1f, 180f / textSize.X);
            Vector2 textPos = new(screenPos.X + 32 - textSize.X * textScale / 2, screenPos.Y + 40 + bounce);
            b.DrawString(Game1.smallFont, label, textPos + new Vector2(1, 1), Color.Black * 0.5f, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0.991f);
            b.DrawString(Game1.smallFont, label, textPos, Color.White, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0.992f);
        }

        private void DrawSignSprite(SpriteBatch b, int tileX, int tileY, string label)
        {
            Vector2 worldPos = new(tileX * 64, tileY * 64);
            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos);
            float bounce = (float)Math.Sin(plateAnimTimer * 2.5 + 1.0) * 3f;

            var texture = contentProvider.GetSprites();
            b.Draw(texture,
                new Vector2(screenPos.X + 8, screenPos.Y - 16 + bounce),
                new Rectangle(32, 0, 16, 16),
                Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 0.99f);

            string text = $"To {label}";
            Vector2 textSize = Game1.smallFont.MeasureString(text);
            Vector2 textPos = new(screenPos.X + 32 - textSize.X / 2, screenPos.Y + 40 + bounce);
            b.DrawString(Game1.smallFont, text, textPos + new Vector2(1, 1), Color.Black * 0.5f, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.991f);
            b.DrawString(Game1.smallFont, text, textPos, Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.992f);
        }
    }
}
