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
        private const int MaxSearchResults = 6;
        private const int SearchRowHeight = 36;

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
        private TextBox searchTextBox;
        private string lastSearchText = "";
        private int addCount = 1;

        private static List<(string QualifiedId, string DisplayName)> itemIndex;
        private List<(string QualifiedId, string DisplayName)> searchResults = new();

        private int innerX, innerY, innerWidth, innerHeight;
        private int searchResultsY;
        private Rectangle saveBtnBounds, cancelBtnBounds, deleteBtnBounds;
        private Rectangle goldMinus5k, goldMinus1k, goldPlus1k, goldPlus5k;
        private Rectangle reqModeBtn, rewardModeBtn;
        private Rectangle addCountMinus, addCountPlus;

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

            BuildItemIndex();

            var textBoxTex = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
            nameTextBox = new TextBox(textBoxTex, null, Game1.smallFont, Color.Black) { Width = 400, Text = editName };
            descTextBox = new TextBox(textBoxTex, null, Game1.smallFont, Color.Black) { Width = 400, Text = editDescription };
            searchTextBox = new TextBox(textBoxTex, null, Game1.smallFont, Color.Black) { Width = 360, Text = "" };

            SetupLayout();
        }

        private void CacheItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || itemCache.ContainsKey(itemId)) return;
            try { var item = ItemRegistry.Create(itemId); if (item != null) itemCache[itemId] = item; } catch { }
        }

        private static void BuildItemIndex()
        {
            if (itemIndex != null) return;
            itemIndex = new();
            foreach (var typeDef in ItemRegistry.ItemTypes)
            {
                foreach (var localId in typeDef.GetAllIds())
                {
                    string qid = typeDef.Identifier + localId;
                    try
                    {
                        var parsed = ItemRegistry.GetMetadata(qid)?.GetParsedData();
                        if (parsed != null && !string.IsNullOrEmpty(parsed.DisplayName))
                            itemIndex.Add((qid, parsed.DisplayName));
                    }
                    catch { }
                }
            }
            itemIndex.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateSearchResults()
        {
            string query = searchTextBox.Text.Trim();
            if (query.Length < 2) { searchResults.Clear(); return; }
            searchResults = itemIndex
                .Where(item => item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.DisplayName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .ThenBy(item => item.DisplayName.Length)
                .Take(MaxSearchResults)
                .ToList();
            foreach (var r in searchResults) CacheItem(r.QualifiedId);
        }

        public override void update(GameTime time)
        {
            base.update(time);
            if (searchTextBox.Text != lastSearchText)
            {
                lastSearchText = searchTextBox.Text;
                UpdateSearchResults();
            }
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

            int searchSectionY = innerY + innerHeight - MaxSearchResults * SearchRowHeight - 130;
            reqModeBtn = new Rectangle(innerX, searchSectionY, 160, 32);
            rewardModeBtn = new Rectangle(innerX + 170, searchSectionY, 160, 32);
            addCountMinus = new Rectangle(innerX + innerWidth - 120, searchSectionY, 32, 32);
            addCountPlus = new Rectangle(innerX + innerWidth - 50, searchSectionY, 32, 32);

            int searchBoxY = searchSectionY + 42;
            searchTextBox.X = innerX;
            searchTextBox.Y = searchBoxY;
            searchResultsY = searchBoxY + 44;

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

            // Search result clicks
            for (int i = 0; i < searchResults.Count && i < MaxSearchResults; i++)
            {
                Rectangle row = new(innerX, searchResultsY + i * SearchRowHeight, innerWidth, SearchRowHeight);
                if (row.Contains(x, y))
                {
                    var result = searchResults[i];
                    AddItemToList(result.QualifiedId, result.DisplayName, addCount);
                    Game1.playSound("smallSelect");
                    return;
                }
            }

            // Text box focus
            DeselectAllTextBoxes();
            if (HitsTextBox(nameTextBox, x, y)) { nameTextBox.Selected = true; Game1.keyboardDispatcher.Subscriber = nameTextBox; }
            else if (HitsTextBox(descTextBox, x, y)) { descTextBox.Selected = true; Game1.keyboardDispatcher.Subscriber = descTextBox; }
            else if (HitsTextBox(searchTextBox, x, y)) { searchTextBox.Selected = true; Game1.keyboardDispatcher.Subscriber = searchTextBox; }

            // Item list clicks
            if (HandleItemListClick(x, y, editItems, GetItemListY(), itemScrollOffset, MaxVisibleItemRows)) return;
            if (HandleItemListClick(x, y, editRewards, GetRewardListY(), rewardScrollOffset, MaxVisibleRewardRows)) return;

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
            if (searchTextBox.Selected)
            {
                if (key == Keys.Escape) { searchTextBox.Selected = false; return; }
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
            searchTextBox.Selected = false;
        }

        private int GetItemListY() => innerY + 148;
        private int GetRewardListY() => GetItemListY() + 28 + Math.Min(editItems.Count, MaxVisibleItemRows) * RowHeight + 20;

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

            // Divider above search
            int divY = reqModeBtn.Y - 12;
            b.Draw(Game1.fadeToBlackRect, new Rectangle(innerX, divY, innerWidth, 2), Color.SaddleBrown * 0.4f);

            // Add to mode toggle + count
            DrawSmallButton(b, reqModeBtn, "Requirements", !addToRewards ? Color.White : Color.Gray * 0.6f);
            DrawSmallButton(b, rewardModeBtn, "Rewards", addToRewards ? Color.White : Color.Gray * 0.6f);
            b.DrawString(Game1.smallFont, "Count:", new Vector2(addCountMinus.X - 60, addCountMinus.Y + 4), Color.DarkSlateGray);
            DrawSmallButton(b, addCountMinus, "-");
            b.DrawString(Game1.smallFont, $"{addCount}", new Vector2(addCountMinus.Right + 6, addCountMinus.Y + 4), Color.Black);
            DrawSmallButton(b, addCountPlus, "+");

            // Search box
            b.DrawString(Game1.smallFont, "Search items:", new Vector2(innerX, searchTextBox.Y + 8), Color.DarkSlateGray);
            searchTextBox.X = innerX + 130;
            searchTextBox.Draw(b);

            // Search results
            if (searchResults.Count > 0)
            {
                for (int i = 0; i < searchResults.Count && i < MaxSearchResults; i++)
                {
                    var result = searchResults[i];
                    int rowY = searchResultsY + i * SearchRowHeight;
                    int textX = innerX + 8;

                    // Hover highlight
                    Rectangle rowRect = new(innerX, rowY, innerWidth, SearchRowHeight);
                    if (rowRect.Contains(Game1.getMouseX(), Game1.getMouseY()))
                        b.Draw(Game1.fadeToBlackRect, rowRect, Color.Wheat * 0.3f);

                    if (itemCache.TryGetValue(result.QualifiedId, out var cached))
                    {
                        cached.drawInMenu(b, new Vector2(innerX - 16, rowY - 18), 0.45f, 1f, 0.9f, StackDrawType.Hide);
                        textX = innerX + 30;
                    }
                    b.DrawString(Game1.smallFont, result.DisplayName, new Vector2(textX, rowY + 4), Color.Black);

                    // Small "click to add" hint on right
                    string hint = $"+ Add {addCount}";
                    Vector2 hintSize = Game1.smallFont.MeasureString(hint);
                    b.DrawString(Game1.smallFont, hint, new Vector2(innerX + innerWidth - hintSize.X - 4, rowY + 4), Color.SaddleBrown * 0.7f);
                }
            }
            else if (searchTextBox.Text.Trim().Length >= 2)
            {
                b.DrawString(Game1.smallFont, "No items found.", new Vector2(innerX + 8, searchResultsY + 4), Color.Gray);
            }
            else if (searchTextBox.Text.Trim().Length > 0)
            {
                b.DrawString(Game1.smallFont, "Type 2+ characters to search...", new Vector2(innerX + 8, searchResultsY + 4), Color.Gray);
            }

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
