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

        // Plate rendering: animated bounce
        private float plateAnimTimer;

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
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;

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

        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e) => isWarpingBack = false;

        private void OnPeerConnected(object sender, PeerConnectedEventArgs e)
        {
            if (Context.IsMainPlayer) stateManager.BroadcastState();
        }

        // ── Animation tick ───────────────────────────────────────────

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (Context.IsWorldReady)
                plateAnimTimer += (float)(1.0 / 60.0); // ~60 ticks per second
        }

        // ── Warp interception ────────────────────────────────────────

        private void OnPlayerWarped(object sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer || isWarpingBack) return;

            string locationName = e.NewLocation.Name;
            string oldLocationName = e.OldLocation.Name;

            if (IsFarmLocation(locationName)) return;

            var zone = stateManager.GetZoneForLocation(locationName);
            if (zone == null) return;
            if (stateManager.IsZoneAccessible(zone.ZoneId)) return;

            Monitor.Log($"Blocked {Game1.player.Name} from entering {locationName} (zone: {zone.ZoneId} is locked).", LogLevel.Info);

            if (config.ShowBlockedMessage)
                Game1.addHUDMessage(new HUDMessage($"{zone.DisplayName} is locked! Visit the zone plate to unlock it.", HUDMessage.error_type));

            isWarpingBack = true;
            try
            {
                if (IsFarmLocation(oldLocationName))
                    Game1.warpFarmer(oldLocationName, GetSafeReturnX(oldLocationName), GetSafeReturnY(oldLocationName), false);
                else
                    Game1.warpFarmer("Farm", 64, 15, false);
            }
            finally
            {
                Helper.Events.GameLoop.UpdateTicked += ResetWarpFlag;
            }
        }

        private void ResetWarpFlag(object sender, UpdateTickedEventArgs e)
        {
            isWarpingBack = false;
            Helper.Events.GameLoop.UpdateTicked -= ResetWarpFlag;
        }

        private bool IsFarmLocation(string name) =>
            !string.IsNullOrEmpty(name) && (
                name == "Farm" || name == "FarmHouse" || name == "FarmCave" ||
                name == "Cellar" || name == "Greenhouse" ||
                name.StartsWith("Cellar") || name.StartsWith("Cabin"));

        private int GetSafeReturnX(string loc) => loc switch { "FarmHouse" => 9, "FarmCave" => 4, "Greenhouse" => 10, _ => 64 };
        private int GetSafeReturnY(string loc) => loc switch { "FarmHouse" => 9, "FarmCave" => 10, "Greenhouse" => 23, _ => 15 };

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

                // Check zone plates
                foreach (var zone in config.Zones)
                {
                    if (zone.Plate == null) continue;
                    if (locName != zone.Plate.LocationName) continue;
                    if (tileX != zone.Plate.X || tileY != zone.Plate.Y) continue;

                    // Plate found!
                    if (zone.UnlockType == "permanent" && stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
                    {
                        // Plate already completed — don't show anything (plate has "disappeared")
                        return;
                    }

                    // Open purchase menu focused on this zone
                    Game1.activeClickableMenu = new BundleMenu(config, stateManager, purchaseEnabled: true, focusZoneId: zone.ZoneId);
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
                Game1.activeClickableMenu = new BundleMenu(config, stateManager, purchaseEnabled: false);
                Game1.playSound("bigSelect");
            }
        }

        // ── Plate + sign rendering ───────────────────────────────────

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            string locName = Game1.currentLocation.Name;
            SpriteBatch b = e.SpriteBatch;

            // Draw zone plates in the current location
            foreach (var zone in config.Zones)
            {
                if (zone.Plate == null) continue;
                if (zone.Plate.LocationName != locName) continue;

                // Don't draw plate if permanently unlocked (plate "disappeared")
                if (zone.UnlockType == "permanent" && stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
                    continue;

                DrawPlateSprite(b, zone);
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

        private void DrawPlateSprite(SpriteBatch b, ZoneDefinition zone)
        {
            // World position of the tile
            Vector2 worldPos = new(zone.Plate.X * 64, zone.Plate.Y * 64);

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
