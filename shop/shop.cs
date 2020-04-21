// Requires: GUICreator

//#define DEBUG
using ConVar;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using static Oxide.Plugins.GUICreator;

namespace Oxide.Plugins
{
    [Info("shop", "OHM", "1.1.0 BETA")]
    [Description("The ultimate GUI Shop")]
    internal class shop : RustPlugin
    {
        #region references

        [PluginReference]
        private Plugin ServerRewards, Economics;

        private GUICreator guiCreator;

        #endregion references

        #region global

        DynamicConfigFile CustomerDataFile;
        CustomerData customerData;

        private static shop PluginInstance = null;

        public shop()
        {
            PluginInstance = this;
        }

        #endregion global

        #region classes

        public interface ITrackable
        {
            string name { get; set; }
            int maxAmount { get; set; }
            int cooldownSeconds { get; set; }
        }

        public class Tracker
        {
            public string name;
            public DateTime lastPurchase;
            public int totalAmount;

            public Tracker() { }
        }

        [JsonObject(MemberSerialization.OptIn)]
        public class Category:ITrackable
        {
            [JsonProperty(PropertyName = "Name")]
            public string name { get; set; }

            [JsonProperty(PropertyName = "Fontsize")]
            public int fontsize { get; set; }

            public string safeName => Regex.Replace(name, @" ", "");

            [JsonProperty(PropertyName = "Category icon url/shortname (recommended size: 60px x 60px, item shortname for item icon)")]
            public string categoryicon { get; set; }

            [JsonProperty(PropertyName = "Permission to craft (if empty, no permission required)")]
            public string permission { get; set; }

            [JsonProperty(PropertyName = "Missing Permission Message")]
            public string forbiddenMsg { get; set; }

            [JsonProperty(PropertyName = "Maximum amount items that can be purchased from this category")]
            public int maxAmount { get; set; }

            [JsonProperty(PropertyName = "Cooldown in seconds after buying an item from this category")]
            public int cooldownSeconds { get; set; }

            [JsonProperty(PropertyName = "Products")]
            public List<Product> products { get; set; } = new List<Product>();
        }

        [JsonObject(MemberSerialization.OptIn)]
        public class Product:ITrackable
        {
            [JsonProperty(PropertyName = "Name")]
            public string name { get; set; }

            [JsonProperty(PropertyName = "Fontsize")]
            public int fontsize { get; set; }

            public string safeName => Regex.Replace(name, @" ", "");

            [JsonProperty(PropertyName = "Description (max. 200 characters)")]
            public string description { get; set; } = null;

            [JsonProperty(PropertyName = "Image URL (recommended size: 400px x 400px)")]
            public string imageUrl { get; set; }

            [JsonProperty(PropertyName = "Permission to craft (if empty, no permission required)")]
            public string permission { get; set; }

            [JsonProperty(PropertyName = "Missing Permission Message")]
            public string forbiddenMsg { get; set; }

            [JsonProperty(PropertyName = "Maximum amount that can be purchased")]
            public int maxAmount { get; set; }

            [JsonProperty(PropertyName = "Cooldown in seconds after purchase")]
            public int cooldownSeconds { get; set; }

            [JsonProperty(PropertyName = "Command to be executed on purchase")]
            public string command { get; set; }

            [JsonProperty(PropertyName = "Item shortname")]
            public string shortname { get; set; }

            [JsonProperty(PropertyName = "Item amount")]
            public int amount { get; set; }

            [JsonProperty(PropertyName = "Ingredients (max. 4)")]
            public Dictionary<string, int> ingredients { get; set; } = new Dictionary<string, int>();
        }

        enum Flags {selected = 1, noPermission = 2, maxAmountReached = 4, cooldown = 8 };

        public class ProductUI
        {
            BasePlayer player;
            Category category;
            Product product;
            Rectangle rectangle;
            Flags flags = 0;
            string parent;
            public int remainingAmount = int.MaxValue;
            public int cooldown = 0;

            public ProductUI(BasePlayer player, Category category, Product product, Rectangle rectangle, string parent)
            {
                this.player = player;
                this.category = category;
                this.product = product;
                this.rectangle = rectangle;
                this.parent = parent;

                refresh();
            }

