using System.Text.Json;

namespace Dustline.Market;

internal sealed record ItemDefinition
{
    public required string Id { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int MaxStack { get; init; }
    public required long Value { get; init; }
    public required long Resale { get; init; }
    public required bool ForSale { get; init; }
    public required int StarterQuantity { get; init; }

    public (int Width, int Height) Size(bool rotated) => rotated ? (Height, Width) : (Width, Height);
}

internal sealed record CatalogFile
{
    public required ItemDefinition[] Items { get; init; }
    public StarterBuild[] StarterBuilds { get; init; } = [];
}

internal sealed record StarterBuild
{
    public required string ItemId { get; init; }
    public int LoadedAmmo { get; init; } = -1;
    public string LoadedAmmoId { get; init; } = "";
    public int UsesSpent { get; init; }
    public int Wear { get; init; }
    public WeaponMods Mods { get; init; } = new();
    public StarterParts Parts { get; init; } = new();

    public ItemStack Create() => Stash.NewItem(ItemId, 1) with
    {
        LoadedAmmo = LoadedAmmo, LoadedAmmoId = LoadedAmmoId, UsesSpent = UsesSpent,
        Wear = Wear, Mods = Mods, Parts = Parts.Create()
    };
}

internal sealed record StarterParts(string? Sight = null, string? Grip = null, string? Stock = null,
    string? Muzzle = null, string? Magazine = null)
{
    public WeaponParts Create() => new()
    {
        Sight = Link(Sight), Grip = Link(Grip), Stock = Link(Stock), Muzzle = Link(Muzzle), Magazine = Link(Magazine)
    };

    private static PartLink Link(string? id) => string.IsNullOrEmpty(id) ? new() : new(id, Stash.NewId());
}

internal sealed class Catalog
{
    public const string Cake = "birthday_cake";
    public const long CakeResale = 52_013_140L;
    public const long StartingCredits = 1_000_000L;

    private readonly Dictionary<string, ItemDefinition> items;
    private readonly StarterBuild[] builds;

    private Catalog(Dictionary<string, ItemDefinition> items, StarterBuild[] builds)
    {
        this.items = items;
        this.builds = builds;
        Products = items.Values.OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => new ProductQuote(item.Id, item.ForSale, item.MaxStack, item.Value, item.Resale)).ToArray();
    }

    public ProductQuote[] Products { get; }
    public ItemDefinition this[string id] => items[id];
    public ItemDefinition? Find(string id) => items.GetValueOrDefault(id);

    public static Catalog Load(string path)
    {
        using var input = File.OpenRead(path);
        var config = JsonSerializer.Deserialize<CatalogFile>(input, MarketJson.Options);
        if (config?.Items == null || config.StarterBuilds == null) throw new InvalidDataException("Invalid catalog.");
        var items = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        foreach (var item in config.Items)
        {
            if (item == null || !Identifiers.Valid(item.Id) || !Fits(item.Width, item.Height) ||
                item.MaxStack < 1 || item.Value < 0 || item.Resale < 0 || item.StarterQuantity < 0 || !items.TryAdd(item.Id, item))
                throw new InvalidDataException("Invalid or duplicate catalog item.");
        }
        if (!items.TryGetValue(Cake, out var cake)) throw new InvalidDataException("Catalog must contain birthday_cake.");
        items[Cake] = cake with { ForSale = false, Resale = CakeResale, StarterQuantity = 1, MaxStack = 1 };

        var catalog = new Catalog(items, config.StarterBuilds);
        foreach (var build in catalog.builds)
        {
            if (build == null || !Identifiers.Valid(build.ItemId) || build.ItemId == Cake ||
                catalog.Find(build.ItemId)?.MaxStack != 1 || build.Mods == null || !build.Mods.IsValid() || build.Parts == null ||
                !catalog.ValidCondition(build.Create()))
                throw new InvalidDataException("Invalid starter build.");
        }
        try { _ = catalog.CreateStarterItems(); }
        catch (MarketError ex) { throw new InvalidDataException("Starter items do not fit in a 12x30 stash.", ex); }
        return catalog;
    }

    public List<ItemStack> CreateStarterItems()
    {
        List<ItemStack> result = [];
        var stash = new Stash(this, result);
        foreach (var definition in items.Values) stash.AddStock(definition, definition.StarterQuantity);
        foreach (var build in builds) stash.Insert(build.Create());
        return result;
    }

    public bool ValidCondition(ItemStack item)
    {
        if (item.Wear < 0 || item.UsesSpent < 0 || item.LoadedAmmo < -1 || item.Mods == null || !item.Mods.IsValid() ||
            item.Parts == null || item.LoadedAmmoId == null) return false;
        if (item.LoadedAmmoId.Length > 0 && (item.LoadedAmmo < 0 || item.LoadedAmmoId == Cake || Find(item.LoadedAmmoId) == null))
            return false;
        return item.Parts.Links().All(link => link != null && (link == new PartLink() ||
            Identifiers.Valid(link.ItemId) && Identifiers.Valid(link.InstanceId) && link.ItemId != Cake &&
            link.ItemId != item.ItemId && Find(link.ItemId)?.MaxStack == 1));
    }

    private static bool Fits(int width, int height) => width > 0 && height > 0 &&
        (width <= Stash.Columns && height <= Stash.Rows || height <= Stash.Columns && width <= Stash.Rows);
}

internal static class Identifiers
{
    public static bool Valid(string? value) => value is { Length: > 0 and <= 128 } &&
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
}
