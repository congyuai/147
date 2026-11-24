namespace Dustline.Market;

internal sealed class Stash
{
    public const int Columns = 12;
    public const int Rows = 30;

    private readonly Catalog catalog;
    private readonly List<ItemStack> items;
    private readonly bool[,] occupied = new bool[Columns, Rows];

    public Stash(Catalog catalog, List<ItemStack> items)
    {
        this.catalog = catalog;
        this.items = items;
        foreach (var item in items)
        {
            var (width, height) = catalog[item.ItemId].Size(item.Rotated);
            if (!Fits(item.X, item.Y, width, height)) throw new InvalidDataException("Invalid stash layout.");
            Occupy(item.X, item.Y, width, height);
        }
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

    public void Insert(ItemStack item)
    {
        var definition = catalog[item.ItemId];
        int quantity = item.Quantity;
        if (definition.MaxStack > 1 && item.IsFungible()) quantity = FillExisting(definition, quantity);
        if (quantity > 0) Place(item with { Quantity = quantity }, definition);
    }

    private int FillExisting(ItemDefinition definition, int quantity)
    {
        for (int i = 0; i < items.Count && quantity > 0; i++)
        {
            var item = items[i];
            if (item.ItemId != definition.Id || item.Quantity == definition.MaxStack || !item.IsFungible()) continue;
            int added = Math.Min(quantity, definition.MaxStack - item.Quantity);
            items[i] = item with { Quantity = item.Quantity + added };
            quantity -= added;
        }
        return quantity;
    }

    private void Place(ItemStack item, ItemDefinition definition)
    {
        var (width, height) = definition.Size(item.Rotated);
        if (Fits(item.X, item.Y, width, height))
        {
            Occupy(item.X, item.Y, width, height);
            items.Add(item);
            return;
        }
        if (TryPlace(item, item.Rotated, width, height)) return;
        if (width != height && TryPlace(item, !item.Rotated, height, width)) return;
        throw MarketError.Conflict("StashFull");
    }

    private bool TryPlace(ItemStack item, bool rotated, int width, int height)
    {
        for (int y = 0; y <= Rows - height; y++)
        for (int x = 0; x <= Columns - width; x++)
        {
            if (!Fits(x, y, width, height)) continue;
            Occupy(x, y, width, height);
            items.Add(item with { X = x, Y = y, Rotated = rotated });
            return true;
        }
        return false;
    }

    private bool Fits(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || x > Columns - width || y > Rows - height) return false;
        for (int row = y; row < y + height; row++)
        for (int column = x; column < x + width; column++)
            if (occupied[column, row]) return false;
        return true;
    }

    private void Occupy(int x, int y, int width, int height)
    {
        for (int row = y; row < y + height; row++)
        for (int column = x; column < x + width; column++)
            occupied[column, row] = true;
    }
}