            public void generateBase()
            {
                //set flags
                if (!PluginInstance.hasPermission(player, category, product)) flags |= Flags.noPermission;
                else
                {
                    int remainingCat = int.MaxValue;
                    int remainingProd = int.MaxValue;
                    PluginInstance.maxAmountReached(player.userID, category, out remainingCat);
                    PluginInstance.maxAmountReached(player.userID, product, out remainingProd);

                    if (remainingCat == 0 || remainingProd == 0)
                    {
                        flags |= Flags.maxAmountReached;
                    }
                    else
                    {
                        if (!PluginInstance.cooldownElapsed(player.userID, category) || !PluginInstance.cooldownElapsed(player.userID, product))
                        {
                            flags |= Flags.cooldown;
                        }
                        else
                        {
                            flags &= ~Flags.cooldown;
                        }
                    }
                    remainingAmount = Math.Min(remainingCat, remainingProd);

                }

                GuiContainer container = new GuiContainer(PluginInstance, $"productui_{product.safeName}", parent);
                //background
                container.addPanel($"productui_{product.safeName}_bg", rectangle, GuiContainer.Layer.menu, flags.HasFlag(Flags.selected) ? white30 : null, FadeIn, FadeOut);
                float bgW = (rectangle.anchorMaxX - rectangle.anchorMinX) * 1920;
                float bgH = (rectangle.anchorMaxY - rectangle.anchorMinY) * 1080;

                //image
                container.addPanel($"productui_{product.safeName}_imgbg", new Rectangle(0, 0, bgW - 2, bgW, bgW, bgH, true), $"productui_{product.safeName}_bg", white30, FadeIn, FadeOut);
                Rectangle imgRect = new Rectangle(productMargin, productMargin, bgW - (2 * productMargin), bgW - (2 * productMargin), bgW, bgH, true);
                if (string.IsNullOrEmpty(product.imageUrl) && !string.IsNullOrEmpty(product.shortname))
                {
                    container.addRawImage($"productui_{product.safeName}_img", imgRect, PluginInstance.guiCreator.getItemIcon(product.shortname), $"productui_{product.safeName}_bg", null, FadeIn, FadeOut);
                }
                else
                {
                    container.addImage($"productui_{product.safeName}_img", imgRect, product.safeName, $"productui_{product.safeName}_bg", null, FadeIn, FadeOut);
                }

                //remainingAmount
                if (remainingAmount != int.MaxValue)
                {
                    Rectangle remRect = new Rectangle(0, bgH - bgW, bgW, 20, bgW, bgH);
                    container.addText($"productui_{product.safeName}_left", remRect, new GuiText($"{remainingAmount} left", 10, white90), FadeIn, FadeOut, $"productui_{product.safeName}_bg");
                }

                //label
                Rectangle labelRect = new Rectangle(0, 0, bgW-2, bgH - bgW - 5, bgW, bgH);
                container.addPanel($"productui_{product.safeName}_label", labelRect, $"productui_{product.safeName}_bg", white30, FadeIn, FadeOut, new GuiText(product.name, product.fontsize, black90));

                Action<BasePlayer, string[]> callback = (bPlayer, input) =>
                {
                    if (flags.HasFlag(Flags.selected)) return;
                    else
                    {
                        foreach(ProductUI uiProduct in PluginInstance.displayedProducts[player.userID])
                        {
                            if (uiProduct.flags.HasFlag(Flags.selected)) uiProduct.deselect();
                        }
                        flags |= Flags.selected;
                    }
                    this.refresh();
                    GuiTracker.getGuiTracker(bPlayer).destroyGui(PluginInstance, "info");
                    PluginInstance.sendInfo(bPlayer, product);
                    PluginInstance.sendCheckout(bPlayer, category, product);
                };

                //overlays
                if (flags.HasFlag(Flags.noPermission) || flags.HasFlag(Flags.maxAmountReached) || flags.HasFlag(Flags.cooldown))
                {
                    container.addPlainPanel($"productui_{product.safeName}_forbidden_bgpanel", new Rectangle(), $"productui_{product.safeName}_img", black40, FadeIn, FadeOut, true);
                    if(flags.HasFlag(Flags.noPermission) || flags.HasFlag(Flags.maxAmountReached))
                    {
                        container.addImage($"productui_{product.safeName}_forbidden", new Rectangle(), "no_permission", $"productui_{product.safeName}_img", white90, FadeIn, FadeOut);
                    }
                    else
                    {
                        container.addImage($"productui_{product.safeName}_cooldown", new Rectangle(), "cooldown", $"productui_{product.safeName}_img", white90, FadeIn, FadeOut);

                        container.timers.Add(PluginInstance.timer.Repeat(config.cooldownInterval, (int)(cooldown/config.cooldownInterval), () =>
                        {
                            int cooldownCat = 0;
                            int cooldownProd = 0;
                            PluginInstance.cooldownElapsed(player.userID, category, out cooldownCat);
                            PluginInstance.cooldownElapsed(player.userID, product, out cooldownProd);
                            cooldown = Math.Max(Math.Max(cooldownCat, cooldownProd), 0);
#if DEBUG
                            player.ChatMessage($"cooldown:{cooldown}");
#endif

                            GuiContainer cooldownUI = new GuiContainer(PluginInstance, $"productui_{product.safeName}_cooldownui", $"productui_{product.safeName}");
                            cooldownUI.addText($"productui_{product.safeName}_cooldowntext", new Rectangle(), new GuiText(PluginInstance.formatTimeSpan(new TimeSpan(0, 0, cooldown)), 14, white90), FadeIn, FadeOut, $"productui_{product.safeName}_cooldown");
                            cooldownUI.display(player);
                            if (cooldown == 0)
                            {
                                refresh();
                                if (flags.HasFlag(Flags.selected)) PluginInstance.sendCheckout(player, category, product);
                            }
                        }));
                    }
                }


                container.addPlainButton($"productui_{product.safeName}_button", new Rectangle(), callback: callback, parent: $"productui_{product.safeName}_bg");
                container.display(player);
            }

            public void refresh()
            {
#if DEBUG
                player.ChatMessage($"generating productUI for {product.name}");
                player.ChatMessage($"cooldown:{cooldown}, remainingAmount:{remainingAmount}");
#endif
                generateBase();
            }

            public void deselect()
            {
                this.flags &= ~Flags.selected;
                refresh();
            }
        }

        #endregion classes

        #region oxide hooks

        private void Init()
        {
            //permissions
            permission.RegisterPermission("shop.use", this);
            permission.RegisterPermission("shop.free", this);

            //Data
            CustomerDataFile = Interface.Oxide.DataFileSystem.GetFile("shop/customerData");
            loadData();

            //Localization
            lang.RegisterMessages(messages, this);
        }

