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
        private readonly Action<string> onRequestZoneEdit;
        private readonly Action<string> onRequestBundleEdit;

        private int selectedIndex;
        private int scrollOffset;
        private int maxVisibleRows;
        private string statusMessage = "";
        private int statusMessageTimer;
        private bool statusIsError;
        private bool waitingForResponse;

        private ClickableTextureComponent purchaseButton;
        private Rectangle contributeButton;
        private ClickableTextureComponent upArrow;
        private ClickableTextureComponent downArrow;
        private List<ClickableComponent> zoneSlots = new();
        private List<ClickableTextureComponent> reorderUpButtons = new();
        private List<ClickableTextureComponent> reorderDownButtons = new();
        private List<ZoneDefinition> orderedZones = new();
        private List<CustomBundle> customBundles = new();

        private Rectangle leftPanelRect;
        private Rectangle rightPanelRect;

        private readonly Dictionary<string, Item> itemCache = new();

        /// <param name="purchaseEnabled">False = read-only view (K key), True = can purchase (plate interaction).</param>
        /// <param name="focusZoneId">If set, auto-select this zone on open.</param>
        public BundleMenu(ModConfig config, ZoneStateManager stateManager, bool purchaseEnabled = true, string focusZoneId = null,
            Action<string> onRequestPlatePlacement = null, Action<string> onRequestZoneEdit = null, Action<string> onRequestBundleEdit = null)
            : base(
                  (Game1.uiViewport.Width - MenuWidth) / 2,
                  (Game1.uiViewport.Height - MenuHeight) / 2,
                  MenuWidth, MenuHeight, showUpperRightCloseButton: true)
        {
            this.config = config;
            this.stateManager = stateManager;
            this.purchaseEnabled = purchaseEnabled;
            this.onRequestPlatePlacement = onRequestPlatePlacement;
            this.onRequestZoneEdit = onRequestZoneEdit;
            this.onRequestBundleEdit = onRequestBundleEdit;

            stateManager.OnPurchaseResponse = OnPurchaseResponse;

            RefreshSidebar();
            foreach (var zone in orderedZones)
            {
                foreach (var itemCost in zone.Items)
                    CacheItem(itemCost.ItemId);
                foreach (var itemCost in stateManager.GetEffectiveItems(zone))
                    CacheItem(itemCost.ItemId);
                foreach (var itemCost in stateManager.GetRewards(zone))
                    CacheItem(itemCost.ItemId);
            }
            foreach (var bundle in customBundles)
            {
                foreach (var item in bundle.Items) CacheItem(item.ItemId);
                foreach (var item in bundle.Rewards) CacheItem(item.ItemId);
            }

            SetupLayout();

            if (focusZoneId != null)
            {
                int idx = orderedZones.FindIndex(z => z.ZoneId == focusZoneId);
                if (idx >= 0)
                {
                    selectedIndex = idx;
                    if (idx >= maxVisibleRows) scrollOffset = idx - maxVisibleRows + 1;
                }
            }
        }

        private int TotalEntries => orderedZones.Count + customBundles.Count + (onRequestBundleEdit != null ? 1 : 0);
        private bool IsZoneIndex(int idx) => idx >= 0 && idx < orderedZones.Count;
        private bool IsBundleIndex(int idx) => idx >= orderedZones.Count && idx < orderedZones.Count + customBundles.Count;
        private bool IsNewBundleIndex(int idx) => onRequestBundleEdit != null && idx == orderedZones.Count + customBundles.Count;
        private CustomBundle GetBundleAt(int idx) => customBundles[idx - orderedZones.Count];

        private void RefreshSidebar()
        {
            orderedZones = stateManager.GetOrderedZones();
            customBundles = stateManager.GetCustomBundles().ToList();
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
            reorderUpButtons.Clear();
            reorderDownButtons.Clear();
            bool canReorder = onRequestZoneEdit != null; // host-only (same gate as edit/move)
            // Reserve column for reorder buttons LEFT of scroll arrow column (right-most 44px reserved for scroll arrows)
            const int ScrollArrowColW = 48;
            const int ReorderBtnW = 32;
            int reorderColReserve = canReorder ? (ReorderBtnW + 8) : 0;
            int slotW = LeftPanelWidth - Padding * 2 - reorderColReserve - (canReorder ? ScrollArrowColW : 0);
            for (int i = 0; i < maxVisibleRows; i++)
            {
                int rowY = leftPanelRect.Y + Padding + i * ZoneRowHeight;
                zoneSlots.Add(new ClickableComponent(
                    new Rectangle(leftPanelRect.X + Padding, rowY, slotW, ZoneRowHeight - 4),
                    $"zone_{i}"));

                if (canReorder)
                {
                    int btnX = leftPanelRect.Right - ScrollArrowColW - ReorderBtnW - 4;
                    int btnH = (ZoneRowHeight - 4) / 2 - 2;
                    reorderUpButtons.Add(new ClickableTextureComponent(
                        new Rectangle(btnX, rowY, ReorderBtnW, btnH),
                        Game1.mouseCursors, new Rectangle(421, 459, 11, 12), 2.5f));
                    reorderDownButtons.Add(new ClickableTextureComponent(
                        new Rectangle(btnX, rowY + btnH + 4, ReorderBtnW, btnH),
                        Game1.mouseCursors, new Rectangle(421, 472, 11, 12), 2.5f));
                }
            }

            upArrow = new ClickableTextureComponent(
                new Rectangle(leftPanelRect.Right - 44, leftPanelRect.Y, 44, 48),
                Game1.mouseCursors, new Rectangle(421, 459, 11, 12), 4f);

            downArrow = new ClickableTextureComponent(
                new Rectangle(leftPanelRect.Right - 44, leftPanelRect.Bottom - 48, 44, 48),
                Game1.mouseCursors, new Rectangle(421, 472, 11, 12), 4f);

            int btnWidth = 260;
            int purchaseBtnX = rightPanelRect.X + (rightPanelRect.Width - btnWidth) / 2;
            int hostLinksReserve = onRequestPlatePlacement != null ? 80 : 0;
            int btnY = rightPanelRect.Bottom - ButtonHeight - Padding - hostLinksReserve;
            purchaseButton = new ClickableTextureComponent(
                new Rectangle(purchaseBtnX, btnY, btnWidth, ButtonHeight),
                Game1.mouseCursors, new Rectangle(256, 256, 10, 10), 4f);

            contributeButton = new Rectangle(purchaseBtnX, btnY - ButtonHeight - 8, btnWidth, ButtonHeight);
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            // Reorder up/down (host only, zones only)
            for (int i = 0; i < reorderUpButtons.Count; i++)
            {
                int dataIndex = scrollOffset + i;
                if (!IsZoneIndex(dataIndex)) break;
                if (reorderUpButtons[i].containsPoint(x, y) && dataIndex > 0)
                {
                    var zone = orderedZones[dataIndex];
                    if (stateManager.MoveZoneInOrder(zone.ZoneId, -1))
                    {
                        RefreshSidebar();
                        selectedIndex = dataIndex - 1;
                        Game1.playSound("shwip");
                    }
                    return;
                }
                if (reorderDownButtons[i].containsPoint(x, y) && dataIndex < orderedZones.Count - 1)
                {
                    var zone = orderedZones[dataIndex];
                    if (stateManager.MoveZoneInOrder(zone.ZoneId, 1))
                    {
                        RefreshSidebar();
                        selectedIndex = dataIndex + 1;
                        Game1.playSound("shwip");
                    }
                    return;
                }
            }

            for (int i = 0; i < zoneSlots.Count; i++)
            {
                if (zoneSlots[i].containsPoint(x, y))
                {
                    int dataIndex = scrollOffset + i;
                    if (IsNewBundleIndex(dataIndex))
                    {
                        onRequestBundleEdit.Invoke(null);
                        Game1.playSound("smallSelect");
                        exitThisMenu();
                        return;
                    }
                    if (dataIndex < TotalEntries) { selectedIndex = dataIndex; Game1.playSound("smallSelect"); }
                    return;
                }
            }

            if (upArrow.containsPoint(x, y) && scrollOffset > 0) { scrollOffset--; Game1.playSound("shwip"); return; }
            if (downArrow.containsPoint(x, y) && scrollOffset + maxVisibleRows < TotalEntries) { scrollOffset++; Game1.playSound("shwip"); return; }

            if (purchaseButton.containsPoint(x, y) && !waitingForResponse)
                TryPurchaseSelected();

            if (contributeButton.Contains(x, y) && !waitingForResponse)
                TryContributeSelected();

            // Host-only links below purchase button
            if (onRequestPlatePlacement != null && IsZoneIndex(selectedIndex))
            {
                string moveText = "Move Plate";
                string editText = "Edit Zone";
                Vector2 moveSize = Game1.smallFont.MeasureString(moveText);
                Vector2 editSize = Game1.smallFont.MeasureString(editText);
                int infoY = purchaseButton.bounds.Bottom + 12;
                int linkY = infoY + 28 + 4;
                int totalLinksWidth = (int)moveSize.X + 24 + (int)editSize.X;
                int linksStartX = purchaseButton.bounds.X + (purchaseButton.bounds.Width - totalLinksWidth) / 2;

                Rectangle moveArea = new(linksStartX, linkY, (int)moveSize.X, (int)moveSize.Y);
                if (moveArea.Contains(x, y))
                {
                    onRequestPlatePlacement.Invoke(orderedZones[selectedIndex].ZoneId);
                    Game1.playSound("smallSelect");
                    exitThisMenu();
                    return;
                }

                int editX = linksStartX + (int)moveSize.X + 24;
                Rectangle editArea = new(editX, linkY, (int)editSize.X, (int)editSize.Y);
                if (editArea.Contains(x, y) && onRequestZoneEdit != null)
                {
                    onRequestZoneEdit.Invoke(orderedZones[selectedIndex].ZoneId);
                    Game1.playSound("smallSelect");
                    exitThisMenu();
                    return;
                }
            }

            // Host-only "Edit Bundle" link for custom bundles
            if (onRequestBundleEdit != null && IsBundleIndex(selectedIndex))
            {
                string editText = "Edit Bundle";
                Vector2 editSize = Game1.smallFont.MeasureString(editText);
                int linkY = purchaseButton.bounds.Bottom + 12;
                int editX = purchaseButton.bounds.X + (purchaseButton.bounds.Width - (int)editSize.X) / 2;
                Rectangle editArea = new(editX, linkY, (int)editSize.X, (int)editSize.Y);
                if (editArea.Contains(x, y))
                {
                    onRequestBundleEdit.Invoke(GetBundleAt(selectedIndex).BundleId);
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
            else if (direction < 0 && scrollOffset + maxVisibleRows < TotalEntries) scrollOffset++;
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
            if (IsBundleIndex(selectedIndex))
            {
                TryPurchaseBundle(GetBundleAt(selectedIndex));
                return;
            }
            if (!IsZoneIndex(selectedIndex)) return;
            if (!purchaseEnabled) return;
            var zone = orderedZones[selectedIndex];
            var farmer = Game1.player;

            if (zone.UnlockType == "permanent" && stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
            { ShowStatus("Already unlocked!", true); return; }
            if (zone.UnlockType == "ticket" && stateManager.HasActiveTicket(zone.ZoneId, farmer.UniqueMultiplayerID))
            { ShowStatus("You already have a ticket for today!", true); return; }
            if (!stateManager.ArePrerequisitesMet(zone))
            {
                if (!string.IsNullOrEmpty(zone.RequiresZone) && !stateManager.IsZonePermanentlyUnlocked(zone.RequiresZone))
                {
                    var req = config.GetZoneById(zone.RequiresZone);
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

            var effectiveItems = stateManager.GetEffectiveItems(zone);
            foreach (var item in effectiveItems)
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

        private void TryPurchaseBundle(CustomBundle bundle)
        {
            if (bundle.IsCompleted) { ShowStatus("Already completed!", true); return; }
            var farmer = Game1.player;
            if (farmer.Money < bundle.MoneyCost) { ShowStatus("Not enough gold!", true); return; }
            foreach (var item in bundle.Items)
            {
                int have = 0;
                foreach (var inv in farmer.Items) if (inv != null && inv.QualifiedItemId == item.ItemId) have += inv.Stack;
                if (have < item.Count) { ShowStatus($"Need {item.Count}x {item.DisplayName} (have {have})", true); return; }
            }

            bool immediate = stateManager.TryPurchaseBundle(bundle.BundleId, farmer);
            if (immediate)
            {
                ShowStatus($"{bundle.DisplayName} completed!", false);
                Game1.playSound("purchaseClick");
                RefreshSidebar();
            }
            else { waitingForResponse = true; ShowStatus("Processing...", false); }
        }

        private void TryContributeSelected()
        {
            if (!IsZoneIndex(selectedIndex) || !purchaseEnabled) return;
            var zone = orderedZones[selectedIndex];
            if (zone.UnlockType != "permanent") return;
            if (stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
            { ShowStatus("Already unlocked!", true); return; }
            if (!stateManager.ArePrerequisitesMet(zone))
            { ShowStatus("Prerequisites not met!", true); return; }

            var farmer = Game1.player;
            int scaledCost = stateManager.GetScaledMoneyCost(zone);
            int remaining = scaledCost - stateManager.GetTotalContributions(zone.ZoneId);
            if (remaining <= 0) { ShowStatus("Already fully funded!", true); return; }

            int contribution = Math.Min(farmer.Money, remaining);
            if (contribution <= 0) { ShowStatus("Not enough gold!", true); return; }

            bool immediate = stateManager.TryContribute(zone.ZoneId, farmer, contribution);
            if (immediate)
            {
                ShowStatus($"Contributed {contribution}g!", false);
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

            if (IsZoneIndex(selectedIndex))
                DrawZoneDetails(b, orderedZones[selectedIndex]);
            else if (IsBundleIndex(selectedIndex))
                DrawBundleDetails(b, GetBundleAt(selectedIndex));

            if (scrollOffset > 0) upArrow.draw(b);
            if (scrollOffset + maxVisibleRows < TotalEntries) downArrow.draw(b);

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
                if (dataIndex >= TotalEntries) break;

                var slot = zoneSlots[i];
                bool isSelected = dataIndex == selectedIndex;

                if (IsNewBundleIndex(dataIndex))
                {
                    if (isSelected) b.Draw(Game1.fadeToBlackRect, slot.bounds, Color.Wheat * 0.4f);
                    b.DrawString(Game1.smallFont, "+ New Bundle", new Vector2(slot.bounds.X + 40, slot.bounds.Y + (ZoneRowHeight - 28) / 2), Color.SaddleBrown);
                    continue;
                }

                if (IsBundleIndex(dataIndex))
                {
                    var bundle = GetBundleAt(dataIndex);
                    if (isSelected) b.Draw(Game1.fadeToBlackRect, slot.bounds, Color.Wheat * 0.4f);
                    string icon = bundle.IsCompleted ? "✓" : "○";
                    Color icoColor = bundle.IsCompleted ? Color.LimeGreen : Color.Orange;
                    b.DrawString(Game1.dialogueFont, icon, new Vector2(slot.bounds.X + 4, slot.bounds.Y + (ZoneRowHeight - 36) / 2), icoColor);
                    Color nameCol = isSelected ? Color.Black : (bundle.IsCompleted ? Color.DarkGreen : Color.DarkGoldenrod);
                    b.DrawString(Game1.smallFont, bundle.DisplayName, new Vector2(slot.bounds.X + 40, slot.bounds.Y + (ZoneRowHeight - 28) / 2), nameCol);
                    continue;
                }

                if (!IsZoneIndex(dataIndex)) break;
                var zone = orderedZones[dataIndex];
                bool isPermanent = stateManager.IsZonePermanentlyUnlocked(zone.ZoneId);
                bool hasTicket = stateManager.HasActiveTicket(zone.ZoneId, Game1.player.UniqueMultiplayerID);
                bool isAccessible = isPermanent || hasTicket;

                if (isSelected)
                    b.Draw(Game1.fadeToBlackRect, slot.bounds, Color.Wheat * 0.4f);

                string statusIcon; Color iconColor;
                if (isPermanent) { statusIcon = "+"; iconColor = Color.LimeGreen; }
                else if (hasTicket) { statusIcon = "T"; iconColor = Color.LimeGreen; }
                else { statusIcon = "X"; iconColor = Color.Red; }

                b.DrawString(Game1.dialogueFont, statusIcon, new Vector2(slot.bounds.X + 4, slot.bounds.Y + (ZoneRowHeight - 36) / 2), iconColor);

                Color nameColor = isSelected ? Color.Black : (isAccessible ? Color.DarkGreen : Color.DarkRed);
                b.DrawString(Game1.smallFont, zone.DisplayName, new Vector2(slot.bounds.X + 40, slot.bounds.Y + (ZoneRowHeight - 28) / 2), nameColor);

                if (i < reorderUpButtons.Count && IsZoneIndex(dataIndex))
                {
                    bool canUp = dataIndex > 0;
                    bool canDown = dataIndex < orderedZones.Count - 1;
                    var upBtn = reorderUpButtons[i];
                    var dnBtn = reorderDownButtons[i];
                    var upTint = canUp ? Color.White : Color.Gray * 0.4f;
                    var dnTint = canDown ? Color.White : Color.Gray * 0.4f;
                    b.Draw(upBtn.texture, new Vector2(upBtn.bounds.X, upBtn.bounds.Y), upBtn.sourceRect, upTint, 0f, Vector2.Zero, upBtn.baseScale, SpriteEffects.None, 0.86f);
                    b.Draw(dnBtn.texture, new Vector2(dnBtn.bounds.X, dnBtn.bounds.Y), dnBtn.sourceRect, dnTint, 0f, Vector2.Zero, dnBtn.baseScale, SpriteEffects.None, 0.86f);
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
            int baseCost = stateManager.GetEffectiveBaseCost(zone);
            int scaledCost = stateManager.GetScaledMoneyCost(zone);
            bool canAffordGold = Game1.player.Money >= scaledCost;
            b.Draw(Game1.mouseCursors, new Vector2(x, y - 2), new Rectangle(193, 373, 9, 10), Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
            string costText = scaledCost != baseCost
                ? $" {scaledCost:N0}g (base {baseCost:N0}g)"
                : $" {scaledCost:N0}g";
            b.DrawString(Game1.smallFont, costText, new Vector2(x + 30, y), canAffordGold ? Color.DarkGreen : Color.DarkRed);
            y += 36;

            if (zone.UnlockType == "permanent" && !stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
            {
                int totalContributed = stateManager.GetTotalContributions(zone.ZoneId);
                if (totalContributed > 0)
                {
                    float progress = Math.Min(1f, (float)totalContributed / scaledCost);
                    b.DrawString(Game1.smallFont, $"Pooled: {totalContributed:N0} / {scaledCost:N0}g", new Vector2(x, y), Color.DarkSlateGray);
                    y += 26;
                    int barWidth = Math.Min(contentWidth, 280);
                    int barHeight = 16;
                    b.Draw(Game1.fadeToBlackRect, new Rectangle(x, y, barWidth, barHeight), Color.Gray * 0.4f);
                    b.Draw(Game1.fadeToBlackRect, new Rectangle(x, y, (int)(barWidth * progress), barHeight), Color.Gold);
                    b.Draw(Game1.fadeToBlackRect, new Rectangle(x, y, barWidth, 2), Color.SaddleBrown * 0.6f);
                    b.Draw(Game1.fadeToBlackRect, new Rectangle(x, y + barHeight - 2, barWidth, 2), Color.SaddleBrown * 0.6f);
                    b.Draw(Game1.fadeToBlackRect, new Rectangle(x, y, 2, barHeight), Color.SaddleBrown * 0.6f);
                    b.Draw(Game1.fadeToBlackRect, new Rectangle(x + barWidth - 2, y, 2, barHeight), Color.SaddleBrown * 0.6f);
                    y += barHeight + 12;
                }
            }

            // Item costs with icons (using effective items from overrides)
            var effectiveItems = stateManager.GetEffectiveItems(zone);
            if (effectiveItems.Count > 0)
            {
                b.DrawString(Game1.smallFont, "Items required:", new Vector2(x, y), Color.Black);
                y += 28;

                foreach (var item in effectiveItems)
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

            // Rewards
            var rewards = stateManager.GetRewards(zone);
            if (rewards.Count > 0)
            {
                b.DrawString(Game1.smallFont, "Rewards:", new Vector2(x, y), Color.Black);
                y += 28;

                foreach (var reward in rewards)
                {
                    int textX = x + 8;
                    if (itemCache.TryGetValue(reward.ItemId, out var cachedReward))
                    {
                        cachedReward.drawInMenu(b, new Vector2(x - 20, y - 24), 0.5f, 1f, 0.9f, StackDrawType.Hide);
                        textX = x + 24;
                    }
                    b.DrawString(Game1.smallFont, $"{reward.DisplayName} x{reward.Count}", new Vector2(textX, y), Color.DarkGoldenrod);
                    y += 36;
                }
            }

            // Prerequisites: zone requirement
            if (!string.IsNullOrEmpty(zone.RequiresZone))
            {
                var req = config.GetZoneById(zone.RequiresZone);
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

            // Mine level gates (Mine zone only)
            if (zone.ZoneId == "Mine")
            {
                var gates = stateManager.GetEffectiveMineLevelGates();
                if (gates.Count > 0)
                {
                    int collectiveMining = stateManager.GetCollectiveSkillLevel("Mining");
                    b.DrawString(Game1.smallFont, "Mine Floor Gates:", new Vector2(x, y), Color.Black);
                    y += 26;
                    foreach (var gate in gates.OrderBy(g => g.FloorNumber))
                    {
                        bool met = collectiveMining >= gate.RequiredMiningLevel;
                        string check = met ? "\u2713 " : "";
                        b.DrawString(Game1.smallFont, $"{check}Floor {gate.FloorNumber}: Mining Lv {gate.RequiredMiningLevel}",
                            new Vector2(x + 8, y), met ? Color.DarkGreen : Color.DarkRed);
                        y += 24;
                    }
                    y += 4;
                }
            }

            // Status
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

            if (purchaseEnabled && zone.UnlockType == "permanent" && !stateManager.IsZonePermanentlyUnlocked(zone.ZoneId) && stateManager.ArePrerequisitesMet(zone))
            {
                bool canContribute = Game1.player.Money > 0 && !waitingForResponse;
                Color cBtnColor = canContribute ? Color.White : Color.Gray * 0.5f;
                drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
                    contributeButton.X, contributeButton.Y, contributeButton.Width, contributeButton.Height,
                    cBtnColor, 4f, drawShadow: true);
                string cBtnText = "Contribute Gold";
                Vector2 cBtnSize = Game1.smallFont.MeasureString(cBtnText);
                b.DrawString(Game1.smallFont, cBtnText,
                    new Vector2(contributeButton.X + (contributeButton.Width - cBtnSize.X) / 2,
                                contributeButton.Y + (contributeButton.Height - cBtnSize.Y) / 2),
                    canContribute ? Color.DarkSlateGray : Color.Gray);
            }

            // Host-only links: "Move Plate" and "Edit Zone"
            if (onRequestPlatePlacement != null)
            {
                var plate = stateManager.GetEffectivePlate(zone);
                string plateInfo = plate != null ? $"{plate.LocationName} ({plate.X}, {plate.Y})" : "not set";
                int infoY = purchaseButton.bounds.Bottom + 12;

                // Plate location info
                string infoText = $"Plate: {plateInfo}";
                Vector2 infoSize = Game1.smallFont.MeasureString(infoText);
                b.DrawString(Game1.smallFont, infoText,
                    new Vector2(purchaseButton.bounds.X + (purchaseButton.bounds.Width - infoSize.X) / 2, infoY),
                    Color.Gray);

                // "Move Plate" and "Edit Zone" links side by side
                string moveText = "Move Plate";
                string editText = "Edit Zone";
                Vector2 moveSize = Game1.smallFont.MeasureString(moveText);
                Vector2 editSize = Game1.smallFont.MeasureString(editText);
                int linkY = infoY + 28 + 4;
                int totalLinksWidth = (int)moveSize.X + 24 + (int)editSize.X;
                int linksStartX = purchaseButton.bounds.X + (purchaseButton.bounds.Width - totalLinksWidth) / 2;

                // "Move Plate" link
                b.DrawString(Game1.smallFont, moveText, new Vector2(linksStartX, linkY) + new Vector2(1, 1), Color.Black * 0.3f, 0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
                b.DrawString(Game1.smallFont, moveText, new Vector2(linksStartX, linkY), Color.SaddleBrown, 0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);

                // "Edit Zone" link
                int editX = linksStartX + (int)moveSize.X + 24;
                b.DrawString(Game1.smallFont, editText, new Vector2(editX, linkY) + new Vector2(1, 1), Color.Black * 0.3f, 0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
                b.DrawString(Game1.smallFont, editText, new Vector2(editX, linkY), Color.SaddleBrown, 0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
            }
        }

        private void DrawBundleDetails(SpriteBatch b, CustomBundle bundle)
        {
            int x = rightPanelRect.X + Padding;
            int y = rightPanelRect.Y + Padding;
            int contentWidth = rightPanelRect.Width - Padding * 2;

            b.DrawString(Game1.dialogueFont, bundle.DisplayName, new Vector2(x, y), Color.SaddleBrown);
            y += 48;

            if (!string.IsNullOrEmpty(bundle.Description))
            {
                string desc = Game1.parseText(bundle.Description, Game1.smallFont, contentWidth);
                b.DrawString(Game1.smallFont, desc, new Vector2(x, y), Color.DarkSlateGray);
                y += (int)Game1.smallFont.MeasureString(desc).Y + 16;
            }

            b.Draw(Game1.fadeToBlackRect, new Rectangle(x, y, contentWidth, 2), Color.SaddleBrown * 0.5f);
            y += 12;

            b.DrawString(Game1.smallFont, "Type: Custom Bundle", new Vector2(x, y), Color.Black);
            y += 32;

            if (bundle.MoneyCost > 0)
            {
                bool canAfford = Game1.player.Money >= bundle.MoneyCost;
                b.Draw(Game1.mouseCursors, new Vector2(x, y - 2), new Rectangle(193, 373, 9, 10), Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
                b.DrawString(Game1.smallFont, $" {bundle.MoneyCost:N0}g", new Vector2(x + 30, y), canAfford ? Color.DarkGreen : Color.DarkRed);
                y += 36;
            }

            if (bundle.Items.Count > 0)
            {
                b.DrawString(Game1.smallFont, "Items required:", new Vector2(x, y), Color.Black);
                y += 28;
                foreach (var item in bundle.Items)
                {
                    int have = 0;
                    foreach (var inv in Game1.player.Items)
                        if (inv != null && inv.QualifiedItemId == item.ItemId) have += inv.Stack;
                    bool hasEnough = have >= item.Count;
                    int textX = x + 8;
                    if (itemCache.TryGetValue(item.ItemId, out var cached))
                    {
                        cached.drawInMenu(b, new Vector2(x - 20, y - 24), 0.5f, 1f, 0.9f, StackDrawType.Hide);
                        textX = x + 24;
                    }
                    string check = hasEnough ? "✓ " : "";
                    b.DrawString(Game1.smallFont, $"{check}{item.DisplayName}: {have}/{item.Count}", new Vector2(textX, y), hasEnough ? Color.DarkGreen : Color.DarkRed);
                    y += 36;
                }
            }

            if (bundle.Rewards.Count > 0)
            {
                b.DrawString(Game1.smallFont, "Rewards:", new Vector2(x, y), Color.Black);
                y += 28;
                foreach (var reward in bundle.Rewards)
                {
                    int textX = x + 8;
                    if (itemCache.TryGetValue(reward.ItemId, out var cached))
                    {
                        cached.drawInMenu(b, new Vector2(x - 20, y - 24), 0.5f, 1f, 0.9f, StackDrawType.Hide);
                        textX = x + 24;
                    }
                    b.DrawString(Game1.smallFont, $"{reward.DisplayName} x{reward.Count}", new Vector2(textX, y), Color.DarkGoldenrod);
                    y += 36;
                }
            }

            y += 8;
            string status = bundle.IsCompleted ? "COMPLETED" : "INCOMPLETE";
            Color statusColor = bundle.IsCompleted ? Color.LimeGreen : Color.Orange;
            b.DrawString(Game1.dialogueFont, status, new Vector2(x, y), statusColor);

            // Purchase button
            bool canPurchase = !bundle.IsCompleted && !waitingForResponse;
            Color btnColor = canPurchase ? Color.White : Color.Gray * 0.5f;
            drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
                purchaseButton.bounds.X, purchaseButton.bounds.Y, purchaseButton.bounds.Width, purchaseButton.bounds.Height,
                btnColor, 4f, drawShadow: true);

            string btnText = bundle.IsCompleted ? "Completed" : "Complete Bundle";
            Vector2 btnTextSize = Game1.smallFont.MeasureString(btnText);
            b.DrawString(Game1.smallFont, btnText,
                new Vector2(purchaseButton.bounds.X + (purchaseButton.bounds.Width - btnTextSize.X) / 2,
                            purchaseButton.bounds.Y + (purchaseButton.bounds.Height - btnTextSize.Y) / 2),
                canPurchase ? Color.DarkSlateGray : Color.Gray);

            // Host-only "Edit Bundle" link
            if (onRequestBundleEdit != null)
            {
                string editText = "Edit Bundle";
                Vector2 editSize = Game1.smallFont.MeasureString(editText);
                int linkY = purchaseButton.bounds.Bottom + 12;
                int editX = purchaseButton.bounds.X + (purchaseButton.bounds.Width - (int)editSize.X) / 2;
                b.DrawString(Game1.smallFont, editText, new Vector2(editX, linkY) + new Vector2(1, 1), Color.Black * 0.3f, 0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
                b.DrawString(Game1.smallFont, editText, new Vector2(editX, linkY), Color.SaddleBrown, 0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
            }
        }

        private bool CanPurchase(ZoneDefinition zone)
        {
            if (waitingForResponse) return false;
            if (zone.UnlockType == "permanent" && stateManager.IsZonePermanentlyUnlocked(zone.ZoneId)) return false;
            if (zone.UnlockType == "ticket" && stateManager.HasActiveTicket(zone.ZoneId, Game1.player.UniqueMultiplayerID)) return false;
            if (!stateManager.ArePrerequisitesMet(zone)) return false;
            return true;
        }

        private void CacheItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || itemCache.ContainsKey(itemId)) return;
            try { var item = ItemRegistry.Create(itemId); if (item != null) itemCache[itemId] = item; } catch { }
        }
    }
}
