namespace Dustline.Market;

internal sealed class Stash
{
    public const int Columns = 12;
    public const int Rows = 30;

    private readonly Catalog catalog;
    private readonly List<ItemStack> items;
    private readonly bool[,] occupied = new bool[Columns, Rows];

    public Stash(Catalog catalog, List<ItemStack> items, bool oldCakeLayout = false)
    {
        this.catalog = catalog;
        this.items = items;
        foreach (var item in items)
        {
            if (!oldCakeLayout && item.Area == ItemStack.RecoveryArea && item.ItemId == Catalog.Cake && item.X == 0 && item.Y == 0)
                continue;
            var (width, height) = oldCakeLayout && item.ItemId == Catalog.Cake ? (2, 2) : catalog[item.ItemId].Size(item.Rotated);
            if (item.Area != ItemStack.MainArea || !Reserve(item.X, item.Y, width, height))
                throw new InvalidDataException("Invalid stash layout.");
        }
    }

    public static List<ItemStack> MigrateCakes(Catalog catalog, List<ItemStack> items)
    {
        var layout = new Stash(catalog, items.FindAll(item => item.ItemId != Catalog.Cake));
        var result = new List<ItemStack>(items);
        var displaced = new List<int>();
        var cake = catalog[Catalog.Cake];
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.ItemId != Catalog.Cake) continue;
            var (width, height) = cake.Size(item.Rotated);
            if (!layout.Reserve(item.X, item.Y, width, height)) displaced.Add(i);
        }
        foreach (int i in displaced)
            result[i] = layout.FindPlacement(items[i], cake, keepRotation: true) ?? InRecovery(items[i]);
        return result;
    }

    public static ItemStack NewItem(string itemId, int quantity) => new()
    {
        ItemId = itemId, InstanceId = NewId(), Quantity = quantity
    };

    public static string NewId() => Guid.NewGuid().ToString("N");

    public static ItemStack Extract(ItemStack item, int quantity) => quantity == item.Quantity ? item
        : item with { Quantity = quantity, InstanceId = NewId() };

    public static ItemStack Take(List<ItemStack> items, string instanceId, int quantity)
    {
        int index = items.FindIndex(item => item.InstanceId == instanceId);
        if (index < 0) throw MarketError.Missing("ItemNotOwned");
        var item = items[index];
        if (quantity > item.Quantity) throw MarketError.Conflict("InsufficientQuantity");
        if (quantity == item.Quantity) items.RemoveAt(index);
        else items[index] = item with { Quantity = item.Quantity - quantity };
        return Extract(item, quantity);
    }

    public void AddStock(ItemDefinition definition, int quantity)
    {
        if (definition.MaxStack > 1) quantity = FillExisting(definition, quantity);
        while (quantity > 0)
        {
            int count = Math.Min(quantity, definition.MaxStack);
            Place(NewItem(definition.Id, count), definition);
            quantity -= count;
        }
    }

    public void Insert(ItemStack item, bool returnToRecovery = false)
    {
        var definition = catalog[item.ItemId];
        if (returnToRecovery && item.ItemId == Catalog.Cake)
        {
            items.Add(FindPlacement(item, definition, keepRotation: true) ?? InRecovery(item));
            return;
        }
        int quantity = item.Quantity;
        if (definition.MaxStack > 1 && item.IsFungible()) quantity = FillExisting(definition, quantity);
        if (quantity > 0) Place(item with { Quantity = quantity }, definition);
    }

    private static ItemStack InRecovery(ItemStack item) => item with { Area = ItemStack.RecoveryArea, X = 0, Y = 0 };

    private int FillExisting(ItemDefinition definition, int quantity)
    {
        for (int i = 0; i < items.Count && quantity > 0; i++)
        {
            var item = items[i];
            if (item.Area != ItemStack.MainArea || item.ItemId != definition.Id || item.Quantity == definition.MaxStack || !item.IsFungible()) continue;
            int added = Math.Min(quantity, definition.MaxStack - item.Quantity);
            items[i] = item with { Quantity = item.Quantity + added };
            quantity -= added;
        }
        return quantity;
    }

    private void Place(ItemStack item, ItemDefinition definition)
        => items.Add(FindPlacement(item, definition) ?? throw MarketError.Conflict("StashFull"));

    private ItemStack? FindPlacement(ItemStack item, ItemDefinition definition, bool keepRotation = false)
    {
        if (item.Area != ItemStack.MainArea) item = item with { Area = ItemStack.MainArea };
        var (width, height) = definition.Size(item.Rotated);
        if (Reserve(item.X, item.Y, width, height)) return item;
        return FindPosition(item, item.Rotated, width, height)
            ?? (!keepRotation && width != height ? FindPosition(item, !item.Rotated, height, width) : null);
    }

    private ItemStack? FindPosition(ItemStack item, bool rotated, int width, int height)
    {
        for (int y = 0; y <= Rows - height; y++)
        for (int x = 0; x <= Columns - width; x++)
            if (Reserve(x, y, width, height)) return item with { X = x, Y = y, Rotated = rotated };
        return null;
    }

    private bool Reserve(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || x > Columns - width || y > Rows - height) return false;
        for (int row = y; row < y + height; row++)
        for (int column = x; column < x + width; column++)
            if (occupied[column, row]) return false;
        for (int row = y; row < y + height; row++)
        for (int column = x; column < x + width; column++)
            occupied[column, row] = true;
        return true;
    }
}