        private void OnServerInitialized()
        {
            //commands
            cmd.AddChatCommand(config.command, this, nameof(shopCommand));
            cmd.AddChatCommand("wipeshop", this, nameof(wipeShopCommand));
            cmd.AddConsoleCommand("shop.close", this, nameof(closeShop));

            //references
            guiCreator = (GUICreator)Manager.GetPlugin("GUICreator");
            if (config.useServerRewards && ServerRewards == null) Puts("ServerRewards not loaded! get it at https://umod.org/plugins/server-rewards");
            if ((config.useServerRewards || config.useEconomics) && Economics == null) Puts("Economics not loaded! get it at https://umod.org/plugins/economics");
            if (guiCreator == null)
            {
                Puts("GUICreator missing! This shouldn't happen");
                return;
            }
            guiCreator.registerImage(this, "white_cross", "https://i.imgur.com/fbwkYDj.png");
            guiCreator.registerImage(this, "dollar_icon", config.currencyIcon);
            guiCreator.registerImage(this, "no_permission", config.noPermissionOverlay);
            guiCreator.registerImage(this, "cooldown", config.cooldownOverlay);
            guiCreator.registerImage(this, "info_background", config.defaultInfoBG);
            guiCreator.registerImage(this, "products_background", config.background);

            //process category/product data
            foreach (Category category in config.Categories)
            {
                if (!string.IsNullOrEmpty(category.permission)) permission.RegisterPermission(category.permission, this);
                if (!string.IsNullOrEmpty(category.categoryicon))
                {
                    ItemDefinition itemDefinition = ItemManager.FindItemDefinition(category.categoryicon);
                    if (itemDefinition == null) guiCreator.registerImage(this, category.safeName, category.categoryicon);
                }
                foreach (Product product in category.products)
                {
                    if (!string.IsNullOrEmpty(product.permission)) permission.RegisterPermission(product.permission, this);
                    if (!string.IsNullOrEmpty(product.imageUrl)) guiCreator.registerImage(this, product.safeName, product.imageUrl);
                    if (string.IsNullOrEmpty(product.description) && !string.IsNullOrEmpty(product.shortname))
                    {
                        ItemDefinition itemDefinition = ItemManager.FindItemDefinition(product.shortname);
                        if (itemDefinition != null) product.description = itemDefinition.displayDescription.english;
                    }
                }
            }
        }

        #endregion oxide hooks

        #region UI
        Dictionary<ulong, List<ProductUI>> displayedProducts = new Dictionary<ulong, List<ProductUI>>();

        #region global parameters

        private const int maxProducts = 18;
        private const int maxIngredients = 4;

        private const float FadeIn = 0.1f;
        private const float FadeOut = 0.1f;

        private const int iconMargin = 5;
        private const int productMargin = 10;

        private static GuiColor black40 = new GuiColor(0, 0, 0, 0.4f);
        private static GuiColor black60 = new GuiColor(0, 0, 0, 0.6f);
        private static GuiColor white10 = new GuiColor(1, 1, 1, 0.1f);
        private static GuiColor white30 = new GuiColor(1, 1, 1, 0.3f);
        private static GuiColor white80 = new GuiColor(1, 1, 1, 0.8f);

        private static GuiColor white90 = new GuiColor(1, 1, 1, 0.9f);
        private static GuiColor black90 = new GuiColor(0, 0, 0, 0.9f);

        private static GuiColor green70 = new GuiColor(0, 1, 0, 0.7f);
        private static GuiColor red70 = new GuiColor(1, 0, 0, 0.7f);
        private static GuiColor red90 = new GuiColor(1, 0, 0, 0.9f);
        private static GuiColor halfRed90 = new GuiColor(0.5f, 0, 0, 0.9f);

        #endregion global parameters

        //divide and conquer...
        private void sendShopUi(BasePlayer player)
        {
            List<Category> categories = getCategories(player);
            sendPanel(player);
            if (config.useEconomics || config.useServerRewards) sendBalance(player);
            sendCategories(player, categories, categories[0]);
            sendProducts(player, categories[0]);
        }

        private void closeShopUi(BasePlayer player)
        {
            if (player == null) return;
            displayedProducts.Remove(player.userID);
            GuiTracker.getGuiTracker(player).destroyGui(this, "shop");
        }

        private void sendPanel(BasePlayer player)
        {
            GuiContainer container = new GuiContainer(this, "shop");
            container.addPlainPanel("panel_bgpanel", new Rectangle(60, 40, 1800, 1000, 1920, 1080, true), GuiContainer.Layer.menu, black60, FadeIn, FadeOut, true);

            container.addPlainPanel("categories_productpanel", new Rectangle(65, 95, 1391, 940, 1920, 1080, true), GuiContainer.Layer.menu, black60, FadeIn, FadeOut);
            container.addImage("productpanel_background", new Rectangle(75, 105, 1371, 920, 1920, 1080, true), "products_background", GuiContainer.Layer.menu, white30, FadeIn, FadeOut);

            container.addPanel("balance_panel", new Rectangle(1461, 45, 345, 48, 1920, 1080, true), GuiContainer.Layer.menu, black60, FadeIn, FadeOut);

            //info panel
            container.addPlainPanel("info_bgpanel", new Rectangle(1461, 95, 394, 940, 1920, 1080, true), GuiContainer.Layer.menu, black60, FadeIn, FadeOut);
            container.addPanel("info_imgbackground", new Rectangle(1466, 100, 384, 384, 1920, 1080, true), GuiContainer.Layer.menu, null, FadeIn, FadeOut, null, "info_background");
            container.addPlainPanel("info_labelbackground", new Rectangle(1466, 489, 384, 45, 1920, 1080, true), GuiContainer.Layer.menu, white30, FadeIn, FadeOut);
            container.addPlainPanel("info_descbackground", new Rectangle(1466, 539, 384, 241, 1920, 1080, true), GuiContainer.Layer.menu, white30, FadeIn, FadeOut);

            //ingredients
            for (int i = 0; i < maxIngredients; i++)
            {
                container.addPlainPanel($"ingredients_icon{i}background", new Rectangle(1466, (785 + (i * 50)), 45, 45, 1920, 1080, true), GuiContainer.Layer.menu, white30, FadeIn, FadeOut);
                container.addPlainPanel($"ingredients_amount{i}background", new Rectangle(1516, (785 + (i * 50)), 95, 45, 1920, 1080, true), GuiContainer.Layer.menu, white30, FadeIn, FadeOut);
                container.addPlainPanel($"ingredients_shortname{i}background", new Rectangle(1616, (785 + (i * 50)), 234, 45, 1920, 1080, true), GuiContainer.Layer.menu, white30, FadeIn, FadeOut);
            }

            container.addPlainPanel("close_bg", new Rectangle(1810, 45, 45, 45, 1920, 1080, true), GuiContainer.Layer.menu, red90, FadeIn, FadeOut);
            container.addButton("close", new Rectangle(1810, 45, 45, 45, 1920, 1080, true), GuiContainer.Layer.menu, null, FadeIn, FadeOut, CursorEnabled: true, imgName: "white_cross");
            container.display(player);
        }

