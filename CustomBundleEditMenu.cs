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
    public class CustomBundleEditMenu : IClickableMenu
    {
        private const int MenuWidth = 1000;
        private const int MenuHeight = 780;
        private const int Padding = 16;
        private const int RowHeight = 36;
        private const int InvCols = 12;
        private const int InvRows = 2;
        private const int InvSlotSize = 64;

        private readonly ZoneStateManager stateManager;
        private readonly string editingBundleId;
        private readonly bool isNew;

        private readonly Dictionary<string, Item> itemCache = new();

        private string editName;
        private string editDescription;
        private int editGoldCost;
        private List<ItemCost> editItems;
        private List<ItemCost> editRewards;
        private bool addToRewards;

        private TextBox nameTextBox;
        private TextBox descTextBox;
        private TextBox itemIdTextBox;
        private int addCount = 1;

        private int innerX, innerY, innerWidth, innerHeight;
        private Rectangle inventoryBounds;
        private Rectangle saveBtnBounds, cancelBtnBounds, deleteBtnBounds;
        private Rectangle goldMinus5k, goldMinus1k, goldPlus1k, goldPlus5k;
        private Rectangle reqModeBtn, rewardModeBtn;
        private Rectangle addItemBtn, addCountMinus, addCountPlus;

        private int itemScrollOffset;
        private int rewardScrollOffset;
        private const int MaxVisibleItemRows = 3;
        private const int MaxVisibleRewardRows = 3;

        /// <param name="bundleId">null = create new, non-null = edit existing.</param>
        public CustomBundleEditMenu(ZoneStateManager stateManager, string bundleId)
            : base(
                (Game1.uiViewport.Width - MenuWidth) / 2,
                (Game1.uiViewport.Height - MenuHeight) / 2,
                MenuWidth, MenuHeight, showUpperRightCloseButton: true)
        {
            this.stateManager = stateManager;
            this.editingBundleId = bundleId;
            this.isNew = string.IsNullOrEmpty(bundleId);

            if (isNew)
            {
                editName = "";
                editDescription = "";
                editGoldCost = 0;
                editItems = new();
                editRewards = new();
            }
            else
            {
                var bundle = stateManager.GetCustomBundles().FirstOrDefault(b => b.BundleId == bundleId);
                if (bundle != null)
                {
                    editName = bundle.DisplayName ?? "";
                    editDescription = bundle.Description ?? "";
                    editGoldCost = bundle.MoneyCost;
                    editItems = bundle.Items.Select(i => new ItemCost { ItemId = i.ItemId, DisplayName = i.DisplayName, Count = i.Count }).ToList();
                    editRewards = bundle.Rewards.Select(i => new ItemCost { ItemId = i.ItemId, DisplayName = i.DisplayName, Count = i.Count }).ToList();
                }
                else
                {
                    editName = "";
                    editDescription = "";
                    editGoldCost = 0;
                    editItems = new();
                    editRewards = new();
                }
            }

            foreach (var item in editItems) CacheItem(item.ItemId);
            foreach (var item in editRewards) CacheItem(item.ItemId);

            var textBoxTex = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
            nameTextBox = new TextBox(textBoxTex, null, Game1.smallFont, Color.Black) { Width = 400, Text = editName };
            descTextBox = new TextBox(textBoxTex, null, Game1.smallFont, Color.Black) { Width = 400, Text = editDescription };
            itemIdTextBox = new TextBox(textBoxTex, null, Game1.smallFont, Color.Black) { Width = 280, Text = "" };

            SetupLayout();
        }

        private void CacheItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || itemCache.ContainsKey(itemId)) return;
            try { var item = ItemRegistry.Create(itemId); if (item != null) itemCache[itemId] = item; } catch { }
        }

        private void SetupLayout()
        {
            innerX = xPositionOnScreen + spaceToClearSideBorder + Padding;
            innerY = yPositionOnScreen + spaceToClearTopBorder + Padding + 48;
            innerWidth = width - (spaceToClearSideBorder + Padding) * 2;
            innerHeight = height - spaceToClearTopBorder - Padding * 2 - 48 - spaceToClearSideBorder;

            nameTextBox.X = innerX + 90;
            nameTextBox.Y = innerY;
            descTextBox.X = innerX + 90;
            descTextBox.Y = innerY + 48;

            int goldY = innerY + 100;
            int goldBtnWidth = 80;
            int goldBtnHeight = 36;
            int goldBtnStartX = innerX + 200;
            goldMinus5k = new Rectangle(goldBtnStartX, goldY, goldBtnWidth, goldBtnHeight);
            goldMinus1k = new Rectangle(goldBtnStartX + goldBtnWidth + 8, goldY, goldBtnWidth, goldBtnHeight);
            goldPlus1k = new Rectangle(goldBtnStartX + (goldBtnWidth + 8) * 2, goldY, goldBtnWidth, goldBtnHeight);
            goldPlus5k = new Rectangle(goldBtnStartX + (goldBtnWidth + 8) * 3, goldY, goldBtnWidth, goldBtnHeight);

            int invWidth = InvCols * InvSlotSize;
            int invX = innerX + (innerWidth - invWidth) / 2;
            int invY = innerY + innerHeight - InvRows * InvSlotSize - 70;
            inventoryBounds = new Rectangle(invX, invY, invWidth, InvRows * InvSlotSize);

            int modeY = invY - 40;
            reqModeBtn = new Rectangle(innerX, modeY, 160, 32);
            rewardModeBtn = new Rectangle(innerX + 170, modeY, 160, 32);

            itemIdTextBox.X = innerX + 350;
            itemIdTextBox.Y = modeY;
            addCountMinus = new Rectangle(innerX + 650, modeY, 32, 32);
            addCountPlus = new Rectangle(innerX + 720, modeY, 32, 32);
            addItemBtn = new Rectangle(innerX + 770, modeY, 80, 32);

            int btnWidth = 140;
            int btnHeight = 48;
            int btnY = innerY + innerHeight - btnHeight - Padding;
            int totalBtnsWidth = isNew ? (btnWidth * 2 + 40) : (btnWidth * 3 + 80);
            int btnStartX = innerX + (innerWidth - totalBtnsWidth) / 2;
            saveBtnBounds = new Rectangle(btnStartX, btnY, btnWidth, btnHeight);
            cancelBtnBounds = new Rectangle(btnStartX + btnWidth + 40, btnY, btnWidth, btnHeight);
            if (!isNew)
                deleteBtnBounds = new Rectangle(btnStartX + (btnWidth + 40) * 2, btnY, btnWidth, btnHeight);
        }

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

            // Add item button
            if (addItemBtn.Contains(x, y)) { TryAddItemFromTextBox(); return; }

            // Text box focus
            DeselectAllTextBoxes();
            if (HitsTextBox(nameTextBox, x, y)) { nameTextBox.Selected = true; Game1.keyboardDispatcher.Subscriber = nameTextBox; }
            else if (HitsTextBox(descTextBox, x, y)) { descTextBox.Selected = true; Game1.keyboardDispatcher.Subscriber = descTextBox; }
            else if (HitsTextBox(itemIdTextBox, x, y)) { itemIdTextBox.Selected = true; Game1.keyboardDispatcher.Subscriber = itemIdTextBox; }

            // Item list clicks
            if (HandleItemListClick(x, y, editItems, GetItemListY(), itemScrollOffset, MaxVisibleItemRows)) return;
            if (HandleItemListClick(x, y, editRewards, GetRewardListY(), rewardScrollOffset, MaxVisibleRewardRows)) return;

            // Inventory click
            var clickedItem = GetClickedInventoryItem(x, y);
            if (clickedItem != null)
            {
                AddItemToList(clickedItem.QualifiedItemId, clickedItem.DisplayName, addCount);
                Game1.playSound("smallSelect");
                return;
            }

            // Save
            if (saveBtnBounds.Contains(x, y))
            {
                if (string.IsNullOrWhiteSpace(nameTextBox.Text))
                {
                    Game1.playSound("cancel");
                    return;
                }
                SaveBundle();
                Game1.playSound("purchaseClick");
                exitThisMenu();
                return;
            }

            // Cancel
            if (cancelBtnBounds.Contains(x, y)) { Game1.playSound("bigDeSelect"); exitThisMenu(); return; }

            // Delete
            if (!isNew && deleteBtnBounds.Contains(x, y))
            {
                stateManager.RemoveCustomBundle(editingBundleId);
                Game1.playSound("trashcan");
                exitThisMenu();
                return;
            }
        }

        public override void receiveScrollWheelAction(int direction)
        {
            base.receiveScrollWheelAction(direction);
            int mouseX = Game1.getMouseX();
            int mouseY = Game1.getMouseY();

            int itemListY = GetItemListY();
            int rewardListY = GetRewardListY();

            if (new Rectangle(innerX, itemListY, innerWidth, MaxVisibleItemRows * RowHeight + 28).Contains(mouseX, mouseY))
            {
                if (direction > 0 && itemScrollOffset > 0) itemScrollOffset--;
                else if (direction < 0 && itemScrollOffset < editItems.Count - MaxVisibleItemRows) itemScrollOffset++;
            }
            else if (new Rectangle(innerX, rewardListY, innerWidth, MaxVisibleRewardRows * RowHeight + 28).Contains(mouseX, mouseY))
            {
                if (direction > 0 && rewardScrollOffset > 0) rewardScrollOffset--;
                else if (direction < 0 && rewardScrollOffset < editRewards.Count - MaxVisibleRewardRows) rewardScrollOffset++;
            }
        }

        public override void receiveKeyPress(Keys key)
        {
            if (nameTextBox.Selected || descTextBox.Selected)
            {
                if (key == Keys.Escape) { DeselectAllTextBoxes(); return; }
                if (key == Keys.Tab)
                {
                    if (nameTextBox.Selected) { nameTextBox.Selected = false; descTextBox.Selected = true; Game1.keyboardDispatcher.Subscriber = descTextBox; }
                    else { descTextBox.Selected = false; nameTextBox.Selected = true; Game1.keyboardDispatcher.Subscriber = nameTextBox; }
                    return;
                }
                return;
            }
            if (itemIdTextBox.Selected)
            {
                if (key == Keys.Escape) { itemIdTextBox.Selected = false; return; }
                if (key == Keys.Enter) { TryAddItemFromTextBox(); return; }
                return;
            }

            base.receiveKeyPress(key);
            if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key))
                exitThisMenu();
        }

        private bool HitsTextBox(TextBox tb, int x, int y) =>
            new Rectangle(tb.X, tb.Y, tb.Width, 48).Contains(x, y);

        private void DeselectAllTextBoxes()
        {
            nameTextBox.Selected = false;
            descTextBox.Selected = false;
            itemIdTextBox.Selected = false;
        }

        private int GetItemListY() => innerY + 148;
        private int GetRewardListY() => GetItemListY() + 28 + Math.Min(editItems.Count, MaxVisibleItemRows) * RowHeight + 20;

        private void TryAddItemFromTextBox()
        {
            string itemId = itemIdTextBox.Text.Trim();
            if (string.IsNullOrEmpty(itemId)) return;
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
                else Game1.playSound("cancel");
            }
            catch { Game1.playSound("cancel"); }
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
            int startY = listY + 28;
            for (int i = 0; i < maxVisible && i + scrollOffset < list.Count; i++)
            {
                int rowY = startY + i * RowHeight;
                int btnBaseX = innerX + innerWidth - 220;

                if (new Rectangle(btnBaseX, rowY, 36, 28).Contains(x, y))
                { list[i + scrollOffset].Count = Math.Max(1, list[i + scrollOffset].Count - 10); Game1.playSound("smallSelect"); return true; }
                if (new Rectangle(btnBaseX + 40, rowY, 28, 28).Contains(x, y))
                { list[i + scrollOffset].Count = Math.Max(1, list[i + scrollOffset].Count - 1); Game1.playSound("smallSelect"); return true; }
                if (new Rectangle(btnBaseX + 72, rowY, 28, 28).Contains(x, y))
                { list[i + scrollOffset].Count++; Game1.playSound("smallSelect"); return true; }
                if (new Rectangle(btnBaseX + 104, rowY, 36, 28).Contains(x, y))
                { list[i + scrollOffset].Count += 10; Game1.playSound("smallSelect"); return true; }
                if (new Rectangle(btnBaseX + 152, rowY, 56, 28).Contains(x, y))
                { list.RemoveAt(i + scrollOffset); Game1.playSound("trashcan"); return true; }
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

        private void SaveBundle()
        {
            var bundle = new CustomBundle
            {
                BundleId = isNew ? $"custom_{Guid.NewGuid():N}" : editingBundleId,
                DisplayName = nameTextBox.Text.Trim(),
                Description = descTextBox.Text.Trim(),
                MoneyCost = editGoldCost,
                Items = editItems.Select(i => new ItemCost { ItemId = i.ItemId, DisplayName = i.DisplayName, Count = i.Count }).ToList(),
                Rewards = editRewards.Select(i => new ItemCost { ItemId = i.ItemId, DisplayName = i.DisplayName, Count = i.Count }).ToList(),
                IsCompleted = false
            };

            if (isNew)
                stateManager.AddCustomBundle(bundle);
            else
            {
                var existing = stateManager.GetCustomBundles().FirstOrDefault(b => b.BundleId == editingBundleId);
                if (existing != null) bundle.IsCompleted = existing.IsCompleted;
                stateManager.UpdateCustomBundle(bundle);
            }
        }

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.75f);
            Game1.drawDialogueBox(xPositionOnScreen, yPositionOnScreen, width, height, false, true);

            string title = isNew ? "Create Custom Bundle" : $"Edit: {editName}";
            SpriteText.drawStringWithScrollCenteredAt(b, title, xPositionOnScreen + width / 2, yPositionOnScreen + spaceToClearTopBorder - 4);

            // Name
            b.DrawString(Game1.smallFont, "Name:", new Vector2(innerX, nameTextBox.Y + 8), Color.Black);
            nameTextBox.Draw(b);

            // Description
            b.DrawString(Game1.smallFont, "Desc:", new Vector2(innerX, descTextBox.Y + 8), Color.Black);
            descTextBox.Draw(b);

            // Gold Cost
            int goldY = innerY + 100;
            b.DrawString(Game1.smallFont, $"Gold Cost: {editGoldCost:N0}g", new Vector2(innerX, goldY + 6), Color.SaddleBrown);
            DrawSmallButton(b, goldMinus5k, "-5000");
            DrawSmallButton(b, goldMinus1k, "-1000");
            DrawSmallButton(b, goldPlus1k, "+1000");
            DrawSmallButton(b, goldPlus5k, "+5000");

            // Item Requirements
            int itemListY = GetItemListY();
            b.DrawString(Game1.smallFont, "Item Requirements:", new Vector2(innerX, itemListY), Color.Black);
            if (editItems.Count > MaxVisibleItemRows)
                b.DrawString(Game1.smallFont, $"({itemScrollOffset + 1}-{Math.Min(itemScrollOffset + MaxVisibleItemRows, editItems.Count)} of {editItems.Count})",
                    new Vector2(innerX + 200, itemListY), Color.Gray);
            DrawItemList(b, editItems, itemListY + 28, itemScrollOffset, MaxVisibleItemRows);

            // Rewards
            int rewardListY = GetRewardListY();
            b.DrawString(Game1.smallFont, "Rewards:", new Vector2(innerX, rewardListY), Color.Black);
            if (editRewards.Count > MaxVisibleRewardRows)
                b.DrawString(Game1.smallFont, $"({rewardScrollOffset + 1}-{Math.Min(rewardScrollOffset + MaxVisibleRewardRows, editRewards.Count)} of {editRewards.Count})",
                    new Vector2(innerX + 100, rewardListY), Color.Gray);
            DrawItemList(b, editRewards, rewardListY + 28, rewardScrollOffset, MaxVisibleRewardRows);

            // Mode toggle + item ID input
            DrawSmallButton(b, reqModeBtn, "Requirements", !addToRewards ? Color.White : Color.Gray * 0.6f);
            DrawSmallButton(b, rewardModeBtn, "Rewards", addToRewards ? Color.White : Color.Gray * 0.6f);
            itemIdTextBox.Draw(b);
            b.DrawString(Game1.smallFont, $"{addCount}", new Vector2(addCountMinus.Right + 4, addCountMinus.Y + 2), Color.Black);
            DrawSmallButton(b, addCountMinus, "-");
            DrawSmallButton(b, addCountPlus, "+");
            DrawSmallButton(b, addItemBtn, "Add");

            // Inventory
            string invLabel = addToRewards ? "Click inventory to add reward:" : "Click inventory to add requirement:";
            b.DrawString(Game1.smallFont, invLabel, new Vector2(inventoryBounds.X, inventoryBounds.Y - 24), Color.DarkSlateGray);
            DrawInventoryGrid(b);

            // Buttons
            DrawButton(b, saveBtnBounds, "Save", Color.LimeGreen);
            DrawButton(b, cancelBtnBounds, "Cancel", Color.IndianRed);
            if (!isNew)
                DrawButton(b, deleteBtnBounds, "Delete", Color.OrangeRed);

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
                if (itemCache.TryGetValue(item.ItemId, out var cached))
                {
                    cached.drawInMenu(b, new Vector2(innerX - 16, rowY - 20), 0.45f, 1f, 0.9f, StackDrawType.Hide);
                    textX = innerX + 28;
                }
                b.DrawString(Game1.smallFont, $"{item.DisplayName}: {item.Count}", new Vector2(textX, rowY), Color.Black);

                int btnBaseX = innerX + innerWidth - 220;
                DrawSmallButton(b, new Rectangle(btnBaseX, rowY, 36, 28), "-10");
                DrawSmallButton(b, new Rectangle(btnBaseX + 40, rowY, 28, 28), "-1");
                DrawSmallButton(b, new Rectangle(btnBaseX + 72, rowY, 28, 28), "+1");
                DrawSmallButton(b, new Rectangle(btnBaseX + 104, rowY, 36, 28), "+10");
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
                b.Draw(Game1.menuTexture, new Rectangle(sx, sy, InvSlotSize, InvSlotSize), new Rectangle(128, 128, 64, 64), Color.White);
                if (i < Game1.player.Items.Count)
                {
                    var item = Game1.player.Items[i];
                    if (item != null) item.drawInMenu(b, new Vector2(sx, sy), 1f);
                }
            }

            int mouseX = Game1.getMouseX();
            int mouseY = Game1.getMouseY();
            if (inventoryBounds.Contains(mouseX, mouseY))
            {
                int hCol = (mouseX - inventoryBounds.X) / InvSlotSize;
                int hRow = (mouseY - inventoryBounds.Y) / InvSlotSize;
                b.Draw(Game1.fadeToBlackRect, new Rectangle(inventoryBounds.X + hCol * InvSlotSize, inventoryBounds.Y + hRow * InvSlotSize, InvSlotSize, InvSlotSize), Color.White * 0.3f);
            }
        }

        private void DrawSmallButton(SpriteBatch b, Rectangle bounds, string text, Color? tint = null)
        {
            drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
                bounds.X, bounds.Y, bounds.Width, bounds.Height, tint ?? Color.White, 3f, drawShadow: false);
            Vector2 textSize = Game1.smallFont.MeasureString(text);
            float scale = Math.Min(1f, (bounds.Width - 8) / textSize.X);
            b.DrawString(Game1.smallFont, text,
                new Vector2(bounds.X + (bounds.Width - textSize.X * scale) / 2, bounds.Y + (bounds.Height - textSize.Y * scale) / 2),
                Color.DarkSlateGray, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
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
