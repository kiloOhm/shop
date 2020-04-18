// Requires: GUICreator

using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using static Oxide.Plugins.GUICreator;

namespace Oxide.Plugins
{
    [Info("shop", "OHM", "1.0.0")]
    [Description("The ultimate GUI Shop")]
    internal class shop : RustPlugin
    {
        #region references

        [PluginReference]
        private Plugin ServerRewards, Economics;

        private GUICreator guiCreator;

        #endregion references

        #region global

        private DynamicConfigFile ConfigFile;

        private static shop PluginInstance = null;

        public shop()
        {
            PluginInstance = this;
        }

        #endregion global

        #region classes

        [JsonObject(MemberSerialization.OptIn)]
        public class Category
        {
            [JsonProperty(PropertyName = "Name")]
            public string name;

            [JsonProperty(PropertyName = "Fontsize")]
            public int fontsize;

            public string safeName => Regex.Replace(name, @" ", "");

            [JsonProperty(PropertyName = "Category icon url/shortname (recommended size: 60px x 60px, item shortname for item icon)")]
            public string categoryicon;

            [JsonProperty(PropertyName = "Permission to craft (if empty, no permission required)")]
            public string permission;

            [JsonProperty(PropertyName = "Missing Permission Message")]
            public string forbiddenMsg;

            [JsonProperty(PropertyName = "Products")]
            public List<Product> products = new List<Product>();
        }

        [JsonObject(MemberSerialization.OptIn)]
        public class Product
        {
            [JsonProperty(PropertyName = "Name")]
            public string name;

            [JsonProperty(PropertyName = "Fontsize")]
            public int fontsize;

            public string safeName => Regex.Replace(name, @" ", "");

            [JsonProperty(PropertyName = "Description (max. 200 characters)")]
            public string description = null;

            [JsonProperty(PropertyName = "Image URL (recommended size: 400px x 400px)")]
            public string imageUrl;

            [JsonProperty(PropertyName = "Permission to craft (if empty, no permission required)")]
            public string permission;

            [JsonProperty(PropertyName = "Missing Permission Message")]
            public string forbiddenMsg;

            [JsonProperty(PropertyName = "Command to be executed on purchase")]
            public string command;

            [JsonProperty(PropertyName = "Item shortname")]
            public string shortname;

            [JsonProperty(PropertyName = "Item amount")]
            public int amount;

            [JsonProperty(PropertyName = "Ingredients (max. 4)")]
            public Dictionary<string, int> ingredients;
        }

        #endregion classes

        #region oxide hooks

        private void Init()
        {
            //permissions
            permission.RegisterPermission("shop.use", this);
            permission.RegisterPermission("shop.free", this);

            //Umod API
            ConfigFile = Interface.Oxide.DataFileSystem.GetFile("shop/posData");

            lang.RegisterMessages(messages, this);
        }

