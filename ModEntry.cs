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

        // Farmhand only: tickets that were active when the day ended. The host's overnight
        // cleanup broadcast can arrive before our DayStarted fires, wiping the expired entries
        // before GetLocalExpiredTicketZones() can see them — so snapshot at DayEnding.
        private List<string> ticketsActiveAtDayEnd = new();

        // Cooldown so repeated bumps into a locked border don't stack HUD messages
        private float lastBlockedMsgAt = -999f;
        private string lastBlockedMsgKey = "";

        private bool shownStartupHint;

        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<ModConfig>();
            contentProvider = new ContentProvider(helper, config);
            stateManager = new ZoneStateManager(helper, Monitor, config, contentProvider);

            stateManager.OnStateChanged = () =>
            {
                if (Game1.activeClickableMenu is BundleMenu menu)
                    menu.RefreshSidebar();
            };

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
            helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            helper.Events.Content.AssetsInvalidated += contentProvider.OnAssetInvalidated;

            helper.ConsoleCommands.Add("zlc_moveplate",
                "Move a zone plate to your current cursor tile. Usage: zlc_moveplate <ZoneId>\nUse 'zlc_moveplate list' to see all zone IDs.",
                OnMovePlateCommand);

            helper.ConsoleCommands.Add("zlc_unlock",
                "Manually unlock a zone (host only). Usage: zlc_unlock <ZoneId>\nUse 'zlc_unlock list' to see all zone IDs and their status.",
                OnUnlockCommand);

            helper.ConsoleCommands.Add("zlc_lock",
                "Manually lock a zone (host only). Usage: zlc_lock <ZoneId>\nUse 'zlc_lock list' to see all zone IDs and their status.",
                OnLockCommand);

            helper.ConsoleCommands.Add("zlc_status",
                "Show the full mod state: zones, contributions, tickets, bundles, and mine gates.",
                OnStatusCommand);

            helper.ConsoleCommands.Add("zlc_unlock_all",
                "Unlock every permanent zone at once (host only). Useful for testing a setup before a run.",
                OnUnlockAllCommand);

            helper.ConsoleCommands.Add("zlc_reset_zone",
                "Clear a zone's in-game cost/item edits and pooled gold, reverting to config defaults (host only). Usage: zlc_reset_zone <ZoneId>",
                OnResetZoneCommand);

            helper.ConsoleCommands.Add("zlc_reload",
                "Re-read config.json and refresh content assets without restarting the game.",
                OnReloadCommand);

            ValidateConfig();

            Monitor.Log("Zone Lock Challenge loaded. Press " + config.OpenMenuKey + " to view zones. Visit zone plates to purchase.", LogLevel.Info);
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm == null) return;

            gmcm.Register(
                mod: ModManifest,
                reset: () =>
                {
                    var fresh = new ModConfig();
                    config.CopyFrom(fresh);
                },
                save: () => Helper.WriteConfig(config)
            );

            gmcm.AddKeybind(
                mod: ModManifest,
                getValue: () => Enum.TryParse<SButton>(config.OpenMenuKey, true, out var btn) ? btn : SButton.K,
                setValue: val => config.OpenMenuKey = val.ToString(),
                name: () => "Open Menu Key",
                tooltip: () => "Key to open the zone overview menu."
            );

            gmcm.AddBoolOption(
                mod: ModManifest,
                getValue: () => config.ShowBlockedMessage,
                setValue: val => config.ShowBlockedMessage = val,
                name: () => "Show Blocked Messages",
                tooltip: () => "Show a HUD message when entering a locked zone or interacting with a completed plate."
            );

            gmcm.AddBoolOption(
                mod: ModManifest,
                getValue: () => config.PreventFriendshipDecay,
                setValue: val => config.PreventFriendshipDecay = val,
                name: () => "Prevent Friendship Decay",
                tooltip: () => "Restore overnight friendship point loss so NPCs in locked zones don't penalise you."
            );

            gmcm.AddNumberOption(
                mod: ModManifest,
                getValue: () => config.CostScalingPercent,
                setValue: val => config.CostScalingPercent = val,
                name: () => "Cost Scaling %",
                tooltip: () => "Extra percentage added to a zone's gold cost for each zone already unlocked. 0 disables scaling.",
                min: 0,
                max: 200,
                interval: 5
            );
        }

        // ── Lifecycle ────────────────────────────────────────────────

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            stateManager.LoadState();
            stateManager.RecordStatsSnapshot(Game1.player);
            if (!Context.IsMainPlayer)
                stateManager.RequestSync();

            if (!shownStartupHint)
            {
                shownStartupHint = true;
                Game1.addHUDMessage(new HUDMessage($"Zone Lock Challenge active — press {config.OpenMenuKey} to view zones.", HUDMessage.newQuest_type));
            }
        }

        private void OnSaving(object sender, SavingEventArgs e) => stateManager.SaveState();

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            var expired = Context.IsMainPlayer
                ? stateManager.CleanupExpiredTickets()
                : stateManager.GetLocalExpiredTicketZones().Union(ticketsActiveAtDayEnd).ToList();
            ticketsActiveAtDayEnd = new List<string>();
            foreach (var zoneId in expired)
            {
                var zone = stateManager.GetZoneById(zoneId);
                if (zone != null)
                    Game1.addHUDMessage(new HUDMessage($"{zone.DisplayName} ticket expired. Visit the plate to buy a new one.", HUDMessage.error_type));
            }

            if (Utility.isFestivalDay())
                Game1.addHUDMessage(new HUDMessage("Festival today — zone locks are lifted for the day!", HUDMessage.newQuest_type));

            EvictFromLockedZone();

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
            }
        }

        private void OnDayEnding(object sender, DayEndingEventArgs e)
        {
            stateManager.RecordStatsSnapshot(Game1.player);

            if (!Context.IsMainPlayer)
                ticketsActiveAtDayEnd = stateManager.GetLocalActiveTicketZones();

            if (!config.PreventFriendshipDecay) return;
            friendshipSnapshot.Clear();
            foreach (var kvp in Game1.player.friendshipData.Pairs)
                friendshipSnapshot[kvp.Key] = kvp.Value.Points;
        }

        /// <summary>At 9pm, remind the player that their daily tickets lapse overnight.</summary>
        private void OnTimeChanged(object sender, TimeChangedEventArgs e)
        {
            if (e.NewTime != 2100) return;
            foreach (var zoneId in stateManager.GetLocalActiveTicketZones())
            {
                var zone = stateManager.GetZoneById(zoneId);
                if (zone != null)
                    Game1.addHUDMessage(new HUDMessage($"Your {zone.DisplayName} ticket expires at the end of the day.", HUDMessage.newQuest_type));
            }
        }

        /// <summary>If the player wakes up inside a now-locked zone (overnight hospital respawn,
        /// expired ticket after passing out in town), warp them home. OnPlayerWarped can't catch
        /// this because no warp event fires on wake-up.</summary>
        private void EvictFromLockedZone()
        {
            string locName = Game1.currentLocation?.Name;
            if (locName == null || IsFarmLocation(locName)) return;
            if (Utility.isFestivalDay()) return;

            var zone = stateManager.GetZoneForLocation(locName);
            if (zone == null || stateManager.IsZoneAccessible(zone.ZoneId, Game1.player.UniqueMultiplayerID)) return;

            Game1.warpFarmer("Farm", 64, 15, false);
            Game1.addHUDMessage(new HUDMessage($"{zone.DisplayName} is locked — you were returned to the farm.", HUDMessage.error_type));
            Monitor.Log($"Evicted {Game1.player.Name} from locked zone '{zone.ZoneId}' at day start.", LogLevel.Info);
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

            string currentLoc = Game1.currentLocation?.Name ?? "Farm";
            if (IsFarmLocation(currentLoc))
            {
                lastSafeLocationName = currentLoc;
                lastSafeX = (int)Game1.player.Tile.X;
                lastSafeY = (int)Game1.player.Tile.Y;
            }
            else
            {
                var zone = stateManager.GetZoneForLocation(currentLoc);
                if (zone == null || stateManager.IsZoneAccessible(zone.ZoneId, Game1.player.UniqueMultiplayerID))
                {
                    lastSafeLocationName = currentLoc;
                    lastSafeX = (int)Game1.player.Tile.X;
                    lastSafeY = (int)Game1.player.Tile.Y;
                }
            }
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

            // Festival days lift all zone locks for the whole day: festivals are often deep
            // inside locked zones (Town, Beach), and gating on Game1.eventUp would bounce
            // players walking to the festival before the event has started.
            if (Utility.isFestivalDay())
                return;

            long farmerId = Game1.player.UniqueMultiplayerID;
            var zone = stateManager.GetZoneForLocation(newLocationName);
            if (zone == null) return;

            // Dungeon floor gate checks: even if the parent zone is unlocked, specific floors may be gated
            if (stateManager.IsZoneAccessible(zone.ZoneId, farmerId))
            {
                int mineFloor = ParseMineFloor(newLocationName);
                if (mineFloor >= 0 && !stateManager.IsMineLevelAllowed(mineFloor))
                {
                    int required = stateManager.GetRequiredMiningLevelForFloor(mineFloor);
                    int current = stateManager.GetCollectiveSkillLevel("Mining");
                    Monitor.Log($"Blocked {Game1.player.Name} from mine floor {mineFloor} (need collective Mining {required}, have {current}).", LogLevel.Info);
                    ShowBlockedMessage($"mine_{mineFloor}", $"Floor {mineFloor} is gated! Need collective Mining level {required} (have {current}).");
                    WarpBackToPrevious(oldLocationName);
                    return;
                }

                int skullFloor = ParseSkullCavernFloor(newLocationName);
                if (skullFloor >= 0)
                {
                    var gates = stateManager.GetEffectiveSkullCavernGates();
                    var blocking = stateManager.GetBlockingDungeonGate(gates, skullFloor);
                    if (blocking.HasValue)
                    {
                        var (skill, required, current) = blocking.Value;
                        Monitor.Log($"Blocked {Game1.player.Name} from Skull Cavern floor {skullFloor} (need collective {skill} {required}, have {current}).", LogLevel.Info);
                        ShowBlockedMessage($"skull_{skullFloor}", $"Skull Cavern floor {skullFloor} is gated! Need collective {skill} level {required} (have {current}).");
                        WarpBackToPrevious(oldLocationName);
                        return;
                    }
                }

                int volcanoFloor = ParseVolcanoFloor(newLocationName);
                if (volcanoFloor >= 0)
                {
                    var gates = stateManager.GetEffectiveVolcanoGates();
                    var blocking = stateManager.GetBlockingDungeonGate(gates, volcanoFloor);
                    if (blocking.HasValue)
                    {
                        var (skill, required, current) = blocking.Value;
                        Monitor.Log($"Blocked {Game1.player.Name} from Volcano floor {volcanoFloor} (need collective {skill} {required}, have {current}).", LogLevel.Info);
                        ShowBlockedMessage($"volcano_{volcanoFloor}", $"Volcano floor {volcanoFloor} is gated! Need collective {skill} level {required} (have {current}).");
                        WarpBackToPrevious(oldLocationName);
                        return;
                    }
                }

                return;
            }

            Monitor.Log($"Blocked {Game1.player.Name} from entering {newLocationName} (zone: {zone.ZoneId} is locked).", LogLevel.Info);

            ShowBlockedMessage($"zone_{zone.ZoneId}", $"{zone.DisplayName} is locked! Visit the zone plate to unlock it.");

            isWarpingBack = true;
            warpBackFramesLeft = 12; // keep flag up long enough for the return warp to complete, even on a lag spike

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

                    var layer = e.OldLocation.Map?.Layers?.Count > 0 ? e.OldLocation.Map.Layers[0] : null;
                    if (!foundWarp || layer == null)
                    {
                        // No usable return point in the old location (a layerless map gives no
                        // safe way to step off the warp trigger tile). Prefer the last tracked
                        // safe spot if its location is still accessible — map centers are
                        // frequently water or buildings, so they're not a fallback.
                        var lastSafeZone = stateManager.GetZoneForLocation(lastSafeLocationName);
                        bool lastSafeOk = IsFarmLocation(lastSafeLocationName)
                            || lastSafeZone == null
                            || stateManager.IsZoneAccessible(lastSafeZone.ZoneId, farmerId);
                        if (lastSafeOk)
                        {
                            Game1.warpFarmer(lastSafeLocationName, lastSafeX, lastSafeY, false);
                            return;
                        }
                        Game1.warpFarmer("Farm", 64, 15, false);
                        return;
                    }

                    // Landing exactly on the warp trigger tile would immediately re-fire the
                    // warp into the locked zone (bounce loop), and trigger tiles often sit in
                    // doorframes or at map edges. Step one tile toward the map interior.
                    warpX += Math.Sign(layer.LayerWidth / 2 - warpX);
                    warpY += Math.Sign(layer.LayerHeight / 2 - warpY);
                }

                Game1.warpFarmer(oldLocationName, warpX, warpY, false);
            }
            else
                Game1.warpFarmer("Farm", 64, 15, false);
        }

        /// <summary>Show a blocked-entry HUD message, but not more than once per few seconds for
        /// the same target — repeatedly bumping a locked border shouldn't stack red messages.</summary>
        private void ShowBlockedMessage(string key, string message)
            => ShowBlockedMessage(key, message, HUDMessage.error_type);

        private void ShowBlockedMessage(string key, string message, int messageType)
        {
            if (!config.ShowBlockedMessage) return;
            if (key == lastBlockedMsgKey && plateAnimTimer - lastBlockedMsgAt < 3f) return;
            lastBlockedMsgKey = key;
            lastBlockedMsgAt = plateAnimTimer;
            Game1.addHUDMessage(new HUDMessage(message, messageType));
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

        private void WarpBackToPrevious(string oldLocationName)
        {
            isWarpingBack = true;
            warpBackFramesLeft = 12;
            Game1.warpFarmer(oldLocationName, lastSafeX, lastSafeY, false);
        }

        private static int ParseSkullCavernFloor(string locationName)
        {
            if (locationName != null && locationName.StartsWith("SkullCave") && int.TryParse(locationName.AsSpan(9), out int floor))
                return floor;
            return -1;
        }

        private static int ParseVolcanoFloor(string locationName)
        {
            if (locationName != null && locationName.StartsWith("VolcanoDungeon") && int.TryParse(locationName.AsSpan(14), out int floor))
                return floor;
            return -1;
        }

        // ── Input: K for read-only, action button for plates + minecart signs ─

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.activeClickableMenu != null) return;

            // Escape cancels plate placement mode (otherwise the only way out is placing the plate)
            if (platePlacementZoneId != null && (e.Button == SButton.Escape || e.Button == SButton.ControllerB))
            {
                platePlacementZoneId = null;
                Game1.addHUDMessage(new HUDMessage("Plate placement cancelled.", HUDMessage.newQuest_type));
                Helper.Input.Suppress(e.Button);
                return;
            }

            // Action button: check plates and minecart signs
            if (e.Button.IsActionButton())
            {
                var grabTile = e.Cursor.GrabTile;
                int tileX = (int)grabTile.X;
                int tileY = (int)grabTile.Y;
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

                int standX = (int)Game1.player.Tile.X;
                int standY = (int)Game1.player.Tile.Y;

                // Check zone plates (use effective plate positions from save data or content)
                foreach (var zone in stateManager.GetContentZones())
                {
                    var plate = stateManager.GetEffectivePlate(zone);
                    if (plate == null) continue;
                    if (locName != plate.LocationName) continue;
                    bool grabMatch = (tileX == plate.X && tileY == plate.Y);
                    bool standMatch = (standX == plate.X && standY == plate.Y);
                    if (!grabMatch && !standMatch) continue;

                    // Plate found!
                    if (zone.UnlockType == "permanent" && stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
                    {
                        // Plate completed and no longer drawn — give light feedback instead of
                        // silently swallowing the click
                        ShowBlockedMessage($"plate_{zone.ZoneId}", $"{zone.DisplayName} is already unlocked.", HUDMessage.newQuest_type);
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
            Game1.addHUDMessage(new HUDMessage($"Click a tile to place the '{targetZone.DisplayName}' plate (Esc to cancel).", HUDMessage.newQuest_type));
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

        private void OnStatusCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady) { Monitor.Log("You must be in-game to use this command.", LogLevel.Warn); return; }

            Monitor.Log("=== Zone Lock Challenge status ===", LogLevel.Info);
            foreach (var zone in stateManager.GetContentZones())
            {
                string status;
                if (stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
                    status = "UNLOCKED";
                else
                {
                    int scaled = stateManager.GetScaledMoneyCost(zone);
                    int pooled = stateManager.GetTotalContributions(zone.ZoneId);
                    status = pooled > 0 ? $"LOCKED — {pooled:N0}/{scaled:N0}g pooled" : $"LOCKED — {scaled:N0}g";
                    if (zone.UnlockType == "ticket") status = $"TICKET ZONE — {scaled:N0}g/day";
                }
                string overridden = stateManager.State.ZoneOverrides.ContainsKey(zone.ZoneId) ? " [edited in-game]" : "";
                Monitor.Log($"  {zone.ZoneId} — {zone.DisplayName} — {status}{overridden}", LogLevel.Info);
            }

            if (stateManager.State.ActiveTickets.Count > 0)
            {
                Monitor.Log("Active tickets:", LogLevel.Info);
                foreach (var kv in stateManager.State.ActiveTickets)
                    foreach (var ticket in kv.Value)
                    {
                        var farmer = Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == ticket.Key);
                        string who = farmer?.Name ?? $"Player {ticket.Key}";
                        string valid = ticket.Value == Game1.Date.TotalDays ? "valid today" : "expired";
                        Monitor.Log($"  {kv.Key}: {who} ({valid})", LogLevel.Info);
                    }
            }

            var bundles = stateManager.GetCustomBundles();
            if (bundles.Count > 0)
            {
                Monitor.Log("Custom bundles:", LogLevel.Info);
                foreach (var bundle in bundles)
                    Monitor.Log($"  {bundle.DisplayName} — {(bundle.IsCompleted ? "COMPLETED" : "incomplete")}", LogLevel.Info);
            }

            var gates = stateManager.GetEffectiveMineLevelGates();
            if (gates.Count > 0)
            {
                int mining = stateManager.GetCollectiveSkillLevel("Mining");
                Monitor.Log($"Mine gates (collective Mining: {mining}):", LogLevel.Info);
                foreach (var gate in gates.OrderBy(g => g.FloorNumber))
                    Monitor.Log($"  Floor {gate.FloorNumber}: requires Mining {gate.RequiredMiningLevel}{(mining >= gate.RequiredMiningLevel ? " (met)" : "")}", LogLevel.Info);
            }

            LogDungeonGates("Skull Cavern", stateManager.GetEffectiveSkullCavernGates());
            LogDungeonGates("Volcano", stateManager.GetEffectiveVolcanoGates());
        }

        private void LogDungeonGates(string dungeonName, List<DungeonGate> gates)
        {
            if (gates == null || gates.Count == 0) return;
            Monitor.Log($"{dungeonName} gates:", LogLevel.Info);
            foreach (var gate in gates.OrderBy(g => g.FloorNumber))
            {
                string skill = gate.RequiredSkill ?? "Combat";
                int current = stateManager.GetCollectiveSkillLevel(skill);
                Monitor.Log($"  Floor {gate.FloorNumber}: requires {skill} {gate.RequiredLevel}{(current >= gate.RequiredLevel ? " (met)" : $" (have {current})")}", LogLevel.Info);
            }
        }

        private void OnUnlockAllCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady) { Monitor.Log("You must be in-game to use this command.", LogLevel.Warn); return; }
            if (!Context.IsMainPlayer) { Monitor.Log("Only the host can unlock zones.", LogLevel.Warn); return; }
            int count = stateManager.AdminUnlockAll();
            Monitor.Log(count > 0 ? $"Unlocked {count} zone(s)." : "All permanent zones are already unlocked.", LogLevel.Info);
        }

        private void OnResetZoneCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady) { Monitor.Log("You must be in-game to use this command.", LogLevel.Warn); return; }
            if (!Context.IsMainPlayer) { Monitor.Log("Only the host can reset zones.", LogLevel.Warn); return; }
            if (args.Length == 0) { Monitor.Log("Usage: zlc_reset_zone <ZoneId>", LogLevel.Warn); return; }

            var zone = stateManager.GetContentZones().FirstOrDefault(z => z.ZoneId.Equals(args[0], StringComparison.OrdinalIgnoreCase));
            if (zone == null) { Monitor.Log($"Unknown zone '{args[0]}'. Use 'zlc_unlock list' to see valid zone IDs.", LogLevel.Warn); return; }

            bool changed = stateManager.AdminResetZone(zone.ZoneId);
            Monitor.Log(changed
                ? $"Zone '{zone.ZoneId}' reset to config defaults (in-game edits and pooled gold cleared)."
                : $"Zone '{zone.ZoneId}' had no in-game edits or pooled gold to clear.", LogLevel.Info);
        }

        private void OnReloadCommand(string command, string[] args)
        {
            var fresh = Helper.ReadConfig<ModConfig>();
            // Other classes hold a reference to the existing config object, so copy the values
            // onto it rather than swapping the reference.
            config.CopyFrom(fresh);

            contentProvider.InvalidateAllCaches();
            ValidateConfig();
            Monitor.Log("Reloaded config.json and refreshed content assets. Note: in-game zone edits (save overrides) still take precedence over config values.", LogLevel.Info);
        }

        /// <summary>Warn about config values that would silently misbehave in-game.</summary>
        private void ValidateConfig()
        {
            if (!Enum.TryParse<SButton>(config.OpenMenuKey, ignoreCase: true, out _))
                Monitor.Log($"Config OpenMenuKey '{config.OpenMenuKey}' is not a valid key name — the zone overview hotkey will not work. See https://stardewvalleywiki.com/Modding:Player_Guide/Key_Bindings", LogLevel.Warn);

            var zones = config.Zones ?? new List<ZoneDefinition>();
            var ids = new HashSet<string>(zones.Where(z => !string.IsNullOrEmpty(z.ZoneId)).Select(z => z.ZoneId));
            foreach (var zone in zones)
            {
                if (string.IsNullOrEmpty(zone.ZoneId))
                {
                    Monitor.Log($"A zone in config.json ('{zone.DisplayName ?? "?"}') has no ZoneId and will be ignored.", LogLevel.Warn);
                    continue;
                }
                if (zone.MoneyCost < 0)
                    Monitor.Log($"Zone '{zone.ZoneId}' has a negative MoneyCost ({zone.MoneyCost}); it will be treated as 0.", LogLevel.Warn);
                if (!string.IsNullOrEmpty(zone.RequiresZone) && !ids.Contains(zone.RequiresZone))
                    Monitor.Log($"Zone '{zone.ZoneId}' requires unknown zone '{zone.RequiresZone}' — it can never be unlocked.", LogLevel.Warn);
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
            Game1.addHUDMessage(new HUDMessage($"Click a tile to place the '{name}' plate (Esc to cancel).", HUDMessage.newQuest_type));
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

            // Plate placement ghost cursor: show a semi-transparent plate at the cursor tile
            if (platePlacementZoneId != null)
            {
                var zone = stateManager.GetZoneById(platePlacementZoneId);
                if (zone != null)
                {
                    var cursorTile = Game1.currentCursorTile;
                    int tileX = (int)cursorTile.X;
                    int tileY = (int)cursorTile.Y;

                    // Highlight the target tile with a green tint
                    Vector2 tileWorld = new(tileX * 64, tileY * 64);
                    Vector2 tileScreen = Game1.GlobalToLocal(Game1.viewport, tileWorld);
                    b.Draw(Game1.staminaRect, new Rectangle((int)tileScreen.X, (int)tileScreen.Y, 64, 64),
                        Color.Green * 0.35f);

                    // Draw a ghost plate sprite at the cursor tile
                    bool isTicket = zone.UnlockType == "ticket";
                    var texture = contentProvider.GetSprites();
                    Rectangle srcRect = isTicket ? new Rectangle(16, 0, 16, 16) : new Rectangle(0, 0, 16, 16);
                    b.Draw(texture,
                        new Vector2(tileScreen.X + 8, tileScreen.Y - 16),
                        srcRect, Color.White * 0.6f, 0f, Vector2.Zero, 3f, SpriteEffects.None, 0.99f);

                    // Label under the ghost
                    string label = zone.DisplayName ?? zone.BundleName ?? platePlacementZoneId;
                    Vector2 textSize = Game1.smallFont.MeasureString(label);
                    float textScale = Math.Min(1f, 180f / textSize.X);
                    Vector2 textPos = new(tileScreen.X + 32 - textSize.X * textScale / 2, tileScreen.Y + 40);
                    b.DrawString(Game1.smallFont, label, textPos + new Vector2(1, 1), Color.Black * 0.3f, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0.991f);
                    b.DrawString(Game1.smallFont, label, textPos, Color.White * 0.7f, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0.992f);
                }
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
