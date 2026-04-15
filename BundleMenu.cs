using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace ZoneLockChallenge
{
    public class BundleMenu : IClickableMenu
    {
        private const int MenuWidth = 1100;
        private const int MenuHeight = 680;
        private const int LeftPanelWidth = 340;
        private const int Padding = 16;
        private const int ZoneRowHeight = 56;
        private const int ButtonHeight = 64;

        private readonly ModConfig config;
        private readonly ZoneStateManager stateManager;
        private readonly bool purchaseEnabled;
        private readonly Action<string> onRequestPlatePlacement;

        private int selectedIndex;
        private int scrollOffset;
        private int maxVisibleRows;
        private string statusMessage = "";
        private int statusMessageTimer;
        private bool statusIsError;
        private bool waitingForResponse;

        private ClickableTextureComponent purchaseButton;
        private ClickableTextureComponent upArrow;
        private ClickableTextureComponent downArrow;
        private List<ClickableComponent> zoneSlots = new();

        private Rectangle leftPanelRect;
        private Rectangle rightPanelRect;

        private readonly Dictionary<string, Item> itemCache = new();

        /// <param name="purchaseEnabled">False = read-only view (K key), True = can purchase (plate interaction).</param>
        /// <param name="focusZoneId">If set, auto-select this zone on open.</param>
        public BundleMenu(ModConfig config, ZoneStateManager stateManager, bool purchaseEnabled = true, string focusZoneId = null, Action<string> onRequestPlatePlacement = null)
            : base(
                  (Game1.uiViewport.Width - MenuWidth) / 2,
                  (Game1.uiViewport.Height - MenuHeight) / 2,
                  MenuWidth, MenuHeight, showUpperRightCloseButton: true)
        {
            this.config = config;
            this.stateManager = stateManager;
            this.purchaseEnabled = purchaseEnabled;
            this.onRequestPlatePlacement = onRequestPlatePlacement;

            stateManager.OnPurchaseResponse = OnPurchaseResponse;

            foreach (var zone in config.Zones)
                foreach (var itemCost in zone.Items)
                    if (!itemCache.ContainsKey(itemCost.ItemId))
                        try { var item = ItemRegistry.Create(itemCost.ItemId); if (item != null) itemCache[itemCost.ItemId] = item; } catch { }

            SetupLayout();

            // Auto-select a zone if requested
            if (focusZoneId != null)
            {
                int idx = config.Zones.FindIndex(z => z.ZoneId == focusZoneId);
                if (idx >= 0)
                {
                    selectedIndex = idx;
                    if (idx >= maxVisibleRows) scrollOffset = idx - maxVisibleRows + 1;
                }
            }
        }

        private void SetupLayout()
        {
            int innerX = xPositionOnScreen + spaceToClearSideBorder + Padding;
            int innerY = yPositionOnScreen + spaceToClearTopBorder + Padding + 48;
            int innerWidth = width - (spaceToClearSideBorder + Padding) * 2;
            int innerHeight = height - spaceToClearTopBorder - Padding * 2 - 48 - spaceToClearSideBorder;

            leftPanelRect = new Rectangle(innerX, innerY, LeftPanelWidth, innerHeight);
            rightPanelRect = new Rectangle(innerX + LeftPanelWidth + Padding, innerY, innerWidth - LeftPanelWidth - Padding, innerHeight);

            maxVisibleRows = (leftPanelRect.Height - Padding * 2) / ZoneRowHeight;

            zoneSlots.Clear();
            for (int i = 0; i < maxVisibleRows; i++)
                zoneSlots.Add(new ClickableComponent(
                    new Rectangle(leftPanelRect.X + Padding, leftPanelRect.Y + Padding + i * ZoneRowHeight, LeftPanelWidth - Padding * 2, ZoneRowHeight - 4),
                    $"zone_{i}"));

            upArrow = new ClickableTextureComponent(
                new Rectangle(leftPanelRect.Right - 44, leftPanelRect.Y, 44, 48),
                Game1.mouseCursors, new Rectangle(421, 459, 11, 12), 4f);

            downArrow = new ClickableTextureComponent(
                new Rectangle(leftPanelRect.Right - 44, leftPanelRect.Bottom - 48, 44, 48),
                Game1.mouseCursors, new Rectangle(421, 472, 11, 12), 4f);

            int btnWidth = 260;
            int btnX = rightPanelRect.X + (rightPanelRect.Width - btnWidth) / 2;
            int btnY = rightPanelRect.Bottom - ButtonHeight - Padding;
            purchaseButton = new ClickableTextureComponent(
                new Rectangle(btnX, btnY, btnWidth, ButtonHeight),
                Game1.mouseCursors, new Rectangle(256, 256, 10, 10), 4f);
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            for (int i = 0; i < zoneSlots.Count; i++)
            {
                if (zoneSlots[i].containsPoint(x, y))
                {
                    int dataIndex = scrollOffset + i;
                    if (dataIndex < config.Zones.Count) { selectedIndex = dataIndex; Game1.playSound("smallSelect"); }
                    return;
                }
            }

            if (upArrow.containsPoint(x, y) && scrollOffset > 0) { scrollOffset--; Game1.playSound("shwip"); return; }
            if (downArrow.containsPoint(x, y) && scrollOffset + maxVisibleRows < config.Zones.Count) { scrollOffset++; Game1.playSound("shwip"); return; }

            if (purchaseEnabled && purchaseButton.containsPoint(x, y) && !waitingForResponse)
                TryPurchaseSelected();

            // "Move Plate" click area (host only, below purchase button)
            if (onRequestPlatePlacement != null && selectedIndex >= 0 && selectedIndex < config.Zones.Count)
            {
                string moveText = "Move Plate";
                Vector2 moveSize = Game1.smallFont.MeasureString(moveText);
                int moveX = purchaseButton.bounds.X + (purchaseButton.bounds.Width - (int)moveSize.X) / 2;
                // Must match the draw position: info line at +12, then move link below that
                int infoY = purchaseButton.bounds.Bottom + 12;
                int linkY = infoY + 28 + 4; // 28px for info text line height
                Rectangle moveArea = new(moveX, linkY, (int)moveSize.X, (int)moveSize.Y);
                if (moveArea.Contains(x, y))
                {
                    var zone = config.Zones[selectedIndex];
                    onRequestPlatePlacement.Invoke(zone.ZoneId);
                    Game1.playSound("smallSelect");
                    exitThisMenu();
                    return;
                }
            }
        }

        public override void receiveScrollWheelAction(int direction)
        {
            base.receiveScrollWheelAction(direction);
            if (direction > 0 && scrollOffset > 0) scrollOffset--;
            else if (direction < 0 && scrollOffset + maxVisibleRows < config.Zones.Count) scrollOffset++;
        }

        public override void receiveKeyPress(Keys key)
        {
            base.receiveKeyPress(key);
            if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key))
                exitThisMenu();
        }

        public override void update(GameTime time)
        {
            base.update(time);
            if (statusMessageTimer > 0) { statusMessageTimer -= time.ElapsedGameTime.Milliseconds; if (statusMessageTimer <= 0) statusMessage = ""; }
        }

        private void TryPurchaseSelected()
        {
            if (selectedIndex < 0 || selectedIndex >= config.Zones.Count) return;
            var zone = config.Zones[selectedIndex];
            var farmer = Game1.player;

            if (zone.UnlockType == "permanent" && stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
            { ShowStatus("Already unlocked!", true); return; }
            if (zone.UnlockType == "ticket" && stateManager.HasActiveTicket(zone.ZoneId, farmer.UniqueMultiplayerID))
            { ShowStatus("You already have a ticket for today!", true); return; }
            if (!stateManager.ArePrerequisitesMet(zone))
            {
                // Check which prerequisite is not met for a specific error message
                if (!string.IsNullOrEmpty(zone.RequiresZone) && !stateManager.IsZonePermanentlyUnlocked(zone.RequiresZone))
                {
                    var req = config.Zones.FirstOrDefault(z => z.ZoneId == zone.RequiresZone);
                    ShowStatus($"Requires: {req?.DisplayName ?? zone.RequiresZone}", true);
                }
                else if (!string.IsNullOrEmpty(zone.RequiredSkill) && zone.RequiredSkillLevel > 0)
                {
                    int current = stateManager.GetCollectiveSkillLevel(zone.RequiredSkill);
                    ShowStatus($"Need collective {zone.RequiredSkill} level {zone.RequiredSkillLevel} (have {current})", true);
                }
                else
                {
                    ShowStatus("Prerequisites not met!", true);
                }
                return;
            }
            int scaledCost = stateManager.GetScaledMoneyCost(zone);
            if (farmer.Money < scaledCost) { ShowStatus("Not enough gold!", true); return; }

            foreach (var item in zone.Items)
            {
                int have = 0;
                foreach (var inv in farmer.Items) if (inv != null && inv.QualifiedItemId == item.ItemId) have += inv.Stack;
                if (have < item.Count) { ShowStatus($"Need {item.Count}x {item.DisplayName} (have {have})", true); return; }
            }

            bool immediate = stateManager.TryPurchase(zone.ZoneId, farmer);
            if (immediate)
            {
                string msg = zone.UnlockType == "permanent" ? $"{zone.DisplayName} unlocked!" : $"Ticket for {zone.DisplayName} purchased!";
                ShowStatus(msg, false);
                Game1.playSound("purchaseClick");
            }
            else { waitingForResponse = true; ShowStatus("Processing...", false); }
        }

        private void OnPurchaseResponse(ZonePurchaseResponse response)
        {
            waitingForResponse = false;
            ShowStatus(response.Message, !response.Success);
            Game1.playSound(response.Success ? "purchaseClick" : "cancel");
        }

        private void ShowStatus(string message, bool isError)
        {
            statusMessage = message;
            statusIsError = isError;
            statusMessageTimer = 3000;
            if (isError) Game1.playSound("cancel");
        }

        // ── Drawing ──────────────────────────────────────────────────

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.6f);
            Game1.drawDialogueBox(xPositionOnScreen, yPositionOnScreen, width, height, false, true);

            string title = purchaseEnabled ? "Zone Control Board" : "Zone Overview (Read Only)";
            SpriteText.drawStringWithScrollCenteredAt(b, title, xPositionOnScreen + width / 2, yPositionOnScreen + spaceToClearTopBorder - 4);

            DrawPanelBackground(b, leftPanelRect);
            DrawZoneList(b);
            DrawPanelBackground(b, rightPanelRect);

            if (selectedIndex >= 0 && selectedIndex < config.Zones.Count)
                DrawZoneDetails(b, config.Zones[selectedIndex]);

            if (scrollOffset > 0) upArrow.draw(b);
            if (scrollOffset + maxVisibleRows < config.Zones.Count) downArrow.draw(b);

            if (!string.IsNullOrEmpty(statusMessage))
            {
                Color msgColor = statusIsError ? Color.Red : Color.LimeGreen;
                Vector2 msgSize = Game1.smallFont.MeasureString(statusMessage);
                b.DrawString(Game1.smallFont, statusMessage,
                    new Vector2(rightPanelRect.X + (rightPanelRect.Width - (int)msgSize.X) / 2, purchaseButton.bounds.Y - (int)msgSize.Y - 8), msgColor);
            }

            drawMouse(b);
        }

        private void DrawPanelBackground(SpriteBatch b, Rectangle rect)
        {
            drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 373, 18, 18), rect.X, rect.Y, rect.Width, rect.Height, Color.White, 4f, drawShadow: false);
        }

        private void DrawZoneList(SpriteBatch b)
        {
            for (int i = 0; i < maxVisibleRows; i++)
            {
                int dataIndex = scrollOffset + i;
                if (dataIndex >= config.Zones.Count) break;

                var zone = config.Zones[dataIndex];
                var slot = zoneSlots[i];
                bool isSelected = dataIndex == selectedIndex;
                bool isPermanent = stateManager.IsZonePermanentlyUnlocked(zone.ZoneId);
                bool hasTicket = stateManager.HasActiveTicket(zone.ZoneId, Game1.player.UniqueMultiplayerID);
                bool isAccessible = isPermanent || hasTicket;

                if (isSelected)
                    b.Draw(Game1.fadeToBlackRect, slot.bounds, Color.Wheat * 0.4f);

                // Status icon
                string statusIcon; Color iconColor;
                if (isPermanent) { statusIcon = "+"; iconColor = Color.LimeGreen; }
                else if (hasTicket) { statusIcon = "T"; iconColor = Color.LimeGreen; }
                else { statusIcon = "X"; iconColor = Color.Red; }

                b.DrawString(Game1.dialogueFont, statusIcon, new Vector2(slot.bounds.X + 4, slot.bounds.Y + (ZoneRowHeight - 36) / 2), iconColor);

                Color nameColor = isSelected ? Color.Black : (isAccessible ? Color.DarkGreen : Color.DarkRed);
                b.DrawString(Game1.smallFont, zone.DisplayName, new Vector2(slot.bounds.X + 40, slot.bounds.Y + (ZoneRowHeight - 28) / 2), nameColor);

                if (zone.UnlockType == "ticket")
                {
                    string tag = "[TICKET]";
                    Vector2 tagSize = Game1.smallFont.MeasureString(tag);
                    b.DrawString(Game1.smallFont, tag, new Vector2(slot.bounds.Right - tagSize.X - 4, slot.bounds.Y + (ZoneRowHeight - 28) / 2), Color.DarkGoldenrod);
                }
            }
        }

        private void DrawZoneDetails(SpriteBatch b, ZoneDefinition zone)
        {
            int x = rightPanelRect.X + Padding;
            int y = rightPanelRect.Y + Padding;
            int contentWidth = rightPanelRect.Width - Padding * 2;

            // Header: BundleName or DisplayName
            string headerName = !string.IsNullOrEmpty(zone.BundleName) ? zone.BundleName : zone.DisplayName;
            b.DrawString(Game1.dialogueFont, headerName, new Vector2(x, y), Color.SaddleBrown);
            y += 48;

            // Description
            string desc = Game1.parseText(zone.Description, Game1.smallFont, contentWidth);
            b.DrawString(Game1.smallFont, desc, new Vector2(x, y), Color.DarkSlateGray);
            y += (int)Game1.smallFont.MeasureString(desc).Y + 16;

            // Divider
            b.Draw(Game1.fadeToBlackRect, new Rectangle(x, y, contentWidth, 2), Color.SaddleBrown * 0.5f);
            y += 12;

            // Type
            string typeLabel = zone.UnlockType == "permanent" ? "Permanent Unlock" : "Daily Ticket";
            b.DrawString(Game1.smallFont, $"Type: {typeLabel}", new Vector2(x, y), Color.Black);
            y += 32;

            // Gold cost with coin icon (scaled by number of unlocked zones)
            int scaledCost = stateManager.GetScaledMoneyCost(zone);
            bool canAffordGold = Game1.player.Money >= scaledCost;
            b.Draw(Game1.mouseCursors, new Vector2(x, y - 2), new Rectangle(193, 373, 9, 10), Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
            string costText = scaledCost != zone.MoneyCost
                ? $" {scaledCost:N0}g (base {zone.MoneyCost:N0}g)"
                : $" {scaledCost:N0}g";
            b.DrawString(Game1.smallFont, costText, new Vector2(x + 30, y), canAffordGold ? Color.DarkGreen : Color.DarkRed);
            y += 36;

            // Item costs with icons
            if (zone.Items.Count > 0)
            {
                b.DrawString(Game1.smallFont, "Items required:", new Vector2(x, y), Color.Black);
                y += 28;

                foreach (var item in zone.Items)
                {
                    int have = 0;
                    foreach (var inv in Game1.player.Items)
                        if (inv != null && inv.QualifiedItemId == item.ItemId) have += inv.Stack;

                    bool hasEnough = have >= item.Count;
                    Color itemColor = hasEnough ? Color.DarkGreen : Color.DarkRed;
                    int textX = x + 8;

                    // Draw item icon aligned with text row
                    if (itemCache.TryGetValue(item.ItemId, out var cachedItem))
                    {
                        cachedItem.drawInMenu(b, new Vector2(x - 20, y - 24), 0.5f, 1f, 0.9f, StackDrawType.Hide);
                        textX = x + 24;
                    }

                    string check = hasEnough ? "\u2713 " : "";
                    b.DrawString(Game1.smallFont, $"{check}{item.DisplayName}: {have}/{item.Count}", new Vector2(textX, y), itemColor);
                    y += 36;
                }
            }
            else
            {
                b.DrawString(Game1.smallFont, "No items required", new Vector2(x, y), Color.DarkGreen);
                y += 32;
            }

            // Prerequisites: zone requirement
            if (!string.IsNullOrEmpty(zone.RequiresZone))
            {
                var req = config.Zones.FirstOrDefault(z => z.ZoneId == zone.RequiresZone);
                bool zoneMet = stateManager.IsZonePermanentlyUnlocked(zone.RequiresZone);
                b.DrawString(Game1.smallFont, $"Requires: {req?.DisplayName ?? zone.RequiresZone}", new Vector2(x, y), zoneMet ? Color.DarkGreen : Color.DarkRed);
                y += 32;
            }

            // Prerequisites: collective skill requirement
            if (!string.IsNullOrEmpty(zone.RequiredSkill) && zone.RequiredSkillLevel > 0)
            {
                int currentLevel = stateManager.GetCollectiveSkillLevel(zone.RequiredSkill);
                bool skillMet = currentLevel >= zone.RequiredSkillLevel;
                b.DrawString(Game1.smallFont, $"Collective {zone.RequiredSkill}: {currentLevel}/{zone.RequiredSkillLevel}",
                    new Vector2(x, y), skillMet ? Color.DarkGreen : Color.DarkRed);
                y += 32;
            }

            // Status — FIX: ticket active text is now green instead of yellow
            y += 8;
            string status; Color statusColor;
            if (stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
            { status = "UNLOCKED"; statusColor = Color.LimeGreen; }
            else if (stateManager.HasActiveTicket(zone.ZoneId, Game1.player.UniqueMultiplayerID))
            { status = "TICKET ACTIVE TODAY"; statusColor = Color.LimeGreen; }
            else
            { status = "LOCKED"; statusColor = Color.Red; }
            b.DrawString(Game1.dialogueFont, status, new Vector2(x, y), statusColor);

            // Purchase button (or read-only notice)
            if (purchaseEnabled)
            {
                bool canPurchase = CanPurchase(zone);
                Color btnColor = canPurchase ? Color.White : Color.Gray * 0.5f;

                drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
                    purchaseButton.bounds.X, purchaseButton.bounds.Y, purchaseButton.bounds.Width, purchaseButton.bounds.Height,
                    btnColor, 4f, drawShadow: true);

                string btnText;
                if (zone.UnlockType == "permanent" && stateManager.IsZonePermanentlyUnlocked(zone.ZoneId)) btnText = "Already Unlocked";
                else if (zone.UnlockType == "ticket" && stateManager.HasActiveTicket(zone.ZoneId, Game1.player.UniqueMultiplayerID)) btnText = "Ticket Active";
                else if (zone.UnlockType == "ticket") btnText = "Buy Ticket";
                else btnText = "Unlock Zone";

                Vector2 btnTextSize = Game1.smallFont.MeasureString(btnText);
                b.DrawString(Game1.smallFont, btnText,
                    new Vector2(purchaseButton.bounds.X + (purchaseButton.bounds.Width - btnTextSize.X) / 2,
                                purchaseButton.bounds.Y + (purchaseButton.bounds.Height - btnTextSize.Y) / 2),
                    canPurchase ? Color.DarkSlateGray : Color.Gray);
            }
            else
            {
                // Read-only mode: show hint about plates
                string hint = "Visit the zone plate to purchase";
                Vector2 hintSize = Game1.smallFont.MeasureString(hint);
                b.DrawString(Game1.smallFont, hint,
                    new Vector2(purchaseButton.bounds.X + (purchaseButton.bounds.Width - hintSize.X) / 2,
                                purchaseButton.bounds.Y + (purchaseButton.bounds.Height - hintSize.Y) / 2),
                    Color.Gray);
            }

            // "Move Plate" link (host only)
            if (onRequestPlatePlacement != null)
            {
                var plate = stateManager.GetEffectivePlate(zone);
                string plateInfo = plate != null ? $"{plate.LocationName} ({plate.X}, {plate.Y})" : "not set";
                string moveText = "Move Plate";
                Vector2 moveSize = Game1.smallFont.MeasureString(moveText);
                int moveX = purchaseButton.bounds.X + (purchaseButton.bounds.Width - (int)moveSize.X) / 2;
                int infoY = purchaseButton.bounds.Bottom + 12;

                // Plate location info
                string infoText = $"Plate: {plateInfo}";
                Vector2 infoSize = Game1.smallFont.MeasureString(infoText);
                b.DrawString(Game1.smallFont, infoText,
                    new Vector2(purchaseButton.bounds.X + (purchaseButton.bounds.Width - infoSize.X) / 2, infoY),
                    Color.Gray);

                // Clickable "Move Plate" text below (must match click area: infoY + 28 + 4)
                int linkY = infoY + 28 + 4;
                b.DrawString(Game1.smallFont, moveText, new Vector2(moveX, linkY) + new Vector2(1, 1), Color.Black * 0.3f, 0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
                b.DrawString(Game1.smallFont, moveText, new Vector2(moveX, linkY), Color.SaddleBrown, 0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
            }
        }

        private bool CanPurchase(ZoneDefinition zone)
        {
            if (!purchaseEnabled || waitingForResponse) return false;
            if (zone.UnlockType == "permanent" && stateManager.IsZonePermanentlyUnlocked(zone.ZoneId)) return false;
            if (zone.UnlockType == "ticket" && stateManager.HasActiveTicket(zone.ZoneId, Game1.player.UniqueMultiplayerID)) return false;
            if (!stateManager.ArePrerequisitesMet(zone)) return false;
            return true;
        }
    }
}
