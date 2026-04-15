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

        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<ModConfig>();
            stateManager = new ZoneStateManager(helper, Monitor, config);

            // Refresh plates when state changes (purchase, sync)
            stateManager.OnStateChanged = () => { };

            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.Saving += OnSaving;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
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

            Monitor.Log("Zone Lock Challenge loaded. Press " + config.OpenMenuKey + " to view zones. Visit zone plates to purchase.", LogLevel.Info);
        }

        // ── Lifecycle ────────────────────────────────────────────────

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            stateManager.LoadState();
            if (!Context.IsMainPlayer)
                stateManager.RequestSync();
        }

        private void OnSaving(object sender, SavingEventArgs e) => stateManager.SaveState();

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            // FIX: Only show "ticket expired" for zones that actually had a ticket yesterday
            var expired = stateManager.CleanupExpiredTickets();
            if (Context.IsMainPlayer)
            {
                foreach (var zoneId in expired)
                {
                    var zone = config.Zones.FirstOrDefault(z => z.ZoneId == zoneId);
                    if (zone != null)
                        Game1.addHUDMessage(new HUDMessage($"{zone.DisplayName} ticket expired. Visit the plate to buy a new one.", HUDMessage.error_type));
                }
            }
        }

        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e) { isWarpingBack = false; warpBackFramesLeft = 0; }

        private void OnPeerConnected(object sender, PeerConnectedEventArgs e)
        {
            if (Context.IsMainPlayer) stateManager.BroadcastState();
        }

        // ── Tick handlers ────────────────────────────────────────────

        /// <summary>Fires BEFORE the game update — save position while player is still in their current location.</summary>
        private void OnUpdateTicking(object sender, UpdateTickingEventArgs e)
        {
            if (Context.IsWorldReady && !isWarpingBack && Game1.player != null)
            {
                lastSafeLocationName = Game1.currentLocation?.Name ?? "Farm";
                lastSafeX = (int)Game1.player.Tile.X;
                lastSafeY = (int)Game1.player.Tile.Y;
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

            // On festival days, allow all warps so players can attend festivals
            if (Utility.isFestivalDay())
                return;

            long farmerId = Game1.player.UniqueMultiplayerID;
            var zone = stateManager.GetZoneForLocation(newLocationName);
            if (zone == null) return;
            if (stateManager.IsZoneAccessible(zone.ZoneId, farmerId)) return;

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
                string locName = Game1.currentLocation.Name;

                // Plate placement mode: place the plate at the clicked tile
                if (platePlacementZoneId != null)
                {
                    var zone = config.Zones.FirstOrDefault(z => z.ZoneId == platePlacementZoneId);
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

                // Check zone plates (use effective plate positions from save data or config)
                foreach (var zone in config.Zones)
                {
                    var plate = stateManager.GetEffectivePlate(zone);
                    if (plate == null) continue;
                    if (locName != plate.LocationName) continue;
                    if (tileX != plate.X || tileY != plate.Y) continue;

                    // Plate found!
                    if (zone.UnlockType == "permanent" && stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
                    {
                        // Plate already completed — don't show anything (plate has "disappeared")
                        return;
                    }

                    // Open purchase menu focused on this zone
                    Game1.activeClickableMenu = new BundleMenu(config, stateManager, purchaseEnabled: true, focusZoneId: zone.ZoneId,
                        onRequestPlatePlacement: Context.IsMainPlayer ? RequestPlatePlacement : null);
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
            }

            // K key: open read-only overview
            if (Enum.TryParse<SButton>(config.OpenMenuKey, ignoreCase: true, out SButton configuredKey)
                && e.Button == configuredKey)
            {
                Game1.activeClickableMenu = new BundleMenu(config, stateManager, purchaseEnabled: false,
                    onRequestPlatePlacement: Context.IsMainPlayer ? RequestPlatePlacement : null);
                Game1.playSound("bigSelect");
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
                foreach (var zone in config.Zones)
                {
                    var plate = stateManager.GetEffectivePlate(zone);
                    string plateLoc = plate != null ? $"{plate.LocationName} ({plate.X}, {plate.Y})" : "none";
                    Monitor.Log($"  {zone.ZoneId} — {zone.DisplayName} — plate at: {plateLoc}", LogLevel.Info);
                }
                Monitor.Log("Usage: zlc_moveplate <ZoneId> — then click a tile in-game to place the plate.", LogLevel.Info);
                return;
            }

            string zoneId = args[0];
            var targetZone = config.Zones.FirstOrDefault(z => z.ZoneId.Equals(zoneId, StringComparison.OrdinalIgnoreCase));
            if (targetZone == null)
            {
                Monitor.Log($"Unknown zone '{zoneId}'. Use 'zlc_moveplate list' to see valid zone IDs.", LogLevel.Warn);
                return;
            }

            platePlacementZoneId = targetZone.ZoneId;
            Game1.addHUDMessage(new HUDMessage($"Click a tile to place the '{targetZone.DisplayName}' plate.", HUDMessage.newQuest_type));
            Monitor.Log($"Plate placement mode active for '{targetZone.ZoneId}'. Click any tile in-game to set the plate location.", LogLevel.Info);
        }

        /// <summary>Called by BundleMenu to enter plate placement mode for a zone.</summary>
        private void RequestPlatePlacement(string zoneId)
        {
            platePlacementZoneId = zoneId;
            var zone = config.Zones.FirstOrDefault(z => z.ZoneId == zoneId);
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

            // Draw zone plates in the current location (use effective plate positions)
            foreach (var zone in config.Zones)
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
        }

        private void DrawPlateSprite(SpriteBatch b, ZoneDefinition zone, PlateTile plate)
        {
            // World position of the tile
            Vector2 worldPos = new(plate.X * 64, plate.Y * 64);

            // Convert to screen position
            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos);

            // Gentle hover animation
            float bounce = (float)Math.Sin(plateAnimTimer * 3.0) * 4f;

            // Draw a bundle-style icon (junimo note sprite from cursors: 331, 374, 15, 14)
            // This is the golden scroll/note icon
            bool isTicketZone = zone.UnlockType == "ticket";
            Color tint = isTicketZone ? Color.LightGoldenrodYellow : Color.White;

            b.Draw(
                Game1.mouseCursors,
                new Vector2(screenPos.X + 16, screenPos.Y - 12 + bounce),
                new Rectangle(331, 374, 15, 14),
                tint, 0f, Vector2.Zero, 3f, SpriteEffects.None, 0.99f);

            // Draw small label below
            string label = !string.IsNullOrEmpty(zone.BundleName) ? zone.BundleName : zone.DisplayName;
            Vector2 textSize = Game1.smallFont.MeasureString(label);
            float textScale = Math.Min(1f, 180f / textSize.X); // scale down long names
            Vector2 textPos = new(screenPos.X + 32 - textSize.X * textScale / 2, screenPos.Y + 40 + bounce);

            // Text shadow
            b.DrawString(Game1.smallFont, label, textPos + new Vector2(1, 1), Color.Black * 0.5f, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0.991f);
            b.DrawString(Game1.smallFont, label, textPos, Color.White, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0.992f);
        }

        private void DrawSignSprite(SpriteBatch b, int tileX, int tileY, string label)
        {
            Vector2 worldPos = new(tileX * 64, tileY * 64);
            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos);
            float bounce = (float)Math.Sin(plateAnimTimer * 2.5 + 1.0) * 3f;

            // Draw a minecart-style icon (using a different cursor sprite: the arrow/warp icon)
            b.Draw(
                Game1.mouseCursors,
                new Vector2(screenPos.X + 16, screenPos.Y - 12 + bounce),
                new Rectangle(402, 496, 9, 9),
                Color.LightCyan, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.99f);

            // Label
            string text = $"To {label}";
            Vector2 textSize = Game1.smallFont.MeasureString(text);
            Vector2 textPos = new(screenPos.X + 32 - textSize.X / 2, screenPos.Y + 40 + bounce);
            b.DrawString(Game1.smallFont, text, textPos + new Vector2(1, 1), Color.Black * 0.5f, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.991f);
            b.DrawString(Game1.smallFont, text, textPos, Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.992f);
        }
    }
}
