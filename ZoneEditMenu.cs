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
    public class ZoneEditMenu : IClickableMenu
    {
        private const int MenuWidth = 1000;
        private const int MenuHeightNormal = 780;
        private const int MenuHeightMine = 920;
        private const int Padding = 16;
        private const int RowHeight = 36;
        private const int InvCols = 12;
        private const int InvRows = 2;
        private const int InvSlotSize = 64;

        private readonly ZoneDefinition zone;
        private readonly ZoneStateManager stateManager;

        private readonly Dictionary<string, Item> itemCache = new();

        // Editing state
        private int editGoldCost;
        private List<ItemCost> editItems;
        private List<ItemCost> editRewards;
        private bool addToRewards; // false = requirements mode, true = rewards mode

        // Mine level gates (only for Mine zone)
        private bool isMineZone;
        private List<MineLevelGate> editMineGates;
        private int mineGateScrollOffset;
        private const int MaxVisibleMineGateRows = 3;

        // Text input for item ID
        private TextBox itemIdTextBox;
        private int addCount = 1;

        // Layout rects
        private int innerX, innerY, innerWidth, innerHeight;
        private Rectangle inventoryBounds;
        private Rectangle saveBtnBounds;
        private Rectangle cancelBtnBounds;

        // Gold cost buttons: [-5000] [-1000] [+1000] [+5000]
        private Rectangle goldMinus5k, goldMinus1k, goldPlus1k, goldPlus5k;

        // Mode toggle buttons
        private Rectangle reqModeBtn, rewardModeBtn;

        // Add button (for text input)
        private Rectangle addItemBtn;
        private Rectangle addCountMinus, addCountPlus;

        // Mine gate buttons
        private Rectangle addGateBtn;
        private TextBox gateFloorTextBox;

        // Scroll for items/rewards if lists get long
        private int itemScrollOffset;
        private int rewardScrollOffset;
        private const int MaxVisibleItemRows = 4;
        private const int MaxVisibleRewardRows = 3;

        public ZoneEditMenu(ZoneDefinition zone, ZoneStateManager stateManager)
            : base(
                (Game1.uiViewport.Width - MenuWidth) / 2,
                (Game1.uiViewport.Height - (zone.ZoneId == "Mine" ? MenuHeightMine : MenuHeightNormal)) / 2,
                MenuWidth, zone.ZoneId == "Mine" ? MenuHeightMine : MenuHeightNormal, showUpperRightCloseButton: true)
        {
            this.zone = zone;
            this.stateManager = stateManager;

            // Initialize editing state from effective values (deep copy)
            editGoldCost = stateManager.GetEffectiveBaseCost(zone);
            editItems = stateManager.GetEffectiveItems(zone)
                .Select(i => new ItemCost { ItemId = i.ItemId, DisplayName = i.DisplayName, Count = i.Count })
                .ToList();
            editRewards = stateManager.GetRewards(zone)
                .Select(i => new ItemCost { ItemId = i.ItemId, DisplayName = i.DisplayName, Count = i.Count })
                .ToList();

            // Mine level gates (only for the Mine zone)
            isMineZone = zone.ZoneId == "Mine";
            if (isMineZone)
            {
                editMineGates = stateManager.GetEffectiveMineLevelGates()
                    .Select(g => new MineLevelGate { FloorNumber = g.FloorNumber, RequiredMiningLevel = g.RequiredMiningLevel })
                    .OrderBy(g => g.FloorNumber)
                    .ToList();
            }

            // Cache all known items
            foreach (var item in editItems) CacheItem(item.ItemId);
            foreach (var item in editRewards) CacheItem(item.ItemId);

            // Text input
            itemIdTextBox = new TextBox(
                Game1.content.Load<Texture2D>("LooseSprites\\textBox"),
                null, Game1.smallFont, Color.Black)
            {
                Width = 280,
                Text = ""
            };

            if (isMineZone)
            {
                gateFloorTextBox = new TextBox(
                    Game1.content.Load<Texture2D>("LooseSprites\\textBox"),
                    null, Game1.smallFont, Color.Black)
                {
                    Width = 120,
                    Text = ""
                };
            }

            SetupLayout();
        }

        private void CacheItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || itemCache.ContainsKey(itemId)) return;
            try
            {
                var item = ItemRegistry.Create(itemId);
                if (item != null) itemCache[itemId] = item;
            }
            catch { }
        }

        private void SetupLayout()
        {
            innerX = xPositionOnScreen + spaceToClearSideBorder + Padding;
            innerY = yPositionOnScreen + spaceToClearTopBorder + Padding + 48;
            innerWidth = width - (spaceToClearSideBorder + Padding) * 2;
            innerHeight = height - spaceToClearTopBorder - Padding * 2 - 48 - spaceToClearSideBorder;

            int y = innerY;

            // Gold cost buttons (positioned after title)
            y += 48; // title
            int goldBtnWidth = 80;
            int goldBtnHeight = 36;
            int goldBtnY = y + 28;
            int goldBtnStartX = innerX + 140;
            goldMinus5k = new Rectangle(goldBtnStartX, goldBtnY, goldBtnWidth, goldBtnHeight);
            goldMinus1k = new Rectangle(goldBtnStartX + goldBtnWidth + 8, goldBtnY, goldBtnWidth, goldBtnHeight);
            goldPlus1k = new Rectangle(goldBtnStartX + (goldBtnWidth + 8) * 2, goldBtnY, goldBtnWidth, goldBtnHeight);
            goldPlus5k = new Rectangle(goldBtnStartX + (goldBtnWidth + 8) * 3, goldBtnY, goldBtnWidth, goldBtnHeight);

            // Inventory grid position (near bottom, above save/cancel)
            int invWidth = InvCols * InvSlotSize;
            int invX = innerX + (innerWidth - invWidth) / 2;
            int invY = innerY + innerHeight - InvRows * InvSlotSize - 70;
            inventoryBounds = new Rectangle(invX, invY, invWidth, InvRows * InvSlotSize);

            // Mode toggle buttons (above inventory)
            int modeY = invY - 40;
            reqModeBtn = new Rectangle(innerX, modeY, 160, 32);
            rewardModeBtn = new Rectangle(innerX + 170, modeY, 160, 32);

            // Item ID text box and add button (between mode toggle and inventory)
            itemIdTextBox.X = innerX + 350;
            itemIdTextBox.Y = modeY;

            addCountMinus = new Rectangle(innerX + 650, modeY, 32, 32);
            addCountPlus = new Rectangle(innerX + 720, modeY, 32, 32);
            addItemBtn = new Rectangle(innerX + 770, modeY, 80, 32);

            // Mine gate add button layout (positioned dynamically in draw since it depends on scroll)
            if (isMineZone)
            {
                gateFloorTextBox.X = innerX + 350;
                gateFloorTextBox.Y = inventoryBounds.Y - 80;
                addGateBtn = new Rectangle(innerX + 500, inventoryBounds.Y - 80, 80, 32);
            }

            // Save/Cancel buttons
            int btnWidth = 160;
            int btnHeight = 48;
            int btnY = innerY + innerHeight - btnHeight - Padding;
            saveBtnBounds = new Rectangle(innerX + innerWidth / 2 - btnWidth - 20, btnY, btnWidth, btnHeight);
            cancelBtnBounds = new Rectangle(innerX + innerWidth / 2 + 20, btnY, btnWidth, btnHeight);
        }

        // ── Click handling ──────────────────────────────────────────

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            // Gold cost buttons
            if (goldMinus5k.Contains(x, y)) { editGoldCost = Math.Max(0, editGoldCost - 5000); Game1.playSound("smallSelect"); return; }
            if (goldMinus1k.Contains(x, y)) { editGoldCost = Math.Max(0, editGoldCost - 1000); Game1.playSound("smallSelect"); return; }
            if (goldPlus1k.Contains(x, y)) { editGoldCost += 1000; Game1.playSound("smallSelect"); return; }
            if (goldPlus5k.Contains(x, y)) { editGoldCost += 5000; Game1.playSound("smallSelect"); return; }

            // Mode toggle
            if (reqModeBtn.Contains(x, y)) { addToRewards = false; Game1.playSound("smallSelect"); return; }
            if (rewardModeBtn.Contains(x, y)) { addToRewards = true; Game1.playSound("smallSelect"); return; }

            // Add count +/-
            if (addCountMinus.Contains(x, y)) { addCount = Math.Max(1, addCount - 1); Game1.playSound("smallSelect"); return; }
            if (addCountPlus.Contains(x, y)) { addCount++; Game1.playSound("smallSelect"); return; }

            // Add item button (from text input)
            if (addItemBtn.Contains(x, y))
            {
                TryAddItemFromTextBox();
                return;
            }

            // Text box click (select/deselect)
            if (new Rectangle(itemIdTextBox.X, itemIdTextBox.Y, itemIdTextBox.Width, 48).Contains(x, y))
            {
                itemIdTextBox.Selected = true;
                if (gateFloorTextBox != null) gateFloorTextBox.Selected = false;
                Game1.keyboardDispatcher.Subscriber = itemIdTextBox;
            }
            else if (gateFloorTextBox == null || !new Rectangle(gateFloorTextBox.X, gateFloorTextBox.Y, gateFloorTextBox.Width, 48).Contains(x, y))
            {
                itemIdTextBox.Selected = false;
            }

            // Item list row buttons (remove / +1 / -1 / +10 / -10)
            if (HandleItemListClick(x, y, editItems, GetItemListY(), itemScrollOffset, MaxVisibleItemRows)) return;

            // Reward list row buttons
            if (HandleItemListClick(x, y, editRewards, GetRewardListY(), rewardScrollOffset, MaxVisibleRewardRows)) return;

            // Mine gate row buttons
            if (isMineZone && HandleMineGateClick(x, y)) return;

            // Mine gate: floor text box and add button
            if (isMineZone)
            {
                if (new Rectangle(gateFloorTextBox.X, gateFloorTextBox.Y, gateFloorTextBox.Width, 48).Contains(x, y))
                {
                    gateFloorTextBox.Selected = true;
                    itemIdTextBox.Selected = false;
                    Game1.keyboardDispatcher.Subscriber = gateFloorTextBox;
                }
                else if (!new Rectangle(itemIdTextBox.X, itemIdTextBox.Y, itemIdTextBox.Width, 48).Contains(x, y))
                {
                    gateFloorTextBox.Selected = false;
                }

                if (addGateBtn.Contains(x, y))
                {
                    TryAddMineGate();
                    return;
                }
            }

            // Inventory grid click
            var clickedItem = GetClickedInventoryItem(x, y);
            if (clickedItem != null)
            {
                AddItemToList(clickedItem.QualifiedItemId, clickedItem.DisplayName, addCount);
                Game1.playSound("smallSelect");
                return;
            }

            // Save button
            if (saveBtnBounds.Contains(x, y))
            {
                SaveChanges();
                Game1.playSound("purchaseClick");
                exitThisMenu();
                return;
            }

            // Cancel button
            if (cancelBtnBounds.Contains(x, y))
            {
                Game1.playSound("bigDeSelect");
                exitThisMenu();
                return;
            }
        }

        public override void receiveScrollWheelAction(int direction)
        {
            base.receiveScrollWheelAction(direction);
            // Scroll item or reward lists based on mouse position
            int mouseX = Game1.getMouseX();
            int mouseY = Game1.getMouseY();

            int itemListY = GetItemListY();
            int rewardListY = GetRewardListY();
            int listHeight = MaxVisibleItemRows * RowHeight;
            int rewardListHeight = MaxVisibleRewardRows * RowHeight;

            if (new Rectangle(innerX, itemListY, innerWidth, listHeight + 28).Contains(mouseX, mouseY))
            {
                if (direction > 0 && itemScrollOffset > 0) itemScrollOffset--;
                else if (direction < 0 && itemScrollOffset < editItems.Count - MaxVisibleItemRows) itemScrollOffset++;
            }
            else if (new Rectangle(innerX, rewardListY, innerWidth, rewardListHeight + 28).Contains(mouseX, mouseY))
            {
                if (direction > 0 && rewardScrollOffset > 0) rewardScrollOffset--;
                else if (direction < 0 && rewardScrollOffset < editRewards.Count - MaxVisibleRewardRows) rewardScrollOffset++;
            }

            if (isMineZone)
            {
                int gateListY = GetMineGateListY();
                int gateListHeight = MaxVisibleMineGateRows * RowHeight;
                if (new Rectangle(innerX, gateListY, innerWidth, gateListHeight + 28).Contains(mouseX, mouseY))
                {
                    if (direction > 0 && mineGateScrollOffset > 0) mineGateScrollOffset--;
                    else if (direction < 0 && mineGateScrollOffset < editMineGates.Count - MaxVisibleMineGateRows) mineGateScrollOffset++;
                }
            }
        }

        public override void receiveKeyPress(Keys key)
        {
            if (itemIdTextBox.Selected)
            {
                if (key == Keys.Escape) { itemIdTextBox.Selected = false; return; }
                if (key == Keys.Enter) { TryAddItemFromTextBox(); return; }
                return;
            }

            if (gateFloorTextBox != null && gateFloorTextBox.Selected)
            {
                if (key == Keys.Escape) { gateFloorTextBox.Selected = false; return; }
                if (key == Keys.Enter) { TryAddMineGate(); return; }
                return;
            }

            base.receiveKeyPress(key);
            if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key))
                exitThisMenu();
        }

        // ── Helpers ──────────────────────────────────────────────────

        private int GetItemListY() => innerY + 48 + 28 + 36 + 12; // after title + gold cost + gold buttons + margin
        private int GetRewardListY() => GetItemListY() + 28 + Math.Min(editItems.Count, MaxVisibleItemRows) * RowHeight + 20;
        private int GetMineGateListY() => GetRewardListY() + 28 + Math.Min(editRewards.Count, MaxVisibleRewardRows) * RowHeight + 20;

        private void TryAddItemFromTextBox()
        {
            string itemId = itemIdTextBox.Text.Trim();
            if (string.IsNullOrEmpty(itemId)) return;

            // Try to create the item to validate the ID and get display name
            try
            {
                var testItem = ItemRegistry.Create(itemId);
                if (testItem != null)
                {
                    AddItemToList(testItem.QualifiedItemId, testItem.DisplayName, addCount);
                    CacheItem(testItem.QualifiedItemId);
                    itemIdTextBox.Text = "";
                    Game1.playSound("smallSelect");
                }
                else
                {
                    Game1.playSound("cancel");
                }
            }
            catch
            {
                Game1.playSound("cancel");
            }
        }

        private void AddItemToList(string itemId, string displayName, int count)
        {
            var targetList = addToRewards ? editRewards : editItems;
            var existing = targetList.FirstOrDefault(i => i.ItemId == itemId);
            if (existing != null)
                existing.Count += count;
            else
                targetList.Add(new ItemCost { ItemId = itemId, DisplayName = displayName, Count = count });
            CacheItem(itemId);
        }

        private bool HandleItemListClick(int x, int y, List<ItemCost> list, int listY, int scrollOffset, int maxVisible)
        {
            int startY = listY + 28; // after header
            for (int i = 0; i < maxVisible && i + scrollOffset < list.Count; i++)
            {
                int rowY = startY + i * RowHeight;
                int btnBaseX = innerX + innerWidth - 220;

                // -10 button
                Rectangle minus10 = new(btnBaseX, rowY, 36, 28);
                if (minus10.Contains(x, y))
                {
                    list[i + scrollOffset].Count = Math.Max(1, list[i + scrollOffset].Count - 10);
                    Game1.playSound("smallSelect");
                    return true;
                }

                // -1 button
                Rectangle minus1 = new(btnBaseX + 40, rowY, 28, 28);
                if (minus1.Contains(x, y))
                {
                    list[i + scrollOffset].Count = Math.Max(1, list[i + scrollOffset].Count - 1);
                    Game1.playSound("smallSelect");
                    return true;
                }

                // +1 button
                Rectangle plus1 = new(btnBaseX + 72, rowY, 28, 28);
                if (plus1.Contains(x, y))
                {
                    list[i + scrollOffset].Count++;
                    Game1.playSound("smallSelect");
                    return true;
                }

                // +10 button
                Rectangle plus10 = new(btnBaseX + 104, rowY, 36, 28);
                if (plus10.Contains(x, y))
                {
                    list[i + scrollOffset].Count += 10;
                    Game1.playSound("smallSelect");
                    return true;
                }

                // Remove button
                Rectangle removeBtn = new(btnBaseX + 152, rowY, 56, 28);
                if (removeBtn.Contains(x, y))
                {
                    list.RemoveAt(i + scrollOffset);
                    Game1.playSound("trashcan");
                    return true;
                }
            }
            return false;
        }

        private Item GetClickedInventoryItem(int x, int y)
        {
            if (!inventoryBounds.Contains(x, y)) return null;
            int col = (x - inventoryBounds.X) / InvSlotSize;
            int row = (y - inventoryBounds.Y) / InvSlotSize;
            int index = row * InvCols + col;
            if (index >= 0 && index < Game1.player.Items.Count)
                return Game1.player.Items[index];
            return null;
        }

        private bool HandleMineGateClick(int x, int y)
        {
            int startY = GetMineGateListY() + 28;
            for (int i = 0; i < MaxVisibleMineGateRows && i + mineGateScrollOffset < editMineGates.Count; i++)
            {
                int rowY = startY + i * RowHeight;
                int idx = i + mineGateScrollOffset;
                int btnBaseX = innerX + innerWidth - 220;

                // -5 level
                if (new Rectangle(btnBaseX, rowY, 36, 28).Contains(x, y))
                { editMineGates[idx].RequiredMiningLevel = Math.Max(0, editMineGates[idx].RequiredMiningLevel - 5); Game1.playSound("smallSelect"); return true; }
                // -1 level
                if (new Rectangle(btnBaseX + 40, rowY, 28, 28).Contains(x, y))
                { editMineGates[idx].RequiredMiningLevel = Math.Max(0, editMineGates[idx].RequiredMiningLevel - 1); Game1.playSound("smallSelect"); return true; }
                // +1 level
                if (new Rectangle(btnBaseX + 72, rowY, 28, 28).Contains(x, y))
                { editMineGates[idx].RequiredMiningLevel++; Game1.playSound("smallSelect"); return true; }
                // +5 level
                if (new Rectangle(btnBaseX + 104, rowY, 36, 28).Contains(x, y))
                { editMineGates[idx].RequiredMiningLevel += 5; Game1.playSound("smallSelect"); return true; }
                // Remove gate
                if (new Rectangle(btnBaseX + 152, rowY, 56, 28).Contains(x, y))
                { editMineGates.RemoveAt(idx); Game1.playSound("trashcan"); return true; }
            }
            return false;
        }

        private void TryAddMineGate()
        {
            string text = gateFloorTextBox.Text.Trim();
            if (!int.TryParse(text, out int floor) || floor <= 0)
            {
                Game1.playSound("cancel");
                return;
            }
            if (editMineGates.Any(g => g.FloorNumber == floor))
            {
                Game1.playSound("cancel");
                return;
            }
            editMineGates.Add(new MineLevelGate { FloorNumber = floor, RequiredMiningLevel = 1 });
            editMineGates = editMineGates.OrderBy(g => g.FloorNumber).ToList();
            gateFloorTextBox.Text = "";
            Game1.playSound("smallSelect");
        }

        private void SaveChanges()
        {
            var zoneOverride = new ZoneConfigOverride
            {
                MoneyCost = editGoldCost,
                Items = editItems.Select(i => new ItemCost { ItemId = i.ItemId, DisplayName = i.DisplayName, Count = i.Count }).ToList(),
                Rewards = editRewards.Select(i => new ItemCost { ItemId = i.ItemId, DisplayName = i.DisplayName, Count = i.Count }).ToList()
            };
            stateManager.SetZoneOverride(zone.ZoneId, zoneOverride);

            if (isMineZone)
                stateManager.SetMineLevelGateOverrides(editMineGates);
        }

        // ── Drawing ──────────────────────────────────────────────────

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.75f);
            Game1.drawDialogueBox(xPositionOnScreen, yPositionOnScreen, width, height, false, true);

            // Title
            string title = $"Edit: {zone.DisplayName}";
            SpriteText.drawStringWithScrollCenteredAt(b, title, xPositionOnScreen + width / 2, yPositionOnScreen + spaceToClearTopBorder - 4);

            int y = innerY;

            // ── Gold Cost ──
            b.DrawString(Game1.dialogueFont, $"Gold Cost: {editGoldCost:N0}g", new Vector2(innerX, y), Color.SaddleBrown);
            y += 28;

            DrawSmallButton(b, goldMinus5k, "-5000");
            DrawSmallButton(b, goldMinus1k, "-1000");
            DrawSmallButton(b, goldPlus1k, "+1000");
            DrawSmallButton(b, goldPlus5k, "+5000");
            y += 44;

            // ── Item Requirements ──
            y += 12;
            int itemListY = GetItemListY();
            b.DrawString(Game1.smallFont, "Item Requirements:", new Vector2(innerX, itemListY), Color.Black);
            DrawItemList(b, editItems, itemListY + 28, itemScrollOffset, MaxVisibleItemRows);

            if (editItems.Count > MaxVisibleItemRows)
            {
                string scrollHint = $"({itemScrollOffset + 1}-{Math.Min(itemScrollOffset + MaxVisibleItemRows, editItems.Count)} of {editItems.Count})";
                b.DrawString(Game1.smallFont, scrollHint, new Vector2(innerX + 200, itemListY), Color.Gray);
            }

            // ── Rewards ──
            int rewardListY = GetRewardListY();
            b.DrawString(Game1.smallFont, "Rewards:", new Vector2(innerX, rewardListY), Color.Black);
            DrawItemList(b, editRewards, rewardListY + 28, rewardScrollOffset, MaxVisibleRewardRows);

            if (editRewards.Count > MaxVisibleRewardRows)
            {
                string scrollHint = $"({rewardScrollOffset + 1}-{Math.Min(rewardScrollOffset + MaxVisibleRewardRows, editRewards.Count)} of {editRewards.Count})";
                b.DrawString(Game1.smallFont, scrollHint, new Vector2(innerX + 100, rewardListY), Color.Gray);
            }

            // ── Mine Level Gates (Mine zone only) ──
            if (isMineZone)
            {
                int gateListY = GetMineGateListY();
                int collectiveMining = stateManager.GetCollectiveSkillLevel("Mining");
                b.DrawString(Game1.smallFont, $"Mine Level Gates (collective Mining: {collectiveMining}):", new Vector2(innerX, gateListY), Color.DarkSlateBlue);

                if (editMineGates.Count > MaxVisibleMineGateRows)
                {
                    string scrollHint = $"({mineGateScrollOffset + 1}-{Math.Min(mineGateScrollOffset + MaxVisibleMineGateRows, editMineGates.Count)} of {editMineGates.Count})";
                    b.DrawString(Game1.smallFont, scrollHint, new Vector2(innerX + 420, gateListY), Color.Gray);
                }

                DrawMineGateList(b, gateListY + 28);

                // "Add gate" input row
                b.DrawString(Game1.smallFont, "Add floor:", new Vector2(innerX, gateFloorTextBox.Y + 4), Color.DarkSlateGray);
                gateFloorTextBox.Draw(b);
                DrawSmallButton(b, addGateBtn, "Add");
            }

            // ── Mode toggle + Item ID input ──
            Color reqColor = !addToRewards ? Color.White : Color.Gray * 0.6f;
            Color rewColor = addToRewards ? Color.White : Color.Gray * 0.6f;
            DrawSmallButton(b, reqModeBtn, "Requirements", reqColor);
            DrawSmallButton(b, rewardModeBtn, "Rewards", rewColor);

            // Item ID text box
            itemIdTextBox.Draw(b);

            // Add count display and buttons
            string countText = $"{addCount}";
            b.DrawString(Game1.smallFont, countText, new Vector2(addCountMinus.Right + 4, addCountMinus.Y + 2), Color.Black);
            DrawSmallButton(b, addCountMinus, "-");
            DrawSmallButton(b, addCountPlus, "+");
            DrawSmallButton(b, addItemBtn, "Add");

            // ── Inventory grid ──
            string invLabel = addToRewards ? "Click inventory to add reward:" : "Click inventory to add requirement:";
            b.DrawString(Game1.smallFont, invLabel, new Vector2(inventoryBounds.X, inventoryBounds.Y - 24), Color.DarkSlateGray);
            DrawInventoryGrid(b);

            // ── Save / Cancel buttons ──
            DrawButton(b, saveBtnBounds, "Save", Color.LimeGreen);
            DrawButton(b, cancelBtnBounds, "Cancel", Color.IndianRed);

            drawMouse(b);
        }

        private void DrawItemList(SpriteBatch b, List<ItemCost> list, int startY, int scrollOffset, int maxVisible)
        {
            if (list.Count == 0)
            {
                b.DrawString(Game1.smallFont, "(none)", new Vector2(innerX + 8, startY), Color.Gray);
                return;
            }

            for (int i = 0; i < maxVisible && i + scrollOffset < list.Count; i++)
            {
                var item = list[i + scrollOffset];
                int rowY = startY + i * RowHeight;
                int textX = innerX + 8;

                // Item icon
                if (itemCache.TryGetValue(item.ItemId, out var cachedItem))
                {
                    cachedItem.drawInMenu(b, new Vector2(innerX - 16, rowY - 20), 0.45f, 1f, 0.9f, StackDrawType.Hide);
                    textX = innerX + 28;
                }

                // Item name and count
                b.DrawString(Game1.smallFont, $"{item.DisplayName}: {item.Count}", new Vector2(textX, rowY), Color.Black);

                // Row buttons
                int btnBaseX = innerX + innerWidth - 220;
                DrawSmallButton(b, new Rectangle(btnBaseX, rowY, 36, 28), "-10");
                DrawSmallButton(b, new Rectangle(btnBaseX + 40, rowY, 28, 28), "-1");
                DrawSmallButton(b, new Rectangle(btnBaseX + 72, rowY, 28, 28), "+1");
                DrawSmallButton(b, new Rectangle(btnBaseX + 104, rowY, 36, 28), "+10");
                DrawSmallButton(b, new Rectangle(btnBaseX + 152, rowY, 56, 28), "X", Color.IndianRed);
            }
        }

        private void DrawMineGateList(SpriteBatch b, int startY)
        {
            if (editMineGates.Count == 0)
            {
                b.DrawString(Game1.smallFont, "(no gates — all floors accessible)", new Vector2(innerX + 8, startY), Color.Gray);
                return;
            }

            int collectiveMining = stateManager.GetCollectiveSkillLevel("Mining");
            for (int i = 0; i < MaxVisibleMineGateRows && i + mineGateScrollOffset < editMineGates.Count; i++)
            {
                var gate = editMineGates[i + mineGateScrollOffset];
                int rowY = startY + i * RowHeight;

                bool met = collectiveMining >= gate.RequiredMiningLevel;
                Color textColor = met ? Color.DarkGreen : Color.DarkRed;
                string check = met ? "\u2713 " : "";
                b.DrawString(Game1.smallFont, $"{check}Floor {gate.FloorNumber}: Mining Lv {gate.RequiredMiningLevel}",
                    new Vector2(innerX + 8, rowY), textColor);

                int btnBaseX = innerX + innerWidth - 220;
                DrawSmallButton(b, new Rectangle(btnBaseX, rowY, 36, 28), "-5");
                DrawSmallButton(b, new Rectangle(btnBaseX + 40, rowY, 28, 28), "-1");
                DrawSmallButton(b, new Rectangle(btnBaseX + 72, rowY, 28, 28), "+1");
                DrawSmallButton(b, new Rectangle(btnBaseX + 104, rowY, 36, 28), "+5");
                DrawSmallButton(b, new Rectangle(btnBaseX + 152, rowY, 56, 28), "X", Color.IndianRed);
            }
        }

        private void DrawInventoryGrid(SpriteBatch b)
        {
            for (int i = 0; i < InvCols * InvRows; i++)
            {
                int col = i % InvCols;
                int row = i / InvCols;
                int sx = inventoryBounds.X + col * InvSlotSize;
                int sy = inventoryBounds.Y + row * InvSlotSize;

                // Slot background
                b.Draw(Game1.menuTexture, new Rectangle(sx, sy, InvSlotSize, InvSlotSize),
                    new Rectangle(128, 128, 64, 64), Color.White);

                // Item sprite
                if (i < Game1.player.Items.Count)
                {
                    var item = Game1.player.Items[i];
                    if (item != null)
                        item.drawInMenu(b, new Vector2(sx, sy), 1f);
                }
            }

            // Highlight on hover
            int mouseX = Game1.getMouseX();
            int mouseY = Game1.getMouseY();
            if (inventoryBounds.Contains(mouseX, mouseY))
            {
                int hCol = (mouseX - inventoryBounds.X) / InvSlotSize;
                int hRow = (mouseY - inventoryBounds.Y) / InvSlotSize;
                int hx = inventoryBounds.X + hCol * InvSlotSize;
                int hy = inventoryBounds.Y + hRow * InvSlotSize;
                b.Draw(Game1.fadeToBlackRect, new Rectangle(hx, hy, InvSlotSize, InvSlotSize), Color.White * 0.3f);
            }
        }

        private void DrawSmallButton(SpriteBatch b, Rectangle bounds, string text, Color? tint = null)
        {
            Color bgColor = tint ?? Color.White;
            drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
                bounds.X, bounds.Y, bounds.Width, bounds.Height, bgColor, 3f, drawShadow: false);

            Vector2 textSize = Game1.smallFont.MeasureString(text);
            float scale = Math.Min(1f, (bounds.Width - 8) / textSize.X);
            Vector2 textPos = new(
                bounds.X + (bounds.Width - textSize.X * scale) / 2,
                bounds.Y + (bounds.Height - textSize.Y * scale) / 2);
            b.DrawString(Game1.smallFont, text, textPos, Color.DarkSlateGray, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
        }

        private void DrawButton(SpriteBatch b, Rectangle bounds, string text, Color tint)
        {
            drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
                bounds.X, bounds.Y, bounds.Width, bounds.Height, tint, 4f, drawShadow: true);

            Vector2 textSize = Game1.smallFont.MeasureString(text);
            b.DrawString(Game1.smallFont, text,
                new Vector2(bounds.X + (bounds.Width - textSize.X) / 2, bounds.Y + (bounds.Height - textSize.Y) / 2),
                Color.DarkSlateGray);
        }
    }
}
