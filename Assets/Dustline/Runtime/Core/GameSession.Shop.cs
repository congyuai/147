namespace Dustline
{
    public sealed partial class GameSession
    {
        internal int ShopPurchaseLimit(ItemDefinition item, GridInventory destination, out string error)
        {
            error = null;
            if (item.NotForSale || item.Value < 0)
            {
                error = "商品不可购买。";
                return 0;
            }
            int affordable = item.Value == 0 ? int.MaxValue : Profile.Credits / item.Value;
            if (affordable <= 0)
            {
                error = "信用点不足。";
                return 0;
            }
            int limit = destination.MaxAddable(item.Id, affordable);
            if (limit == 0) error = "目的物品栏空间或承重不足。";
            return limit;
        }

        internal bool TryBuyShopItem(string itemId, int quantity, int quote, out string error)
        {
            error = "当前无法购买商品。";
            if (Phase != SessionPhase.Menu || quantity < 1) return false;
            var item = Catalog.Find(itemId);
            if (item == null || item.NotForSale || item.Value < 0 || item.Value != quote)
            {
                error = "商品或价格已变化。";
                return false;
            }
            long cost = (long)item.Value * quantity;
            if (cost > Profile.Credits)
            {
                error = "信用点不足。";
                return false;
            }
            var draft = Stash.Draft();
            if (!draft.TryAdd(itemId, quantity, out error)) return false;
            if (SaveMenuInventory(draft, Loadout, Profile.Credits - (int)cost)) return true;
            error = "未能保存购买结果。";
            return false;
        }
    }
}