        private void OnServerInitialized()
        {
            //commands
            cmd.AddChatCommand(config.command, this, nameof(shopCommand));
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

        #region global parameters

        private int maxProducts = 18;
        private int maxIngredients = 4;

        private float FadeIn = 0.1f;
        private float FadeOut = 0.1f;

        private int iconMargin = 5;
        private int productMargin = 10;

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
            GuiContainer container = new GuiContainer(this, "products", "categories");
            List<Product> products = getProducts(player, category);

            int startX = 75;
            int startY = 105;
            int gap = 10;
            int rows = 3;
            int columns = 6;
            float sizeEachX = (1391 - ((columns + 1) * gap)) / columns;
            float sizeEachY = (940 - ((rows + 1) * gap)) / rows;

            Product activeProduct = null;

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
                    container.addPanel($"products_{product.safeName}_imgbg", new Rectangle((j == 0) ? startX : anchorX, (i == 0) ? startY : anchorY, sizeEachX, sizeEachX, 1920, 1080, true), GuiContainer.Layer.menu, white30, FadeIn, FadeOut);
                    if (string.IsNullOrEmpty(product.imageUrl) && !string.IsNullOrEmpty(product.shortname))
                    {
                        container.addRawImage($"products_{product.safeName}_img", new Rectangle((j == 0) ? (startX + productMargin) : (anchorX + productMargin), (i == 0) ? (startY + productMargin) : (anchorY + productMargin), sizeEachX - (2 * productMargin), sizeEachX - (2 * productMargin), 1920, 1080, true), guiCreator.getItemIcon(product.shortname), GuiContainer.Layer.menu, null, FadeIn, FadeOut);
                    }
                    else
                    {
                        container.addPanel($"products_{product.safeName}_img", new Rectangle((j == 0) ? (startX + productMargin) : (anchorX + productMargin), (i == 0) ? (startY + productMargin) : (anchorY + productMargin), sizeEachX - (2 * productMargin), sizeEachX - (2 * productMargin), 1920, 1080, true), GuiContainer.Layer.menu, null, FadeIn, FadeOut, null, product.safeName);
                    }
                    container.addPanel($"products_{product.safeName}_label", new Rectangle((j == 0) ? startX : anchorX, ((i == 0) ? startY : anchorY) + 5 + sizeEachX, sizeEachX, sizeEachY - (5 + sizeEachX), 1920, 1080, true), GuiContainer.Layer.menu, white30, FadeIn, FadeOut, new GuiText(product.name, product.fontsize, black90));
                    int index = count;
                    Action<BasePlayer, string[]> callback = (bPlayer, input) =>
                    {
                        if (activeProduct == products[index]) return;
                        GuiTracker.getGuiTracker(bPlayer).destroyGui(this, "info");
                        sendInfo(bPlayer, products[index]);
                        sendCheckout(bPlayer, category, products[index]);
                        activeProduct = products[index];
                    };
                    
                    if (!hasPermission(player, category, product))
                    {
                        container.addPlainPanel($"products_{product.safeName}_forbidden_bgpanel", new Rectangle((j == 0) ? startX : anchorX, (i == 0) ? startY : anchorY, sizeEachX, sizeEachX, 1920, 1080, true), GuiContainer.Layer.menu, black40, FadeIn, FadeOut, true);
                        container.addImage($"products_{product.safeName}_forbidden", new Rectangle(0, 0, 1, 1), "no_permission", $"products_{product.safeName}_forbidden_bgpanel", white90, FadeIn, FadeOut);
                    }

                    container.addPlainButton($"products_{product.safeName}_button", new Rectangle((j == 0) ? startX : anchorX, (i == 0) ? startY : anchorY, sizeEachX, sizeEachY, 1920, 1080, true), GuiContainer.Layer.menu, callback: callback);
                    count++;
                }
            }

            container.display(player);
        }

        private void sendInfo(BasePlayer player, Product product)
        {
            GuiContainer container = new GuiContainer(this, "info", "products");

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

            Queue<bool> canAfford = PluginInstance.canAfford(player, product, amount);

            //ingredients
            int count = 0;
            foreach (KeyValuePair<string, int> ingredient in product.ingredients)
            {
                if (count == maxIngredients) break;
                bool canAfford_ = canAfford.Dequeue();
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
            if (!canAfford(player, product, amount).Last<bool>())
            {
                guiCreator.customGameTip(player, lang.GetMessage("cantAfford", this, player.UserIDString), 2, gametipType.error);
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

        private void log(BasePlayer player, Product product, int amount)
        {
            LogToFile("shopPurchases", $"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss")} {player.displayName } bought {Math.Max(1,product.amount)*amount} x {product.name}" , this);
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
            if (!canAfford(player, product, amount).Last<bool>()) return false;
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

        private Queue<bool> canAfford(BasePlayer player, Product product, int amount)
        {
            Queue<bool> output = new Queue<bool>();
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
                    output.Enqueue(false);
                }
                else
                {
                    output.Enqueue(true);
                }
            }
            output.Enqueue(total);
            return output;
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
                noPermissionOverlay = "https://i.imgur.com/BBuqkPf.png",
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
            {"cantAfford", "You can't afford this!"}
        };

        #endregion Localization
    }
}