using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        enum MarketTab { Buy, Sell, History, Shop, Recycle }
        enum MarketDetail { Buy, List, Cancel, Shop, Recycle }

        const string MarketUrlKey = "Dustline.Market.Url";
        static readonly string[] MarketTabs = { "购买", "出售", "交易记录", "系统购入", "系统回收" };
        readonly MarketClient marketClient = new MarketClient();
        MarketTab marketTab;
        bool marketBusy, marketOwnListings, marketRecovery, marketUrlLoaded;
        string marketStatus, marketLoginName, marketItemId;
        ItemKind? marketCategory;
        int marketPage, marketRender;
        float marketCategoryScroll = 1f;
        RectTransform marketRoot;
        InventoryScroll marketCategoryView;

        public void ShowMarket()
        {
            if (session.Phase != SessionPhase.Menu) return;
            if (!marketUrlLoaded)
            {
                marketUrlLoaded = true;
                marketClient.SetEndpoint(PlayerPrefs.GetString(MarketUrlKey, ""));
            }
            marketRoot = Display(ScreenPage.Market, "Account market", wide: true);
            marketCategoryView = null;
            SetPageRestore(ShowMarket, CaptureMarketScroll);
            reflowWidePage = DrawMarket;
            if (marketClient.SignedIn && !marketBusy) RunMarket(marketClient.Refresh, "已同步服务器账户。");
            else DrawMarket();
        }

        bool MarketVisible => this != null && marketRoot != null && marketRoot == panelRoot && current == ScreenPage.Market;

        void CaptureMarketScroll()
        {
            if (marketCategoryView != null) marketCategoryScroll = Mathf.Clamp01(marketCategoryView.verticalNormalizedPosition);
        }

        void DrawMarket()
        {
            if (!MarketVisible) return;
            CaptureMarketScroll();
            marketCategoryView = null;
            CloseItemPopup();
            Clear(marketRoot);
            marketRender++;
            float width = marketRoot.rect.width;
            var state = marketClient.State;
            WideHeader(marketRoot, "交易市场", "", false);
            MarketText(marketRoot, "服务器账户 · 与离线档独立", 16, width * .37f, 6, width * .31f, 37, color: Muted);
            var balance = state == null ? "未同步" : MarketPrice(state.Credits) + " 信用点";
            MarketText(marketRoot, marketClient.SignedIn ? "账户 " + MarketDisplay(marketClient.Account, 32) + "  /  " + balance : "未登录",
                17, width * .68f, 6, width * .32f - 14, 37, TextAnchor.MiddleRight, Aqua);

            if (marketClient.SignedIn)
            {
                for (int i = 0; i < MarketTabs.Length; i++)
                {
                    var tab = (MarketTab)i;
                    MarketButton(marketRoot, MarketTabs[i], 12 + 126 * i, 57, 118, 42, () => SelectMarketTab(tab), tab == marketTab);
                }
                MarketButton(marketRoot, "刷新", width - 206, 57, 91, 42, () => RunMarket(marketClient.Refresh, "已同步服务器账户。"));
                MarketButton(marketRoot, "退出账号", width - 107, 57, 95, 42, MarketSignOut,
                    enabled: marketClient.PendingAction == null);

                if (state == null)
                {
                    MarketText(marketRoot, "暂无可用账户数据，请刷新。", 24, 12, 144, width - 24, 96, TextAnchor.MiddleCenter, Muted);
                }
                else if (marketTab == MarketTab.History) DrawMarketHistory(state);
                else DrawMarketTradePage(state);
            }
            else DrawMarketConnection();

            Fill(At("Market status strip", marketRoot, 0, 626, width, 54), Ink);
            var pending = marketClient.PendingAction;
            string status = marketBusy ? "正在等待服务器… 可返回大厅。"
                : pending.HasValue ? "原单待确认：" + MarketActionName(pending.Value) + "。" + (marketStatus ?? "")
                : marketStatus ?? "市场仅使用服务器物品与信用点。";
            MarketText(marketRoot, status, 16, 13, 630, width - (pending.HasValue && marketClient.SignedIn ? 180 : 26), 43,
                color: pending.HasValue ? Amber : Muted);
            if (pending.HasValue && marketClient.SignedIn)
                MarketButton(marketRoot, "重试原单", width - 153, 632, 141, 40,
                    () => RunMarket(marketClient.Retry, MarketActionName(pending.Value) + "已确认。"), true);
        }

        void DrawMarketConnection()
        {
            float width = Mathf.Min(684, marketRoot.rect.width - 40);
            var card = At("Market connection", marketRoot, (marketRoot.rect.width - width) * .5f, 97, width, 489);
            Fill(card, Ink);
            Fill(At("Connection rule", card, 0, 0, width, 2), Aqua);
            Label(card, "服务器市场账号", 28, 24, 17, width - 48, 45, TextAnchor.MiddleLeft);
            Label(card, "市场仓库与余额由服务器管理，不会导入本机仓库、突袭掉落或离线信用点。",
                17, 24, 68, width - 48, 52, color: Muted);
            Label(card, "服务地址", 16, 24, 132, width - 48, 24, color: Aqua);
            var url = MarketInput(card, "Market URL", "HTTPS 或本机回环 HTTP（主机和端口）", marketClient.Url, 24, 158, width - 48, 45, 512);
            string lockedAccount = marketClient.PendingAccount;
            url.interactable = !marketBusy && lockedAccount == null;
            Label(card, "账号", 16, 24, 229, width - 48, 24, color: Aqua);
            var name = MarketInput(card, "Market account", "3–32 位字母、数字、下划线或连字符", lockedAccount ?? marketLoginName ?? "",
                24, 255, width - 48, 45, 32);
            name.interactable = !marketBusy && lockedAccount == null;
            Label(card, "密码", 16, 24, 326, width - 48, 24, color: Aqua);
            var password = MarketInput(card, "Market password", "8–128 个字符", "", 24, 352, width - 48, 45, 128, InputField.ContentType.Password);
            MarketButton(card, "登录", 24, 422, (width - 58) * .5f, 46,
                () => AuthenticateMarket(url.text, name.text, password, false), true);
            MarketButton(card, "注册独立账号", width * .5f + 5, 422, (width - 58) * .5f, 46,
                () => AuthenticateMarket(url.text, name.text, password, true), enabled: lockedAccount == null);
        }

        void AuthenticateMarket(string url, string name, InputField password, bool register)
        {
            marketLoginName = name.Trim();
            var secret = password.text;
            password.DeactivateInputField();
            password.SetTextWithoutNotify("");
            if (!marketClient.SetEndpoint(url))
            {
                marketStatus = "仅填写协议、主机和端口；非回环地址必须用 HTTPS。";
                DrawMarket();
                return;
            }
            if (PlayerPrefs.GetString(MarketUrlKey, "") != marketClient.Url)
            {
                PlayerPrefs.SetString(MarketUrlKey, marketClient.Url);
                PlayerPrefs.Save();
            }
            RunMarket(() => marketClient.Authenticate(marketLoginName, secret, register), register ? "注册成功，请登录。" : "已登录服务器账户。");
            secret = null;
        }

        void MarketSignOut()
        {
            if (!marketClient.SignOut()) return;
            marketLoginName = null;
            marketStatus = "已退出服务器账户。";
            marketTab = MarketTab.Buy;
            marketOwnListings = marketRecovery = false;
            marketCategory = null;
            marketItemId = null;
            marketCategoryScroll = 1f;
            marketCategoryView = null;
            marketPage = 0;
            DrawMarket();
        }

        void SelectMarketTab(MarketTab tab)
        {
            if (marketTab == tab) return;
            marketTab = tab;
            marketRecovery = false;
            marketCategory = null;
            marketItemId = null;
            marketCategoryView = null;
            marketCategoryScroll = 1f;
            marketPage = 0;
            DrawMarket();
        }

        void DrawMarketTradePage(MarketState state)
        {
            const float railWidth = 186, x = railWidth + 22;
            float w = marketRoot.rect.width - x - 12;
            var rail = At("Market category rail", marketRoot, 12, 109, railWidth, 506);
            Fill(rail, Ink);
            var body = At("Market trade grid", marketRoot, x, 109, w, 506);
            var listings = state.Listings.Where(l => l != null && !string.IsNullOrWhiteSpace(l.Id) && l.UnitPrice > 0 && MarketStack(l.Item));
            var own = listings.Where(l => string.Equals(l.Seller, state.Account, StringComparison.OrdinalIgnoreCase));
            var items = state.Items.Where(MarketStack);
            if (marketTab == MarketTab.Recycle) items = items.Where(i => MarketQuote(state, i.ItemId) != null);
            int recovered = items.Count(item => item.Area == CargoArea.Legacy);
            if (recovered == 0 && marketRecovery) ResetMarketFilter(false);
            var owned = items.Where(item => (item.Area == CargoArea.Legacy) == marketRecovery);
            var stock = state.Products.Where(product => product.ForSale);
            IEnumerable<string> choices = marketTab == MarketTab.Buy ? listings.Select(l => l.Item.ItemId)
                : marketTab == MarketTab.Shop ? stock.Select(product => product.Id)
                : marketTab == MarketTab.Sell && marketOwnListings ? own.Select(l => l.Item.ItemId)
                : owned.Select(i => i.ItemId);
            DrawMarketCategories(rail, choices, railWidth);

            switch (marketTab)
            {
                case MarketTab.Buy:
                    DrawMarketBuy(body, w, listings);
                    break;
                case MarketTab.Shop:
                    MarketText(body, "服务端系统商店  /  服务器报价", 19, 0, 0, w, 43);
                    var products = stock.Where(p => MarketMatches(p.Id)).OrderBy(p => MarketItemName(p.Id), StringComparer.Ordinal).ToArray();
                    DrawMarketCards(body, w, products, "本类别暂无系统商品。", (card, product) =>
                        MarketCard(card, product.Id, null, "系统单价 " + MarketPrice(product.Value), "查看并购入 ›",
                            anchor => ShowMarketDetail(anchor, MarketDetail.Shop, product.Id, null)));
                    break;
                case MarketTab.Sell:
                    MarketButton(body, "市场仓库 / 上架", 0, 0, 156, 39, () => SelectMarketStorage(false, false), !marketOwnListings && !marketRecovery);
                    MarketButton(body, "我的挂单 / 撤单", 164, 0, 156, 39, () => SelectMarketStorage(true, false), marketOwnListings);
                    if (recovered > 0)
                        MarketButton(body, "恢复区 · " + recovered, 328, 0, 130, 39, () => SelectMarketStorage(false, true), marketRecovery);
                    MarketText(body, "只使用服务器账号仓库", 15, 468, 0, w - 468, 39, TextAnchor.MiddleRight, Muted);
                    if (marketOwnListings)
                    {
                        var orders = own.Where(l => MarketMatches(l.Item.ItemId)).OrderBy(l => l.UnitPrice).ThenBy(l => l.Id, StringComparer.Ordinal).ToArray();
                        DrawMarketCards(body, w, orders, "没有符合条件的挂单。", (card, l) =>
                            MarketCard(card, l.Item.ItemId, l.Item, "单价 " + MarketPrice(l.UnitPrice), "查看并撤单 ›",
                                anchor => ShowMarketDetail(anchor, MarketDetail.Cancel, l.Item.ItemId, l.Item, l)));
                        break;
                    }
                    DrawMarketOwned(body, state, owned, w, false);
                    break;
                case MarketTab.Recycle:
                    if (recovered > 0)
                    {
                        MarketButton(body, "市场仓库", 0, 0, 126, 39, () => SelectMarketStorage(false, false), !marketRecovery);
                        MarketButton(body, "恢复区 · " + recovered, 134, 0, 130, 39, () => SelectMarketStorage(false, true), marketRecovery);
                    }
                    MarketText(body, "服务端系统回收  /  服务器报价", 19, recovered > 0 ? 276 : 0, 0, w - (recovered > 0 ? 276 : 0), 43);
                    DrawMarketOwned(body, state, owned, w, true);
                    break;
            }
        }

        void SelectMarketStorage(bool listings, bool recovery)
        {
            if (marketOwnListings == listings && marketRecovery == recovery) return;
            marketOwnListings = listings;
            ResetMarketFilter(recovery);
            DrawMarket();
        }

        void ResetMarketFilter(bool recovery)
        {
            marketRecovery = recovery;
            marketCategory = null;
            marketItemId = null;
            marketCategoryView = null;
            marketCategoryScroll = 1f;
            marketPage = 0;
        }

        void DrawMarketCategories(RectTransform rail, IEnumerable<string> source, float width)
        {
            var ids = source.Distinct(StringComparer.Ordinal).ToArray();
            var expanded = !marketCategory.HasValue ? Array.Empty<string>()
                : ids.Where(id => MarketDefinition(id)?.Kind == marketCategory).OrderBy(MarketItemName, StringComparer.Ordinal).ToArray();
            var unknown = ids.Where(id => MarketDefinition(id) == null).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            if (marketItemId != null && !ids.Contains(marketItemId)) marketItemId = null;
            MarketText(rail, "物品类别", 19, 10, 4, width - 20, 39, color: Aqua);
            const float height = 454, row = 39;
            float contentHeight = row * (ShopTabs.Length + 1 + expanded.Length + unknown.Length);
            var viewport = At("Market category viewport", rail, 5, 44, width - 10, height);
            Fill(viewport, Color.clear, true);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = At("Market categories", viewport, 0, 0, width - 10, Mathf.Max(height, contentHeight));
            if (contentHeight > height)
            {
                marketCategoryView = viewport.gameObject.AddComponent<InventoryScroll>();
                marketCategoryView.viewport = viewport;
                marketCategoryView.content = content;
                marketCategoryView.horizontal = false;
                marketCategoryView.inertia = false;
                marketCategoryView.movementType = ScrollRect.MovementType.Clamped;
                marketCategoryView.scrollSensitivity = 40;
                content.anchoredPosition = new Vector2(0, (1 - marketCategoryScroll) * (contentHeight - height));
                var view = marketCategoryView;
                view.onValueChanged.AddListener(value =>
                {
                    if (marketCategoryView == view) marketCategoryScroll = Mathf.Clamp01(value.y);
                });
            }
            float y = 0;
            Add("全部", () => SelectMarketCategory(null, null), !marketCategory.HasValue && marketItemId == null);
            foreach (var entry in ShopTabs)
            {
                var kind = entry.kind;
                bool open = marketCategory == kind;
                Add((open ? "−  " : "+  ") + entry.name, () => SelectMarketCategory(open ? (ItemKind?)null : kind, null), open && marketItemId == null);
                if (open)
                    foreach (var id in expanded)
                        Add(MarketItemName(id), () => SelectMarketCategory(kind, id), marketItemId == id, 14);
            }
            foreach (string id in unknown)
                Add(MarketDisplay(id, 24), () => SelectMarketCategory(null, id), marketItemId == id, 8);

            void Add(string title, Action onClick, bool active, float inset = 0)
            {
                MarketButton(content, title, inset, y, width - 10 - inset, 35, () =>
                {
                    if (marketCategoryView?.Dragging != true) onClick();
                }, active);
                y += row;
            }
        }

        void SelectMarketCategory(ItemKind? category, string id)
        {
            marketCategory = category;
            marketItemId = id;
            marketPage = 0;
            DrawMarket();
        }

        bool MarketMatches(string id) => marketItemId != null ? marketItemId == id
            : !marketCategory.HasValue || MarketDefinition(id)?.Kind == marketCategory;

        void DrawMarketBuy(RectTransform body, float width, IEnumerable<MarketListing> listings)
        {
            var sorted = listings.Where(l => MarketMatches(l.Item.ItemId)).OrderBy(l => l.UnitPrice).ThenBy(l => l.Id, StringComparer.Ordinal);
            if (marketItemId == null)
            {
                MarketText(body, "玩家报价  /  按型号展示最低单价", 19, 0, 0, width, 43);
                var groups = sorted.GroupBy(l => l.Item.ItemId).ToArray();
                DrawMarketCards(body, width, groups, "本类别暂时没有玩家报价。", (card, g) =>
                {
                    var low = g.First();
                    MarketCard(card, g.Key, low.Item, "最低单价 " + MarketPrice(low.UnitPrice), g.Count() + " 单报价 ›",
                        _ => SelectMarketCategory(MarketDefinition(g.Key)?.Kind, g.Key));
                });
            }
            else
            {
                string id = marketItemId;
                MarketButton(body, "‹ 最低价总览", 0, 0, 145, 39, () => SelectMarketCategory(marketCategory, null));
                MarketText(body, MarketItemName(id) + "  /  报价实例 · 单价升序", 19, 156, 0, width - 156, 39);
                DrawMarketCards(body, width, sorted.ToArray(), "该型号暂时没有玩家报价。", (card, offer) =>
                    MarketCard(card, id, offer.Item, "单价 " + MarketPrice(offer.UnitPrice),
                        "卖家 " + MarketDisplay(offer.Seller, 24) + " ›",
                        anchor => ShowMarketDetail(anchor, MarketDetail.Buy, id, offer.Item, offer)));
            }
        }

        void DrawMarketOwned(RectTransform body, MarketState state, IEnumerable<ItemStack> items, float width, bool recycle)
        {
            var matches = items.Where(i => MarketMatches(i.ItemId)).OrderBy(i => MarketItemName(i.ItemId), StringComparer.Ordinal)
                .ThenBy(i => i.InstanceId, StringComparer.Ordinal).ToArray();
            DrawMarketCards(body, width, matches, marketRecovery ? "恢复区中没有符合条件的物品。" : "市场仓库中没有符合条件的物品。", (card, item) =>
            {
                bool suggested = false;
                long price = recycle ? MarketQuote(state, item.ItemId).Resale : MarketListingPrice(state, item.ItemId, out suggested);
                string quote = recycle ? "回收单价 " + MarketPrice(price)
                    : price > 0 ? (suggested ? "建议单价 " : "市场参考 ") + MarketPrice(price) : "暂无单价参考";
                MarketCard(card, item.ItemId, item, quote, recycle ? "查看并回收 ›" : "查看并上架 ›",
                    anchor => ShowMarketDetail(anchor, recycle ? MarketDetail.Recycle : MarketDetail.List, item.ItemId, item));
            });
        }

        void DrawMarketCards<T>(RectTransform body, float width, IReadOnlyList<T> cards, string empty, Action<RectTransform, T> draw)
        {
            const int columns = 3, count = 6;
            float cellWidth = (width - 16) / columns;
            MarketPagination(body, width, cards.Count, count);
            for (int i = 0; i < count && marketPage * count + i < cards.Count; i++)
            {
                var card = At("Market card", body, (i % columns) * (cellWidth + 8), 49 + (i / columns) * 200, cellWidth, 191);
                draw(card, cards[marketPage * count + i]);
            }
            if (cards.Count == 0) MarketText(body, empty, 21, 0, 66, width, 78, TextAnchor.MiddleCenter, Muted);
        }

        void MarketCard(RectTransform card, string id, ItemStack stack, string price, string hint, Action<RectTransform> click)
        {
            float width = card.rect.width;
            MarketButton(card, "", 0, 0, width, 191, () => click(card), background: Ink, enabled: !marketBusy);
            var def = MarketDefinition(id);
            if (def != null)
            {
                DrawSpecialBackdrop(card, def);
                var color = def.Special || def.Tier > 0 ? def.QualityColor : Border;
                Fill(At("Market item quality", card, 0, 0, 3, 191), color);
                DrawItemIcon(card, def, stack, 37, 9, width - 74, 79);
                if (def.Special) MarketText(card, "特殊", 14, 10, 7, 48, 25, color: Amber);
            }
            if (stack != null) MarketText(card, (stack.Area == CargoArea.Legacy ? "恢复区 · " : "") + "×" + stack.Quantity,
                14, width - 112, 7, 102, 24, TextAnchor.MiddleRight, Aqua);
            MarketText(card, MarketItemName(id), 19, 9, 85, width - 18, 31, TextAnchor.MiddleCenter);
            MarketText(card, price, 18, 9, 117, width - 18, 31, TextAnchor.MiddleCenter, Amber);
            Fill(At("Market price band", card, 9, 151, width - 18, 32), Plate);
            MarketText(card, hint, 15, 12, 151, width - 24, 32, TextAnchor.MiddleCenter, Aqua);
        }

        void DrawMarketHistory(MarketState state)
        {
            var history = state.History.Where(h => h != null && !string.IsNullOrWhiteSpace(h.ItemId) && h.Quantity > 0 && h.UnitPrice >= 0).ToArray();
            float width = marketRoot.rect.width - 24;
            var body = At("Market history", marketRoot, 12, 109, width, 506);
            const int pageSize = 9;
            MarketPagination(body, width, history.Length, pageSize);
            Fill(At("History heading", body, 0, 0, width, 38), Ink);
            HistoryRow(body, width, 0, "物品", "数量", "单价", "合计", "状态", "UTC 时间", true);
            for (int i = 0; i < pageSize && i + marketPage * pageSize < history.Length; i++)
            {
                var h = history[i + marketPage * pageSize];
                var time = DateTimeOffset.TryParse(h.Utc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when)
                    ? when.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "—";
                var y = 42 + i * 45;
                Fill(At("History entry", body, 0, y, width, 41), i % 2 == 0 ? Ink : Plate);
                HistoryRow(body, width, y, MarketItemName(h.ItemId), h.Quantity.ToString(CultureInfo.InvariantCulture), MarketPrice(h.UnitPrice),
                    MarketTotal(h.UnitPrice, h.Quantity, out var total) ? MarketPrice(total) : "—", MarketHistoryStatus(h.Status), time, false);
            }
            if (history.Length == 0) MarketText(body, "此账号暂无交易记录。", 23, 0, 70, width, 74, TextAnchor.MiddleCenter, Muted);
        }

        void HistoryRow(RectTransform body, float width, float y, string item, string quantity, string price, string total, string status, string time, bool heading)
        {
            var color = heading ? Aqua : Pale;
            MarketText(body, item, 16, 8, y, width * .24f - 10, 40, color: color);
            Column(quantity, .24f, .08f);
            Column(price, .32f, .15f);
            Column(total, .47f, .15f);
            Column(status, .62f, .14f);
            Column(time, .76f, .24f);
            void Column(string text, float offset, float fraction) => MarketText(body, text, 16,
                width * offset, y, width * fraction - 5, 40, TextAnchor.MiddleCenter, color);
        }

        void MarketPagination(RectTransform parent, float width, int count, int pageSize)
        {
            int pages = count == 0 ? 1 : (count - 1) / pageSize + 1;
            marketPage = Mathf.Clamp(marketPage, 0, pages - 1);
            Fill(At("Market pagination", parent, 0, 456, width, 50), Ink);
            MarketText(parent, count + " 项", 15, 12, 460, 142, 42, color: Muted);
            MarketButton(parent, "上一页", width - 308, 460, 100, 42, () => Change(-1), enabled: marketPage > 0);
            MarketText(parent, (marketPage + 1) + " / " + pages, 17, width - 204, 460, 91, 42, TextAnchor.MiddleCenter);
            MarketButton(parent, "下一页", width - 108, 460, 100, 42, () => Change(1), enabled: marketPage + 1 < pages);
            void Change(int step) { marketPage += step; DrawMarket(); }
        }

        void ShowMarketDetail(RectTransform anchor, MarketDetail detail, string id, ItemStack stack, MarketListing offer = null)
        {
            var state = marketClient.State;
            if (state == null) return;
            var product = detail == MarketDetail.Shop || detail == MarketDetail.Recycle ? MarketQuote(state, id) : null;
            if ((detail == MarketDetail.Shop && product?.ForSale != true) || (detail == MarketDetail.Recycle && product == null)) return;
            var def = MarketDefinition(id);
            RectTransform popup;
            if (def != null) popup = ItemPopup(marketRoot, anchor, def, stack, null);
            else
            {
                CloseItemPopup();
                popup = itemPopup = At("Item details", marketRoot, 0, 0, 330, 216);
                Fill(popup, Ink, true);
                MarketText(popup, MarketItemName(id), 20, 12, 12, 252, 43);
                MarketButton(popup, "收起", 272, 9, 49, 35, () => { if (itemPopup == popup) BackOrSettings(); });
                MarketText(popup, "服务器物品 / 本机目录无此型号", 15, 14, 70, 302, 35, color: Muted);
            }
            float y = def != null ? 163 : 113;
            if (stack != null)
            {
                if (stack.Area == CargoArea.Legacy)
                {
                    MarketText(popup, "恢复区物品 · 可上架或回收", 15, 13, y, 304, 25, color: Aqua);
                    y += 25;
                }
                MarketText(popup, "实例 " + MarketDisplay(stack.InstanceId, 42) + " · ×" + stack.Quantity, 13, 13, y, 304, 25, color: Muted);
                y += 25;
                if (def?.Durability > 0 || stack.Wear != 0)
                {
                    string wear = def?.Durability > 0 ? "耐久 " + Math.Max(0L, (long)def.Durability - stack.Wear) + " / " + def.Durability
                        : "磨损 " + stack.Wear;
                    MarketText(popup, wear, 15, 13, y, 304, 24, color: Aqua);
                    y += 24;
                }
                if (def?.Kind == ItemKind.Weapon || !stack.Parts.Empty || !stack.Mods.IsEmpty)
                {
                    bool installed = false;
                    for (int i = 0; i <= (int)PartSlot.Magazine; i++)
                    {
                        var slot = (PartSlot)i;
                        var part = stack.Parts.Get(slot);
                        if (part.Empty) continue;
                        installed = true;
                        MarketText(popup, PartName(slot) + "  /  " + MarketItemName(part.ItemId), 15, 13, y, 304, 22, color: Aqua);
                        y += 22;
                    }
                    if (!installed || !stack.Mods.IsEmpty)
                    {
                        var mods = stack.Mods;
                        var line = !mods.IsEmpty ? "改装等级：瞄具 " + mods.Sight + " · 握把 " + mods.Grip + " · 枪托 " + mods.Stock : "未安装独立配件";
                        MarketText(popup, line, 15, 13, y, 304, 23, color: Muted);
                        y += 23;
                    }
                }
            }
            if (offer != null)
            {
                MarketText(popup, "卖家 " + MarketDisplay(offer.Seller, 32) + "  /  单价 " + MarketPrice(offer.UnitPrice), 15,
                    13, y, 304, 28, color: Amber);
                y += 29;
            }

            bool own = offer != null && string.Equals(offer.Seller, state.Account, StringComparison.OrdinalIgnoreCase);
            bool watchingOnly = marketClient.PendingAction.HasValue || (detail == MarketDetail.Buy && own);
            if (watchingOnly)
            {
                string note = marketClient.PendingAction.HasValue ? "原单尚未确认，请先重试。" : "这是你的挂单，可在「出售」中撤单。";
                MarketText(popup, note, 15, 13, y, 304, 43, color: Muted);
                y += 45;
            }
            else if (detail == MarketDetail.Cancel)
            {
                MarketButton(popup, "撤单并退回市场仓库", 13, y + 2, 304, 44, () =>
                {
                    if (itemPopup == popup) SubmitMarket(MarketAction.Cancel, offer.Id);
                }, true);
                y += 49;
            }
            else
            {
                if (detail == MarketDetail.Recycle && (!stack.Parts.Empty || stack.LoadedAmmo > 0))
                {
                    MarketText(popup, "回收整件，不退回已装配件和弹药。", 15, 13, y, 304, 26, color: Amber);
                    y += 26;
                }
                y = MarketTradeForm(popup, state, detail, id, product, stack, offer, y);
            }
            popup.sizeDelta = new Vector2(330, y + 9);
            PlaceItemPopup(popup, marketRoot, anchor);
        }

        float MarketTradeForm(RectTransform popup, MarketState state, MarketDetail detail, string id,
            MarketProduct product, ItemStack stack, MarketListing offer, float y)
        {
            bool listing = detail == MarketDetail.List;
            int maxQuantity = detail == MarketDetail.Shop ? product.MaxStack : stack.Quantity;
            long price = detail switch
            {
                MarketDetail.Buy => offer.UnitPrice,
                MarketDetail.Shop => product.Value,
                MarketDetail.Recycle => product.Resale,
                _ => MarketListingPrice(state, id, out _)
            };
            MarketText(popup, "数量 1–" + maxQuantity, 15, 13, y, 304, 25, color: Muted);
            y += 26;
            var quantity = MarketInput(popup, "Market quantity", "数量", "1", 13, y, listing ? 100 : 146, 41, 10, InputField.ContentType.IntegerNumber);
            InputField unit = null;
            if (listing)
                unit = MarketInput(popup, "Market unit price", "输入上架单价", price > 0 ? price.ToString(CultureInfo.InvariantCulture) : "",
                    121, y, 196, 41, 19, InputField.ContentType.IntegerNumber);
            else
                MarketText(popup, (detail == MarketDetail.Recycle ? "回收单价 " : "单价 ") + MarketPrice(price),
                    15, 167, y, 150, 41, color: Aqua);
            y += 44;
            var estimate = MarketText(popup, "", 16, 13, y, 304, 26, color: Amber);
            y += 28;
            var action = detail switch
            {
                MarketDetail.Buy => MarketAction.Buy, MarketDetail.Shop => MarketAction.Shop, MarketDetail.Recycle => MarketAction.Recycle, _ => MarketAction.List
            };
            string target = detail switch { MarketDetail.Buy => offer.Id, MarketDetail.Shop => id, _ => stack.InstanceId };
            var button = MarketButton(popup, "确认" + MarketActionName(action), 13, y, 304, 44, () =>
            {
                if (itemPopup == popup && ReadOrder(out var n, out var p, out _)) SubmitMarket(action, target, n, p);
            }, true);
            quantity.onValueChanged.AddListener(_ => UpdateEstimate());
            if (unit != null) unit.onValueChanged.AddListener(_ => UpdateEstimate());
            UpdateEstimate();
            return y + 49;

            bool ReadOrder(out int n, out long p, out long total)
            {
                p = price;
                total = 0;
                return int.TryParse(quantity.text, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n > 0 && n <= maxQuantity
                    && (!listing || (long.TryParse(unit.text, NumberStyles.None, CultureInfo.InvariantCulture, out p) && p > 0))
                    && MarketTotal(p, n, out total);
            }

            void UpdateEstimate()
            {
                bool valid = ReadOrder(out _, out _, out var total);
                bool lowCredits = (detail == MarketDetail.Buy || detail == MarketDetail.Shop) && valid && total > state.Credits;
                button.interactable = valid && !lowCredits && !marketBusy;
                estimate.text = !valid ? "请输入有效数量与单价（总价不能溢出）。" : lowCredits ? "余额不足 · 合计 " + MarketPrice(total)
                    : "合计 " + MarketPrice(total) + " 信用点";
            }
        }

        void SubmitMarket(MarketAction action, string id, int quantity = 0, long unitPrice = -1)
        {
            if (!MarketVisible || marketBusy || marketClient.PendingAction.HasValue) return;
            RunMarket(() => marketClient.Trade(action, id, quantity, unitPrice), MarketActionName(action) + "已确认。");
        }

        async void RunMarket(Func<Task<MarketResult>> send, string success)
        {
            if (marketBusy) return;
            marketBusy = true;
            try
            {
                DrawMarket();
                var task = send();
                send = null;
                var result = await task;
                marketStatus = result.Ok ? success : MarketErrorText(result.Error);
            }
            catch (Exception)
            {
                marketStatus = "请求未完成，请检查连接或重试。";
            }
            finally
            {
                marketBusy = false;
                if (MarketVisible) DrawMarket();
            }
        }

        Button MarketButton(Transform parent, string title, float x, float y, float width, float height, Action click,
            bool primary = false, bool enabled = true, Color? background = null)
        {
            int render = marketRender;
            var button = Button(parent, title, x, y, width, height, () =>
            {
                if (MarketVisible && render == marketRender && !marketBusy && !ProcessInventoryBackKey()) click();
            }, primary, background);
            button.interactable = enabled && !marketBusy;
            return button;
        }

        InputField MarketInput(Transform parent, string name, string placeholder, string value, float x, float y, float w, float h,
            int limit, InputField.ContentType type = InputField.ContentType.Standard)
        {
            var box = At(name, parent, x, y, w, h);
            var image = Fill(box, Plate, true);
            var actual = Label(box, "", 18, 10, 0, w - 20, h, TextAnchor.MiddleLeft);
            var hint = Label(box, placeholder, 15, 10, 0, w - 20, h, TextAnchor.MiddleLeft, Muted);
            var field = box.gameObject.AddComponent<InputField>();
            field.targetGraphic = image;
            field.textComponent = actual;
            field.placeholder = hint;
            field.contentType = type;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = limit;
            field.shouldHideMobileInput = type == InputField.ContentType.Password;
            if (name == "Market URL") field.keyboardType = TouchScreenKeyboardType.URL;
            field.SetTextWithoutNotify(value);
            field.interactable = !marketBusy;
            return field;
        }

        Text MarketText(Transform parent, string text, int fontSize, float x, float y, float w, float h, TextAnchor align = TextAnchor.MiddleLeft, Color? color = null)
        {
            var label = Label(parent, text, fontSize, x, y, w, h, align, color);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Min(12, fontSize);
            label.resizeTextMaxSize = fontSize;
            return label;
        }

        ItemDefinition MarketDefinition(string id) => session.Catalog.Find(id);
        string MarketItemName(string id) => MarketDefinition(id)?.DisplayName ?? MarketDisplay(id, 36);
        static bool MarketStack(ItemStack stack) => stack != null && stack.Quantity > 0
            && !string.IsNullOrWhiteSpace(stack.ItemId) && !string.IsNullOrWhiteSpace(stack.InstanceId);
        static MarketProduct MarketQuote(MarketState state, string id) => Array.Find(state.Products, product => product.Id == id);
        long MarketListingPrice(MarketState state, string id, out bool suggested)
        {
            long price = state.References.Where(r => r != null && r.ItemId == id && r.UnitPrice > 0)
                .Select(r => r.UnitPrice).DefaultIfEmpty().Min();
            var item = price == 0 ? MarketDefinition(id) : null;
            suggested = item != null && item.Kind == ItemKind.Part && item.Part.Slot == PartSlot.Sight && item.Part.Magnification >= 6;
            return suggested ? item.Value : price;
        }
        static string MarketPrice(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
        static bool MarketTotal(long price, int quantity, out long total)
        {
            total = 0;
            if (price < 0 || quantity <= 0 || price > long.MaxValue / quantity) return false;
            total = price * quantity;
            return true;
        }

        static string MarketDisplay(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "—";
            string visible = new string(value.Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format).Take(max + 1).ToArray());
            return visible.Length > max ? visible.Substring(0, max) + "…" : visible;
        }

        static string MarketActionName(MarketAction action) => action switch
        {
            MarketAction.List => "上架", MarketAction.Buy => "购买", MarketAction.Cancel => "撤单",
            MarketAction.Shop => "系统购入", MarketAction.Recycle => "系统回收", _ => "交易"
        };

        static string MarketHistoryStatus(string status) => status switch
        {
            "Listed" => "已上架", "Bought" => "已买入", "Sold" => "已售出", "Cancelled" => "已撤单",
            "Shop" => "系统购入", "Recycled" => "系统回收", _ => MarketDisplay(status, 18)
        };

        static string MarketErrorText(string error) => error switch
        {
            "Timeout" => "服务器未在时限内应答。",
            "ConnectionFailed" => "连接失败，请检查服务地址与网络。",
            "Redirect" => "服务地址发生跳转，请使用正确的直连地址。",
            "Busy" => "请求尚在进行。",
            "SignInRequired" => "请重新登录服务器账户。",
            "CredentialsRequired" or "InvalidCredentials" => "请检查市场账号与密码。",
            "InvalidAccount" => "账号须为 3–32 位字母、数字、下划线或连字符。",
            "InvalidPassword" => "密码长度须为 8–128 个字符。",
            "AccountExists" => "账号已存在，请登录。",
            "AccountLocked" => "请登录原账号以确认未完成的请求。",
            "TooManyRequests" => "请求过于频繁，请稍后重试。",
            "RetryRequired" => "上一笔请求尚未确认，请重试原单。",
            "RefreshRequired" => "账户数据未同步，请刷新。",
            "InsufficientCredits" => "服务器账户信用点不足，请刷新。",
            "PriceChanged" => "服务器报价已变动，未成交。请刷新后重新确认。",
            "InsufficientQuantity" or "ItemNotOwned" or "ListingNotFound" => "库存或报价已变动，请刷新。",
            "ListingNotOwned" or "OwnListing" => "不能购买自己的报价或撤销他人的挂单。",
            "NotForSale" or "UnknownItem" => "此物品不在服务器商店出售。",
            "StashFull" => "服务器仓库空间不足。",
            "InvalidOrder" or "InvalidQuantity" or "InvalidUnitPrice" or "PriceOverflow" or "CreditOverflow" => "数量或金额超出有效范围。",
            "ServiceUnavailable" or "StateUnavailable" => "服务器暂不可用，请稍后重试。",
            "InvalidReply" => "服务器响应无法确认。",
            _ => "服务器未确认该操作，请刷新或按提示重试。"
        };
    }
}