        private void sendBalance(BasePlayer player)
        {
            GuiContainer container = new GuiContainer(this, "balance", "shop");
            string balanceString = $"Balance: {getPoints(player)}";
            container.addPanel("balance_content", new Rectangle(1461, 45, 345, 48, 1920, 1080, true), GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            container.addImage("balance_img", new Rectangle(1461 + iconMargin, 45 + iconMargin, 45 - (2*iconMargin), 45 - (2*iconMargin), 1920, 1080, true), "dollar_icon", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            container.addText("balance_text", new Rectangle(1511, 45, 295, 45, 1920, 1080, true), GuiContainer.Layer.menu, new GuiText(balanceString, 22, white90, TextAnchor.MiddleLeft), FadeIn, FadeOut);
            container.display(player);
        }

        private void sendCategories(BasePlayer player, List<Category> categories, Category category)
        {
            GuiContainer container = new GuiContainer(this, "categories", "shop");

            int start = 65;
            int gap = 5;
            float sizeEach = (1391 - ((categories.Count - 1) * gap)) / categories.Count;

            for (int i = 0; i < categories.Count; i++)
            {
                float anchor = start + (i * (sizeEach + gap));
                container.addPlainPanel($"categories_{categories[i].safeName}", new Rectangle((i == 0) ? start : anchor, 45, sizeEach, 48, 1920, 1080, true), GuiContainer.Layer.menu, (category == categories[i]) ? black60 : white30, FadeIn, FadeOut);
                container.addText($"categories_{categories[i].safeName}_text", new Rectangle(((i == 0) ? start : anchor) + 50, 45, sizeEach - 50, 45, 1920, 1080, true), GuiContainer.Layer.menu, new GuiText(categories[i].name.ToUpper(), categories[i].fontsize, (category == categories[i]) ? white90 : black90, TextAnchor.MiddleLeft), FadeIn, FadeOut);
                if(!string.IsNullOrEmpty(categories[i].categoryicon))
                {
                    ItemDefinition itemDefinition = ItemManager.FindItemDefinition(categories[i].categoryicon);
                    if(itemDefinition != null)
                    {
                        container.addRawImage($"categories_{categories[i].safeName}_icon", new Rectangle(((i == 0) ? start : anchor) + iconMargin, 45 + iconMargin, 45 - (2*iconMargin), 45 - (2*iconMargin), 1920, 1080, true), guiCreator.getItemIcon(categories[i].categoryicon), GuiContainer.Layer.menu, null, FadeIn, FadeOut);
                    }
                    else
                    {
                        container.addImage($"categories_{categories[i].safeName}_icon", new Rectangle(((i == 0) ? start : anchor) + iconMargin, 45 + iconMargin, 45 - (2 * iconMargin), 45 - (2 * iconMargin), 1920, 1080, true), categories[i].safeName, GuiContainer.Layer.menu, null, FadeIn, FadeOut);
                    }
                }
                Category selected = categories[i];
                Action<BasePlayer, string[]> callback = (bPlayer, input) =>
                {
                    if (category == selected) return;
                    GuiTracker.getGuiTracker(bPlayer).destroyGui(this, "categories");
                    sendCategories(bPlayer, categories, selected);
                    sendProducts(bPlayer, selected);
                };
                container.addButton($"categories_{categories[i].safeName}_button", new Rectangle((i == 0) ? start : anchor, 45, sizeEach, 48, 1920, 1080, true), GuiContainer.Layer.menu, null, FadeIn, FadeOut, null, callback);
            }

            container.display(player);
        }

        private void sendProducts(BasePlayer player, Category category)
        {
            List<Product> products = getProducts(player, category);
            List<ProductUI> uiProducts = new List<ProductUI>();

            int startX = 75;
            int startY = 105;
            int gap = 10;
            int rows = 3;
            int columns = 6;
            float sizeEachX = (1391 - ((columns + 1) * gap)) / columns;
            float sizeEachY = (940 - ((rows + 1) * gap)) / rows;

            int count = 0;
            for (int i = 0; i < rows; i++)
            {
                if (count == maxProducts) break;
                if (count == products.Count) break;
                float anchorY = startY + (i * (sizeEachY + gap));
                for (int j = 0; j < columns; j++)
                {
                    if (count == maxProducts) break;
                    if (count == products.Count) break;
                    Product product = products[count];
                    float anchorX = startX + (j * (sizeEachX + gap));

                    ProductUI uiProduct = new ProductUI(player, category, product, new Rectangle(anchorX, anchorY, sizeEachX, sizeEachY, 1920, 1080, true), "categories");
                    uiProducts.Add(uiProduct);

                    count++;
                }
            }
            displayedProducts.Remove(player.userID);
            displayedProducts.Add(player.userID, uiProducts);
        }

        private void sendInfo(BasePlayer player, Product product)
        {
            GuiContainer container = new GuiContainer(this, "info", "categories");

            if (string.IsNullOrEmpty(product.imageUrl) && !string.IsNullOrEmpty(product.shortname))
            {
                container.addRawImage("info_img", new Rectangle(1466, 100, 384, 384, 1920, 1080, true), guiCreator.getItemIcon(product.shortname), GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            }
            else
            {
                container.addPanel("info_img", new Rectangle(1466, 100, 384, 384, 1920, 1080, true), GuiContainer.Layer.menu, null, FadeIn, FadeOut, null, product.safeName);
            }
            container.addText("info_Label", new Rectangle(1466, 489, 384, 45, 1920, 1080, true), GuiContainer.Layer.menu, new GuiText(product.name, 26, black90), FadeIn, FadeOut);
            container.addText("info_Description", new Rectangle(1466, 539, 384, 241, 1920, 1080, true), GuiContainer.Layer.menu, new GuiText(product.description, 14, black90), FadeIn, FadeOut);

            container.display(player);
        }

        private void sendCheckout(BasePlayer player, Category category, Product product, int amount = 1)
        {
            if (product == null) return;
            GuiContainer container = new GuiContainer(this, "checkout", "info");
            List<Category> categories = getCategories(player);

            Queue<bool> canAffordIngredients;
            bool canAfford = PluginInstance.canAfford(player, product, amount, out canAffordIngredients);

            //ingredients
            int count = 0;
            foreach (KeyValuePair<string, int> ingredient in product.ingredients)
            {
                if (count == maxIngredients) break;
                bool canAfford_ = canAffordIngredients.Dequeue();
                string displayName;
                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(ingredient.Key);
                if (itemDefinition != null)
                {
                    displayName = itemDefinition.displayName.english;
                    container.addRawImage($"ingredients_icon{count}", new Rectangle(1468, (787 + (count * 50)), 41, 41, 1920, 1080, true), guiCreator.getItemIcon(itemDefinition.shortname), GuiContainer.Layer.menu, null, 0, 0);
                }
                else
                {
                    displayName = ingredient.Key;
                    container.addImage($"ingredients_icon{count}", new Rectangle(1466 + iconMargin, (785 + (count * 50)) + iconMargin, 45 - (2*iconMargin), 45 - (2*iconMargin), 1920, 1080, true), "dollar_icon", GuiContainer.Layer.menu, null, 0, 0);
                }

                container.addText($"ingredients_amount{count}", new Rectangle(1516, (785 + (count * 50)), 95, 45, 1920, 1080, true), GuiContainer.Layer.menu, new GuiText((ingredient.Value * amount).ToString(), 20, canAfford_ ? black90 : halfRed90), 0, 0);
                container.addText($"ingredients_shortname{count}", new Rectangle(1616, (785 + (count * 50)), 234, 45, 1920, 1080, true), GuiContainer.Layer.menu, new GuiText(displayName, 20, canAfford_ ? black90 : halfRed90), 0, 0);
                count++;
            }

            //remaining amount
            int remainingProduct;
            int remainingCategory;
            maxAmountReached(player.userID, product, out remainingProduct);
            maxAmountReached(player.userID, category, out remainingCategory);
            int remaining = Math.Min(remainingProduct, remainingCategory);

            int amountLocal = amount;
            //atc
            Action<BasePlayer, string[]> minusCallback = (bPlayer, input) =>
            {
                if (amountLocal == 1) return;
                sendCheckout(bPlayer, category, product, amountLocal - 1);
            };
            container.addPlainButton("atc_minusbackground", new Rectangle(1466, 985, 45, 45, 1920, 1080, true), GuiContainer.Layer.menu, black60, 0, 0, new GuiText("-", 10, white90), minusCallback);
            container.addPanel("atc_amountbackground", new Rectangle(1516, 985, 45, 45, 1920, 1080, true), GuiContainer.Layer.menu, white30, 0, 0, new GuiText((product.amount*amount).ToString(), 10, black90));
            Action<BasePlayer, string[]> plusCallback = (bPlayer, input) =>
            {
                if (product.amount == 0) return;
                if (amountLocal >= remaining) return;
                sendCheckout(bPlayer, category, product, amountLocal + 1);
            };
            container.addPlainButton("atc_plusbackground", new Rectangle(1566, 985, 45, 45, 1920, 1080, true), GuiContainer.Layer.menu, black60, 0, 0, new GuiText("+", 10, white90), plusCallback);

            //buy button
            Action<BasePlayer, string[]> buyCallback = (bPlayer, input) =>
            {
                if (purchase(bPlayer, category, product, amount))
                {
                    sendCheckout(player, category, product, amount);
                    sendBalance(player);
                }
                foreach(ProductUI uiProduct in displayedProducts[player.userID])
                {
                    uiProduct.refresh();
                }
            };
            container.addPlainButton("buybutton", new Rectangle(1616, 985, 234, 45, 1920, 1080, true), GuiContainer.Layer.menu, canBuy(player, category, product, amount) ? green70 : red70, 0, 0, new GuiText("Buy", 26, white90), buyCallback);

            container.display(player);
        }

        #endregion UI

        #region commands

        private void shopCommand(BasePlayer player, string command, string[] args)
        {
            if (GuiTracker.getGuiTracker(player).getContainer(this, "shop") != null)
            {
                closeShopUi(player);
                return;
            }

            if (!permission.UserHasPermission(player.UserIDString, "shop.use"))
            {
                PrintToChat(player, lang.GetMessage("noPermission", this, player.UserIDString));
                return;
            }
            sendShopUi(player);
        }

        private void closeShop(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;
            closeShopUi(player);
        }

        private void wipeShopCommand(BasePlayer player, string command, string[] args)
        {
            if(!player.IsAdmin)
            {
                PrintToChat(player, lang.GetMessage("noPermission", this, player.UserIDString));
                return;
            }
            customerData.purchaseTrackers.Clear();
            player.ChatMessage("shop data wiped");
            saveData();
        }

        #endregion commands

        #region transaction

        private bool purchase(BasePlayer player, Category category, Product product, int amount)
        {
            //can buy?
            if (!hasPermission(player, category, product))
            {
                string errorMsg = lang.GetMessage("noPermissionToBuy", this, player.UserIDString);
                if (!string.IsNullOrEmpty(category.forbiddenMsg)) errorMsg = category.forbiddenMsg;
                if (!string.IsNullOrEmpty(product.forbiddenMsg)) errorMsg = product.forbiddenMsg;
                guiCreator.customGameTip(player, errorMsg, 2, gametipType.error);
                return false;
            }
            if (!canAfford(player, product, amount))
            {
                guiCreator.customGameTip(player, lang.GetMessage("cantAfford", this, player.UserIDString), 2, gametipType.error);
                return false;
            }
            if(maxAmountReached(player.userID, category) || maxAmountReached(player.userID, product))
            {
                guiCreator.customGameTip(player, lang.GetMessage("maxAmount", this, player.UserIDString), 2, gametipType.error);
                return false;
            }
            int cooldown;
            if (!cooldownElapsed(player.userID, product, out cooldown))
            {
                string remaining = formatTimeSpan(new TimeSpan(0, 0, cooldown));
                guiCreator.customGameTip(player, string.Format(lang.GetMessage("cooldown", this, player.UserIDString), remaining), 2, gametipType.error);
                return false;
            }
            else if(!cooldownElapsed(player.userID, category, out cooldown))
            {
                string remaining = formatTimeSpan(new TimeSpan(0, 0, cooldown));
                guiCreator.customGameTip(player, string.Format(lang.GetMessage("cooldown", this, player.UserIDString), remaining), 2, gametipType.error);
                return false;
            }


            //take price
            if (!permission.UserHasPermission(player.UserIDString, "shop.free"))
            {
                foreach (KeyValuePair<string, int> ingredient in product.ingredients)
                {
                    ItemDefinition itemDefinition = ItemManager.FindItemDefinition(ingredient.Key);
                    if (itemDefinition != null)
                    {
                        player.inventory.Take(null, itemDefinition.itemid, ingredient.Value);
                    }
                    else if (ingredient.Key.ToUpper() == "POINTS" || ingredient.Key.ToUpper() == "RP" || ingredient.Key.ToUpper() == "REWARD POINTS")
                    {
                        takePoints(player, ingredient.Value);
                    }
                }
            }

            //tracker
            if(category.maxAmount != 0 || category.cooldownSeconds != 0)
            {
                Tracker tracker = getTracker(player.userID, category.name);
                if (tracker == null) tracker = new Tracker { name = category.name };
                tracker.lastPurchase = DateTime.Now;
                if(category.maxAmount != 0) tracker.totalAmount += amount;
                customerData.purchaseTrackers[player.userID].Add(tracker);
                saveData();
            }
            if (product.maxAmount != 0 || product.cooldownSeconds != 0)
            {
                Tracker tracker = getTracker(player.userID, product.name);
                if (tracker == null) tracker = new Tracker { name = product.name };
                tracker.lastPurchase = DateTime.Now;
                if(product.maxAmount != 0) tracker.totalAmount += amount;
                customerData.purchaseTrackers[player.userID].Add(tracker);
                saveData();
            }

            //result
            if (!string.IsNullOrEmpty(product.command))
            {
                //execute command
                string command = Regex.Replace(product.command, @"{steamid}", player.userID.ToString());
                command = Regex.Replace(command, @"{amount}", (product.amount).ToString());
                command = Regex.Replace(command, @"{name}", player.displayName);
                command = Regex.Replace(command, @"{x}", player.transform.position.x.ToString());
                command = Regex.Replace(command, @"{y}", player.transform.position.y.ToString());
                command = Regex.Replace(command, @"{z}", player.transform.position.z.ToString());

                for (int i = 0; i < amount; i++)
                {
                    Server.Command(command);
                }
                Effect.server.Run("assets/prefabs/deployable/vendingmachine/effects/vending-machine-purchase-human.prefab", player.transform.position);

                log(player, product, amount);
                return true;
            }
            else if (!string.IsNullOrEmpty(product.shortname) && product.amount != 0)
            {
                //give item
                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(product.shortname);
                if (itemDefinition == null)
                {
                    Puts($"purchase: shortname {product.shortname} is invalid!");
                    return false;
                }

                //stacksize handling
                int totalAmount = amount * product.amount;
                while (totalAmount > 0)
                {
                    int stacksize = Math.Min(itemDefinition.stackable, totalAmount);
                    Item item = ItemManager.Create(itemDefinition, stacksize);
                    if (item != null) player.GiveItem(item);
                    totalAmount -= stacksize;
                }
                Effect.server.Run("assets/prefabs/deployable/vendingmachine/effects/vending-machine-purchase-human.prefab", player.transform.position);

                log(player, product, amount);
                return true;
            }

            return false;
        }

        #endregion transaction

        #region helpers

        private string formatTimeSpan(TimeSpan span)
        {
            int d = (int)span.TotalDays;
            int h = (int)(span.TotalHours-(d * 24));
            int m = (int)(span.TotalMinutes - (h * 60) - (d * 24 * 60));
            int s = (int)(span.TotalSeconds - (m * 60) - (h * 60 * 60) - (d * 24 * 60 *60));
            StringBuilder sb = new StringBuilder();
            if (d != 0) sb.Append($"{d}d ");
            if (h != 0) sb.Append($"{h}h ");
            if (m != 0) sb.Append($"{m}m ");
            if (s != 0) sb.Append($"{s}s");
            return sb.ToString();
        }

        private void log(BasePlayer player, Product product, int amount)
        {
            LogToFile("shopPurchases", $"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss")} {player.displayName } bought {Math.Max(1,product.amount)*amount} x {product.name}" , this);
        }

        private Tracker getTracker(ulong userID, string name)
        {
            List<Tracker> userTrackers;
            if (!customerData.purchaseTrackers.TryGetValue(userID, out userTrackers))
            {
                userTrackers = new List<Tracker>();
                customerData.purchaseTrackers.Add(userID, userTrackers);
                saveData();
            }
            foreach(Tracker tracker in userTrackers)
            {
                if (tracker.name == name) return tracker;
            }
            return null;
        }

        private bool maxAmountReached(ulong userID, ITrackable input)
        {
            int temp;
            return maxAmountReached(userID, input, out temp);
        }

        private bool maxAmountReached(ulong userID, ITrackable input, out int remainingAmount)
        {
            remainingAmount = int.MaxValue;
            if (input.maxAmount == 0) return false;
            remainingAmount = input.maxAmount;
            Tracker tracker = getTracker(userID, input.name);
            if (tracker == null) return false;
            remainingAmount = input.maxAmount - tracker.totalAmount;
            if (remainingAmount <= 0) return true;
            return false;
        }

        private bool cooldownElapsed(ulong userID, ITrackable input)
        {
            int temp;
            return cooldownElapsed(userID, input, out temp);
        }

        private bool cooldownElapsed(ulong userID, ITrackable input, out int remainingSeconds)
        {
            remainingSeconds = 0;
            Tracker tracker = getTracker(userID, input.name);
            if (tracker == null) return true;
            remainingSeconds = (int)(input.cooldownSeconds - DateTime.Now.Subtract(tracker.lastPurchase).TotalSeconds);
            if (remainingSeconds <= 0) return true;
            return false;
        }

        private List<Category> getCategories(BasePlayer player)
        {
            if (!config.hideNonPermission) return config.Categories;

            List<Category> output = new List<Category>();

            foreach (Category category in config.Categories)
            {
                if (hasPermission(player, category)) output.Add(category);
            }

            return output;
        }

        private List<Product> getProducts(BasePlayer player, Category category)
        {
            if (!config.hideNonPermission) return category.products;

            if (!hasPermission(player, category)) return null;
            List<Product> output = new List<Product>();

            foreach (Product product in category.products)
            {
                if (hasPermission(player, category, product)) output.Add(product);
            }

            return output;
        }

        private bool canBuy(BasePlayer player, Category category, Product product, int amount)
        {
            if(!hasPermission(player, category, product)) return false;
            if (!canAfford(player, product, amount)) return false;
            if (maxAmountReached(player.userID, category)) return false;
            if (maxAmountReached(player.userID, product)) return false;
            if (!cooldownElapsed(player.userID, category)) return false;
            if (!cooldownElapsed(player.userID, product)) return false;
            return true;
        }

        private bool hasPermission(BasePlayer player, Category category, Product product = null)
        {
            if (!string.IsNullOrEmpty(category.permission) && !permission.UserHasPermission(player.UserIDString, category.permission)) return false;
            if (product != null)
            {
                if (!string.IsNullOrEmpty(product.permission) && !permission.UserHasPermission(player.UserIDString, product.permission)) return false;
            }

            return true;
        }

        private bool canAfford(BasePlayer player, Product product, int amount)
        {
            Queue<bool> temp;
            return canAfford(player, product, amount, out temp);
        }

        private bool canAfford(BasePlayer player, Product product, int amount, out Queue<bool> ingredients)
        {
            ingredients = new Queue<bool>();
            bool total = true;
            foreach (KeyValuePair<string, int> ingredient in product.ingredients)
            {
                int balance = 0;
                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(ingredient.Key);
                if (itemDefinition != null)
                {
                    balance = player.inventory.GetAmount(itemDefinition.itemid);
                }
                else if (ingredient.Key.ToUpper() == "POINTS" || ingredient.Key.ToUpper() == "RP" || ingredient.Key.ToUpper() == "REWARD POINTS")
                {
                    balance = getPoints(player);
                }

                if (balance < (ingredient.Value * amount))
                {
                    total = false;
                    ingredients.Enqueue(false);
                }
                else
                {
                    ingredients.Enqueue(true);
                }
            }
            return total;
        }

        private int getPoints(BasePlayer player)
        {
            if (config.useServerRewards)
            {
                object answer = ServerRewards?.Call<int>("CheckPoints", player.userID);
                if (answer != null) return (int)answer;
            }
            else if (config.useEconomics)
            {
                object answer = Economics?.Call<int>("Balance", player.userID);
                if (answer != null) return (int)answer;
            }
            return 0;
        }

        private bool takePoints(BasePlayer player, int amount)
        {
            if (config.useServerRewards)
            {
                object answer = ServerRewards?.Call<int>("TakePoints", player.userID, amount);
                if (answer == null) return false;
                else return true;
            }
            else if (config.useEconomics)
            {
                object answer = Economics?.Call<int>("Withdraw", player.userID, (double)amount);
                if (answer == null) return false;
            }
            return false;
        }

        #endregion helpers

        #region data management
        private class CustomerData
        {
            public Dictionary<ulong, List<Tracker>> purchaseTrackers = new Dictionary<ulong, List<Tracker>>();

            public CustomerData()
            {
            }
        }

        void saveData()
        {
            try
            {
                CustomerDataFile.WriteObject(customerData);
            }
            catch (Exception E)
            {
                Puts(E.ToString());
            }
        }

        void loadData()
        {
            try
            {
                customerData = CustomerDataFile.ReadObject<CustomerData>();
            }
            catch (Exception E)
            {
                Puts(E.ToString());
            }
        }
        #endregion

        #region Config

        private static ConfigData config;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Chat Command")]
            public string command;

            [JsonProperty(PropertyName = "use Server Rewards")]
            public bool useServerRewards;

            [JsonProperty(PropertyName = "use Economics")]
            public bool useEconomics;

            [JsonProperty(PropertyName = "Hide products from customers without permission")]
            public bool hideNonPermission;

            [JsonProperty(PropertyName = "Product Panel Background URL (recommended size: 1400px x 940px)")]
            public string background;

            [JsonProperty(PropertyName = "Default Infopanel Background URL (recommended size: 400px x 400px)")]
            public string defaultInfoBG;

            [JsonProperty(PropertyName = "No Permission Overlay (recommended size: 400px x 400px)")]
            public string noPermissionOverlay;

            [JsonProperty(PropertyName = "cooldown Overlay (recommended size: 400px x 400px)")]
            public string cooldownOverlay;

            [JsonProperty(PropertyName = "cooldown Interval (increase to reduce lag)")]
            public float cooldownInterval;

            [JsonProperty(PropertyName = "Economics/ServerRewards Point Icon")] 
            public string currencyIcon;

            [JsonProperty(PropertyName = "Categories")]
            public List<Category> Categories;
        }

        private ConfigData getDefaultConfig()
        {
            return new ConfigData
            {
                command = "shop",
                useServerRewards = true,
                useEconomics = false,
                hideNonPermission = false,
                background = "https://i.imgur.com/O7T9ow3.jpg",
                defaultInfoBG = "https://i.imgur.com/tQPYSIf.jpg",
                noPermissionOverlay = "https://i.imgur.com/pKTsSZg.png",
                cooldownOverlay = "https://i.imgur.com/55uXmZt.png",
                cooldownInterval = 1,
                currencyIcon = "https://i.gyazo.com/acbdabe16b83e4312af80bbce31bc36c.png",
                Categories = new List<Category>
                {
                    new Category
                    {
                        name = "Home",
                        fontsize = 24,
                        categoryicon = "https://i.imgur.com/4dWsoUa.png",
                        permission = "",
                        forbiddenMsg = "",
                        maxAmount = 2,
                        cooldownSeconds = 20,
                        products = new List<Product>
                        {
                            new Product
                            {
                                name = "Assault Rifle",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 300,
                                command = "",
                                shortname = "rifle.ak",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "metal.refined", 50 },
                                    { "wood", 200 },
                                    { "riflebody", 1 },
                                    { "metalspring", 4 }
                                }
                            },
                            new Product
                            {
                                name = "MiniCopter",
                                fontsize = 24,
                                description = "The minicopter is currently one of the three aquirable flying vehicles in the game, it uses low grade fuel to fly and has 2 seats for 2 players. They are always spawning on roads. ",
                                imageUrl = "https://vignette.wikia.nocookie.net/play-rust/images/9/95/494EDB03-BAEA-42BB-8FAE-748F0D2B50A9.png/revision/latest/scale-to-width-down/340?cb=20191107202537",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "portablevehicles.give {steamid} minicopter",
                                shortname = "",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "reward points", 100 },
                                }
                            }
                        }
                    },
                    new Category
                    {
                        name = "Components",
                        fontsize = 24,
                        categoryicon = "gears",
                        permission = "",
                        forbiddenMsg = "",
                        maxAmount = 0,
                        cooldownSeconds = 0,
                        products = new List<Product>
                        {
                            new Product
                            {
                                name = "Propane Tank",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "propanetank",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "RP", 50 }
                                }
                            },
                            new Product
                            {
                                name = "Gear",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "gears",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "metal.fragments", 25 },
                                    { "scrap", 100 }
                                }
                            },
                            new Product
                            {
                                name = "Metal Blade",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "metalblade",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "RP", 50 }
                                }
                            },
                            new Product
                            {
                                name = "Metal Pipe",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "metalpipe",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "RP", 50 }
                                }
                            },
                            new Product
                            {
                                name = "Metal Spring",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "shop.spring",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "metalspring",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "metal.refined", 2 },
                                    { "scrap", 50 }
                                }
                            },
                            new Product
                            {
                                name = "Rifle Body",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "riflebody",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "RP", 50 }
                                }
                            },
                            new Product
                            {
                                name = "Roadsigns",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "roadsigns",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "RP", 50 }
                                }
                            },
                            new Product
                            {
                                name = "Rope",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "rope",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "RP", 50 }
                                }
                            },
                            new Product
                            {
                                name = "SMG Body",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "smgbody",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "RP", 50 }
                                }
                            },
                            new Product
                            {
                                name = "Semi Automatic Body",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "semibody",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "RP", 50 }
                                }
                            },
                            new Product
                            {
                                name = "Sewing Kit",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "sewingkit",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "RP", 50 }
                                }
                            },
                            new Product
                            {
                                name = "Sheet Metal",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "sheetmetal",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "RP", 50 }
                                }
                            },
                            new Product
                            {
                                name = "Tarp",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "tarp",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "RP", 50 }
                                }
                            },
                            new Product
                            {
                                name = "Tech Trash",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "techparts",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "RP", 50 }
                                }
                            }
                        }
                    },
                    new Category
                    {
                        name = "Explosives",
                        fontsize = 24,
                        categoryicon = "explosive.timed",
                        permission = "shop.explosives",
                        forbiddenMsg = "You need to be a gold donator to buy this!",
                        maxAmount = 0,
                        cooldownSeconds = 0,
                        products = new List<Product>
                        {
                            new Product
                            {
                                name = "C4",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "explosive.timed",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "points", 1000 },
                                }
                            },
                            new Product
                            {
                                name = "Rocket",
                                fontsize = 24,
                                description = "",
                                imageUrl = "",
                                permission = "",
                                forbiddenMsg = "You specifically don't have permission for this, because I don't like you :P",
                                maxAmount = 0,
                                cooldownSeconds = 0,
                                command = "",
                                shortname = "ammo.rocket.basic",
                                amount = 1,
                                ingredients = new Dictionary<string, int>
                                {
                                    { "points", 300 },
                                }
                            }
                        }
                    }
                }
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<ConfigData>();
            }
            catch
            {
                Puts("Config data is corrupted, replacing with default");
                config = getDefaultConfig();
            }

            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        protected override void LoadDefaultConfig() => config = getDefaultConfig();

        #endregion Config

        #region Localization

        private Dictionary<string, string> messages = new Dictionary<string, string>()
        {
            {"noPermission", "You don't have permission to use this command!"},
            {"noPermissionToBuy", "You don't have permission to buy this!" },
            {"cantAfford", "You can't afford this!"},
            {"maxAmount", "You can't buy any more of this!" },
            {"cooldown", "You have to wait {0} before you can buy more!" }
        };

        #endregion Localization
    }
}