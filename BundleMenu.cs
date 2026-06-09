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

        // Native Stardew UI sprites (Game1.mouseCursors)
        private static readonly Rectangle CheckedBox = new(236, 425, 9, 9);
        private static readonly Rectangle UncheckedBox = new(227, 425, 9, 9);
        private static readonly Rectangle CoinIcon = new(193, 373, 9, 10);
        private static readonly Rectangle UpArrowIcon = new(421, 459, 11, 12);
        private static readonly Rectangle DownArrowIcon = new(421, 472, 11, 12);
        private static readonly Color HighlightColor = Color.Wheat * 0.55f;

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
        private int responseTimeoutMs;
        private const int ResponseTimeoutTotalMs = 10000;
        /// <summary>Zone/bundle ID of the request this menu sent and is awaiting. Responses for
        /// other IDs (sent from a previously closed menu) are ignored.</summary>
        private string pendingRequestId;

        private bool showRunLog;
        private int logScrollOffset;
        private int logMaxVisible = 1;
        private int hoverIndex = -1;
        private Rectangle runLogTabRect;
        private Rectangle zonesTabRect;

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

            if (purchaseEnabled)
                stateManager.OnPurchaseResponse += OnPurchaseResponse;

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

        protected override void cleanupBeforeExit()
        {
            if (purchaseEnabled)
                stateManager.OnPurchaseResponse -= OnPurchaseResponse;
            base.cleanupBeforeExit();
        }

        internal void RefreshSidebar()
        {
            // Track the selection by ID: a sync from the host can reorder or add entries while
            // the menu is open, and a positional index would then point at the wrong zone.
            string prevZoneId = IsZoneIndex(selectedIndex) ? orderedZones[selectedIndex].ZoneId : null;
            string prevBundleId = IsBundleIndex(selectedIndex) ? GetBundleAt(selectedIndex).BundleId : null;
            bool prevWasNewBundleSlot = IsNewBundleIndex(selectedIndex);

            orderedZones = stateManager.GetOrderedZones();
            customBundles = stateManager.GetCustomBundles().ToList();

            if (prevZoneId != null)
            {
                int idx = orderedZones.FindIndex(z => z.ZoneId == prevZoneId);
                if (idx >= 0) selectedIndex = idx;
            }
            else if (prevBundleId != null)
            {
                int idx = customBundles.FindIndex(bd => bd.BundleId == prevBundleId);
                if (idx >= 0) selectedIndex = orderedZones.Count + idx;
            }
            else if (prevWasNewBundleSlot)
            {
                // The "+ New Bundle" slot moves when entries are added; follow it
                selectedIndex = TotalEntries - 1;
            }
            if (selectedIndex >= TotalEntries)
                selectedIndex = Math.Max(0, TotalEntries - 1);

            // Keep the scroll window valid and the selected row visible
            // (maxVisibleRows is 0 during the constructor's first call, before SetupLayout)
            if (maxVisibleRows > 0)
            {
                scrollOffset = Math.Min(scrollOffset, Math.Max(0, TotalEntries - maxVisibleRows));
                if (selectedIndex < scrollOffset)
                    scrollOffset = selectedIndex;
                else if (selectedIndex >= scrollOffset + maxVisibleRows)
                    scrollOffset = selectedIndex - maxVisibleRows + 1;
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

            int tabW = (rightPanelRect.Width - Padding * 2) / 2;
            int tabY = rightPanelRect.Y - 40;
            zonesTabRect = new Rectangle(rightPanelRect.X + Padding, tabY, tabW - 4, 36);
            runLogTabRect = new Rectangle(rightPanelRect.X + Padding + tabW + 4, tabY, tabW - 4, 36);
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            if (zonesTabRect.Contains(x, y) && showRunLog)
            { showRunLog = false; Game1.playSound("smallSelect"); return; }
            if (runLogTabRect.Contains(x, y) && !showRunLog)
            { showRunLog = true; Game1.playSound("smallSelect"); return; }

            if (showRunLog) return;

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
                        // Close before invoking: exitThisMenu() nulls Game1.activeClickableMenu,
                        // which would destroy the editor menu the callback opens.
                        Game1.playSound("smallSelect");
                        exitThisMenu();
                        onRequestBundleEdit.Invoke(null);
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
                    string zoneId = orderedZones[selectedIndex].ZoneId;
                    Game1.playSound("smallSelect");
                    exitThisMenu();
                    onRequestPlatePlacement.Invoke(zoneId);
                    return;
                }

                int editX = linksStartX + (int)moveSize.X + 24;
                Rectangle editArea = new(editX, linkY, (int)editSize.X, (int)editSize.Y);
                if (editArea.Contains(x, y) && onRequestZoneEdit != null)
                {
                    // Close before invoking: exitThisMenu() nulls Game1.activeClickableMenu,
                    // which would destroy the editor menu the callback opens.
                    string zoneId = orderedZones[selectedIndex].ZoneId;
                    Game1.playSound("smallSelect");
                    exitThisMenu();
                    onRequestZoneEdit.Invoke(zoneId);
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
                    // Close before invoking (see "Edit Zone" above)
                    string bundleId = GetBundleAt(selectedIndex).BundleId;
                    Game1.playSound("smallSelect");
                    exitThisMenu();
                    onRequestBundleEdit.Invoke(bundleId);
                    return;
                }
            }
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            hoverIndex = -1;
            if (showRunLog) return;
            for (int i = 0; i < zoneSlots.Count; i++)
            {
                if (zoneSlots[i].containsPoint(x, y))
                {
                    int dataIndex = scrollOffset + i;
                    if (dataIndex < TotalEntries) hoverIndex = dataIndex;
                    break;
                }
            }
        }

        public override void receiveScrollWheelAction(int direction)
        {
            base.receiveScrollWheelAction(direction);
            if (showRunLog)
            {
                var log = stateManager.GetRunLog();
                if (direction > 0 && logScrollOffset > 0) logScrollOffset--;
                else if (direction < 0 && logScrollOffset + logMaxVisible < log.Count) logScrollOffset++;
                return;
            }
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

            // Don't leave the buttons disabled forever if the host never answers (lag, disconnect).
            if (waitingForResponse)
            {
                responseTimeoutMs -= time.ElapsedGameTime.Milliseconds;
                if (responseTimeoutMs <= 0)
                {
                    waitingForResponse = false;
                    pendingRequestId = null;
                    ShowStatus("No response from the host — please try again.", true);
                }
            }
        }

        private void BeginWaitingForResponse(string requestId)
        {
            pendingRequestId = requestId;
            waitingForResponse = true;
            responseTimeoutMs = ResponseTimeoutTotalMs;
            ShowStatus("Processing...", false);
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
                    var req = stateManager.GetZoneById(zone.RequiresZone);
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
                int have = stateManager.CountItemInInventory(farmer, item.ItemId);
                if (have < item.Count) { ShowStatus($"Need {item.Count}x {item.DisplayName} (have {have})", true); return; }
            }

            bool immediate = stateManager.TryPurchase(zone.ZoneId, farmer);
            if (immediate)
            {
                string msg = zone.UnlockType == "permanent" ? $"{zone.DisplayName} unlocked!" : $"Ticket for {zone.DisplayName} purchased!";
                ShowStatus(msg, false);
                Game1.playSound("purchaseClick");
            }
            else BeginWaitingForResponse(zone.ZoneId);
        }

        private void TryPurchaseBundle(CustomBundle bundle)
        {
            if (bundle.IsCompleted) { ShowStatus("Already completed!", true); return; }
            var farmer = Game1.player;
            if (farmer.Money < bundle.MoneyCost) { ShowStatus("Not enough gold!", true); return; }
            foreach (var item in bundle.Items)
            {
                int have = stateManager.CountItemInInventory(farmer, item.ItemId);
                if (have < item.Count) { ShowStatus($"Need {item.Count}x {item.DisplayName} (have {have})", true); return; }
            }

            bool immediate = stateManager.TryPurchaseBundle(bundle.BundleId, farmer);
            if (immediate)
            {
                ShowStatus($"{bundle.DisplayName} completed!", false);
                Game1.playSound("purchaseClick");
                RefreshSidebar();
            }
            else BeginWaitingForResponse(bundle.BundleId);
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
            bool fullyFunded = remaining <= 0;
            var items = stateManager.GetEffectiveItems(zone);

            if (fullyFunded && items.Count == 0) { ShowStatus("Already fully funded!", true); return; }

            int contribution = fullyFunded ? 0 : Math.Min(farmer.Money, remaining);
            if (!fullyFunded && contribution <= 0) { ShowStatus("Not enough gold!", true); return; }

            if (fullyFunded)
            {
                // Gold goal is met — this click delivers the required items to finish the unlock.
                foreach (var item in items)
                {
                    int have = stateManager.CountItemInInventory(farmer, item.ItemId);
                    if (have < item.Count) { ShowStatus($"Need {item.Count}x {item.DisplayName} (have {have})", true); return; }
                }
            }

            bool immediate = stateManager.TryContribute(zone.ZoneId, farmer, contribution);
            if (immediate)
            {
                ShowStatus(contribution > 0 ? $"Contributed {contribution}g!" : $"{zone.DisplayName} unlocked!", false);
                Game1.playSound("purchaseClick");
            }
            else BeginWaitingForResponse(zone.ZoneId);
        }

        private void OnPurchaseResponse(ZonePurchaseResponse response)
        {
            // Ignore stale responses: ones for a menu that's no longer active, or for a request
            // sent from a previously closed menu (this menu never asked about that ID).
            if (Game1.activeClickableMenu != this) return;
            if (pendingRequestId == null || response.ZoneId != pendingRequestId) return;
            pendingRequestId = null;
            waitingForResponse = false;
            ShowStatus(response.Message, !response.Success);
            if (response.Success) Game1.playSound("purchaseClick");
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

            DrawTab(b, zonesTabRect, "Details", !showRunLog);
            DrawTab(b, runLogTabRect, "Run Log", showRunLog);

            if (showRunLog)
            {
                DrawRunLog(b);
            }
            else
            {
                if (IsZoneIndex(selectedIndex))
                    DrawZoneDetails(b, orderedZones[selectedIndex]);
                else if (IsBundleIndex(selectedIndex))
                    DrawBundleDetails(b, GetBundleAt(selectedIndex));

                if (!string.IsNullOrEmpty(statusMessage))
                {
                    Color msgColor = statusIsError ? Color.DarkRed : Color.Green;
                    Vector2 msgSize = Game1.smallFont.MeasureString(statusMessage);
                    b.DrawString(Game1.smallFont, statusMessage,
                        new Vector2(rightPanelRect.X + (rightPanelRect.Width - (int)msgSize.X) / 2, purchaseButton.bounds.Y - (int)msgSize.Y - 8), msgColor);
                }
            }

            if (scrollOffset > 0) upArrow.draw(b);
            if (scrollOffset + maxVisibleRows < TotalEntries) downArrow.draw(b);

            drawMouse(b);
        }

        private void DrawPanelBackground(SpriteBatch b, Rectangle rect)
        {
            drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 373, 18, 18), rect.X, rect.Y, rect.Width, rect.Height, Color.White, 4f, drawShadow: false);
        }

        private void DrawTab(SpriteBatch b, Rectangle rect, string label, bool active)
        {
            Color bg = active ? Color.White : Color.Gray * 0.6f;
            drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 373, 18, 18), rect.X, rect.Y, rect.Width, rect.Height, bg, 4f, drawShadow: false);
            Vector2 textSize = Game1.smallFont.MeasureString(label);
            b.DrawString(Game1.smallFont, label,
                new Vector2(rect.X + (rect.Width - textSize.X) / 2, rect.Y + (rect.Height - textSize.Y) / 2),
                active ? Color.SaddleBrown : Color.DarkGray);
        }

        private void DrawRunLog(SpriteBatch b)
        {
            int x = rightPanelRect.X + Padding;
            int y = rightPanelRect.Y + Padding;
            int contentWidth = rightPanelRect.Width - Padding * 2;

            b.DrawString(Game1.dialogueFont, "Run Log", new Vector2(x, y), Color.SaddleBrown);
            y += 44;

            int totalGold = stateManager.GetTotalGoldSpent();
            int zonesUnlocked = 0;
            int bundlesCompleted = 0;
            var log = stateManager.GetRunLog();
            foreach (var entry in log)
            {
                if (entry.EventType == "zone_unlock") zonesUnlocked++;
                if (entry.EventType == "bundle_complete") bundlesCompleted++;
            }

            b.DrawString(Game1.smallFont, $"Gold spent: {totalGold:N0}g   Zones: {zonesUnlocked}   Bundles: {bundlesCompleted}", new Vector2(x, y), Color.DarkSlateGray);
            y += 28;

            var deltas = stateManager.GetAllPlayerDeltas();
            foreach (var kv in deltas)
            {
                var s = kv.Value;
                b.DrawString(Game1.smallFont, kv.Key, new Vector2(x, y), Color.SaddleBrown);
                y += 22;

                int col2 = x + contentWidth / 2;
                b.DrawString(Game1.smallFont, $"  Seeds sown: {s.SeedsSown}", new Vector2(x, y), Color.DarkSlateGray);
                b.DrawString(Game1.smallFont, $"Fish caught: {s.FishCaught}", new Vector2(col2, y), Color.DarkSlateGray);
                y += 20;
                b.DrawString(Game1.smallFont, $"  Stones mined: {s.StonesSmashed}", new Vector2(x, y), Color.DarkSlateGray);
                b.DrawString(Game1.smallFont, $"Stumps chopped: {s.StumpsChopped}", new Vector2(col2, y), Color.DarkSlateGray);
                y += 20;
                b.DrawString(Game1.smallFont, $"  Monsters slain: {s.MonstersKilled}", new Vector2(x, y), Color.DarkSlateGray);
                b.DrawString(Game1.smallFont, $"Items shipped: {s.ItemsShipped}", new Vector2(col2, y), Color.DarkSlateGray);
                y += 20;
                b.DrawString(Game1.smallFont, $"  Cooked: {s.ItemsCooked}", new Vector2(x, y), Color.DarkSlateGray);
                b.DrawString(Game1.smallFont, $"Crafted: {s.ItemsCrafted}", new Vector2(col2, y), Color.DarkSlateGray);
                y += 24;
            }

            if (deltas.Count == 0)
            {
                b.DrawString(Game1.smallFont, "Stats will appear after the first day.", new Vector2(x, y), Color.Gray);
                y += 24;
            }

            b.Draw(Game1.fadeToBlackRect, new Rectangle(x, y, contentWidth, 2), Color.SaddleBrown * 0.5f);
            y += 10;
            int logListTop = y;

            int rowH = 26;
            int maxVisible = (rightPanelRect.Bottom - Padding - y) / rowH;
            if (maxVisible < 1) maxVisible = 1;
            logMaxVisible = maxVisible;
            if (logScrollOffset > Math.Max(0, log.Count - maxVisible))
                logScrollOffset = Math.Max(0, log.Count - maxVisible);

            if (log.Count == 0)
            {
                b.DrawString(Game1.smallFont, "No events yet.", new Vector2(x, y), Color.Gray);
                return;
            }

            for (int i = 0; i < maxVisible; i++)
            {
                int idx = logScrollOffset + i;
                if (idx >= log.Count) break;
                var entry = log[idx];

                Color markColor;
                switch (entry.EventType)
                {
                    case "zone_unlock": markColor = Color.ForestGreen; break;
                    case "ticket_purchase": markColor = Color.Goldenrod; break;
                    case "bundle_complete": markColor = Color.DarkOrange; break;
                    case "contribution": markColor = Color.SteelBlue; break;
                    default: markColor = Color.Gray; break;
                }

                string season = entry.Season != null
                    ? char.ToUpper(entry.Season[0]) + entry.Season.Substring(1)
                    : "?";
                string dateStr = $"Y{entry.Year} {season} {entry.Day}";

                string desc = entry.EventType switch
                {
                    "zone_unlock" => $"{entry.PlayerName} unlocked {entry.TargetName}",
                    "ticket_purchase" => $"{entry.PlayerName} bought {entry.TargetName} ticket",
                    "bundle_complete" => $"{entry.PlayerName} completed {entry.TargetName}",
                    "contribution" => $"{entry.PlayerName} contributed {entry.GoldAmount:N0}g to {entry.TargetName}",
                    _ => $"{entry.PlayerName}: {entry.TargetName}"
                };

                b.Draw(Game1.staminaRect, new Rectangle(x, y + 6, 10, 10), markColor);
                b.DrawString(Game1.smallFont, dateStr, new Vector2(x + 20, y), Color.Gray);
                float dateWidth = Game1.smallFont.MeasureString(dateStr).X;
                float descMaxW = contentWidth - 20 - dateWidth - 20;
                string fullDesc = $" — {desc}";
                string truncDesc = Game1.parseText(fullDesc, Game1.smallFont, (int)descMaxW);
                if (truncDesc.Contains('\n')) truncDesc = truncDesc.Substring(0, truncDesc.IndexOf('\n'));
                b.DrawString(Game1.smallFont, truncDesc, new Vector2(x + 20 + dateWidth, y), Color.DarkSlateGray);
                y += rowH;
            }

            if (logScrollOffset > 0)
                DrawCursorIcon(b, UpArrowIcon, rightPanelRect.Right - Padding - 28, logListTop, 2f, Color.White);
            if (logScrollOffset + maxVisible < log.Count)
                DrawCursorIcon(b, DownArrowIcon, x + contentWidth / 2 - 11, rightPanelRect.Bottom - Padding - 26, 2.5f, Color.White);
        }

        private void DrawZoneList(SpriteBatch b)
        {
            for (int i = 0; i < maxVisibleRows; i++)
            {
                int dataIndex = scrollOffset + i;
                if (dataIndex >= TotalEntries) break;

                var slot = zoneSlots[i];
                bool isSelected = dataIndex == selectedIndex;

                if (!isSelected && dataIndex == hoverIndex)
                    b.Draw(Game1.staminaRect, slot.bounds, Color.Wheat * 0.25f);

                if (IsNewBundleIndex(dataIndex))
                {
                    if (isSelected) b.Draw(Game1.staminaRect, slot.bounds, HighlightColor);
                    b.DrawString(Game1.smallFont, "+ New Bundle", new Vector2(slot.bounds.X + 40, slot.bounds.Y + (ZoneRowHeight - 28) / 2), Color.SaddleBrown);
                    continue;
                }

                int iconY = slot.bounds.Y + (slot.bounds.Height - 27) / 2;

                if (IsBundleIndex(dataIndex))
                {
                    var bundle = GetBundleAt(dataIndex);
                    if (isSelected) b.Draw(Game1.staminaRect, slot.bounds, HighlightColor);
                    DrawCursorIcon(b, bundle.IsCompleted ? CheckedBox : UncheckedBox, slot.bounds.X + 6, iconY, 3f, Color.White);
                    Color nameCol = isSelected ? Game1.textColor : (bundle.IsCompleted ? Color.DarkGreen : Color.DarkGoldenrod);
                    b.DrawString(Game1.smallFont, bundle.DisplayName, new Vector2(slot.bounds.X + 40, slot.bounds.Y + (ZoneRowHeight - 28) / 2), nameCol);
                    continue;
                }

                if (!IsZoneIndex(dataIndex)) break;
                var zone = orderedZones[dataIndex];
                bool isPermanent = stateManager.IsZonePermanentlyUnlocked(zone.ZoneId);
                bool hasTicket = stateManager.HasActiveTicket(zone.ZoneId, Game1.player.UniqueMultiplayerID);
                bool isAccessible = isPermanent || hasTicket;

                if (isSelected)
                    b.Draw(Game1.staminaRect, slot.bounds, HighlightColor);

                // Status icon: checked box = owned, coin = ticket today, empty box = locked
                if (isPermanent)
                    DrawCursorIcon(b, CheckedBox, slot.bounds.X + 6, iconY, 3f, Color.White);
                else if (hasTicket)
                    DrawCursorIcon(b, CoinIcon, slot.bounds.X + 8, iconY, 2.5f, Color.White);
                else
                    DrawCursorIcon(b, UncheckedBox, slot.bounds.X + 6, iconY, 3f, Color.White);

                Color nameColor = isSelected ? Game1.textColor : (isAccessible ? Color.DarkGreen : Color.DarkRed);
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
            string typeLabel = zone.UnlockType == "permanent" ? "Permanent Unlock" : "Daily Ticket (valid for one day)";
            b.DrawString(Game1.smallFont, $"Type: {typeLabel}", new Vector2(x, y), Game1.textColor);
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

            var effectiveItems = stateManager.GetEffectiveItems(zone);

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
                    b.Draw(Game1.staminaRect, new Rectangle(x, y, barWidth, barHeight), Color.Black * 0.25f);
                    b.Draw(Game1.staminaRect, new Rectangle(x, y, (int)(barWidth * progress), barHeight), Color.Goldenrod);
                    b.Draw(Game1.fadeToBlackRect, new Rectangle(x, y, barWidth, 2), Color.SaddleBrown * 0.6f);
                    b.Draw(Game1.fadeToBlackRect, new Rectangle(x, y + barHeight - 2, barWidth, 2), Color.SaddleBrown * 0.6f);
                    b.Draw(Game1.fadeToBlackRect, new Rectangle(x, y, 2, barHeight), Color.SaddleBrown * 0.6f);
                    b.Draw(Game1.fadeToBlackRect, new Rectangle(x + barWidth - 2, y, 2, barHeight), Color.SaddleBrown * 0.6f);
                    y += barHeight + 12;

                    if (totalContributed >= scaledCost && effectiveItems.Count > 0)
                    {
                        b.DrawString(Game1.smallFont, "Fully funded — deliver the items to unlock!", new Vector2(x, y), Color.DarkGoldenrod);
                        y += 28;
                    }
                }
            }

            // Item costs with icons (using effective items from overrides)
            if (effectiveItems.Count > 0)
            {
                b.DrawString(Game1.smallFont, "Items required:", new Vector2(x, y), Game1.textColor);
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

                    string line = $"{item.DisplayName}: {have}/{item.Count}";
                    b.DrawString(Game1.smallFont, line, new Vector2(textX, y), itemColor);
                    if (hasEnough) DrawTrailingCheck(b, line, textX, y);
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
                b.DrawString(Game1.smallFont, "Rewards:", new Vector2(x, y), Game1.textColor);
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
                var req = stateManager.GetZoneById(zone.RequiresZone);
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
                    b.DrawString(Game1.smallFont, "Mine Floor Gates:", new Vector2(x, y), Game1.textColor);
                    y += 26;
                    foreach (var gate in gates.OrderBy(g => g.FloorNumber))
                    {
                        bool met = collectiveMining >= gate.RequiredMiningLevel;
                        string line = $"Floor {gate.FloorNumber}: Mining Lv {gate.RequiredMiningLevel}";
                        b.DrawString(Game1.smallFont, line, new Vector2(x + 8, y), met ? Color.DarkGreen : Color.DarkRed);
                        if (met) DrawTrailingCheck(b, line, x + 8, y);
                        y += 24;
                    }
                    y += 4;
                }
            }

            // Status
            y += 8;
            string status; Color statusColor;
            if (stateManager.IsZonePermanentlyUnlocked(zone.ZoneId))
            { status = "UNLOCKED"; statusColor = Color.Green; }
            else if (stateManager.HasActiveTicket(zone.ZoneId, Game1.player.UniqueMultiplayerID))
            { status = "TICKET ACTIVE (expires tonight)"; statusColor = Color.Green; }
            else
            { status = "LOCKED"; statusColor = Color.DarkRed; }
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
                bool funded = stateManager.GetTotalContributions(zone.ZoneId) >= scaledCost;
                bool canContribute = (funded || Game1.player.Money > 0) && !waitingForResponse;
                Color cBtnColor = canContribute ? Color.White : Color.Gray * 0.5f;
                drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
                    contributeButton.X, contributeButton.Y, contributeButton.Width, contributeButton.Height,
                    cBtnColor, 4f, drawShadow: true);
                string cBtnText = funded && effectiveItems.Count > 0 ? "Deliver Items" : "Contribute Gold";
                Vector2 cBtnSize = Game1.smallFont.MeasureString(cBtnText);
                b.DrawString(Game1.smallFont, cBtnText,
                    new Vector2(contributeButton.X + (contributeButton.Width - cBtnSize.X) / 2,
                                contributeButton.Y + (contributeButton.Height - cBtnSize.Y) / 2),
                    canContribute ? Color.DarkSlateGray : Color.Gray);
            }

            // Plate location: shown to everyone so any player can find where to buy
            {
                var plate = stateManager.GetEffectivePlate(zone);
                string plateInfo = plate != null ? $"{plate.LocationName} ({plate.X}, {plate.Y})" : "not set";
                string infoText = $"Plate: {plateInfo}";
                Vector2 infoSize = Game1.smallFont.MeasureString(infoText);
                b.DrawString(Game1.smallFont, infoText,
                    new Vector2(purchaseButton.bounds.X + (purchaseButton.bounds.Width - infoSize.X) / 2, purchaseButton.bounds.Bottom + 12),
                    Color.Gray);
            }

            // Host-only links: "Move Plate" and "Edit Zone"
            if (onRequestPlatePlacement != null)
            {
                int infoY = purchaseButton.bounds.Bottom + 12;

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

            b.DrawString(Game1.smallFont, "Type: Custom Bundle", new Vector2(x, y), Game1.textColor);
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
                b.DrawString(Game1.smallFont, "Items required:", new Vector2(x, y), Game1.textColor);
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
                    string line = $"{item.DisplayName}: {have}/{item.Count}";
                    b.DrawString(Game1.smallFont, line, new Vector2(textX, y), hasEnough ? Color.DarkGreen : Color.DarkRed);
                    if (hasEnough) DrawTrailingCheck(b, line, textX, y);
                    y += 36;
                }
            }

            if (bundle.Rewards.Count > 0)
            {
                b.DrawString(Game1.smallFont, "Rewards:", new Vector2(x, y), Game1.textColor);
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
            Color statusColor = bundle.IsCompleted ? Color.Green : Color.DarkOrange;
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

        /// <summary>Draw a sprite from Game1.mouseCursors at the given top-left position.</summary>
        private static void DrawCursorIcon(SpriteBatch b, Rectangle src, float ix, float iy, float scale, Color color)
            => b.Draw(Game1.mouseCursors, new Vector2(ix, iy), src, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.9f);

        /// <summary>Draw a small green check sprite immediately after a satisfied requirement line.</summary>
        private static void DrawTrailingCheck(SpriteBatch b, string line, float lineX, float lineY)
            => DrawCursorIcon(b, CheckedBox, lineX + Game1.smallFont.MeasureString(line).X + 8, lineY + 2, 2f, Color.White);
    }
}
