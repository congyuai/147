using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dustline.Market;

internal static class MarketJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 24
    };
}

internal sealed record MarketRequest
{
    public string? Account { get; init; }
    public string? Password { get; init; }
    public string? RequestId { get; init; }
    public string? ItemId { get; init; }
    public string? InstanceId { get; init; }
    public string? ListingId { get; init; }
    public int Quantity { get; init; }
    public long? UnitPrice { get; init; }
}

internal sealed record ItemStack
{
    public required string InstanceId { get; init; }
    public required string ItemId { get; init; }
    public required int Quantity { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public bool Rotated { get; init; }
    public int Area { get; init; }
    public int Slot { get; init; }
    public int LoadedAmmo { get; init; } = -1;
    public WeaponMods Mods { get; init; } = new();
    public WeaponParts Parts { get; init; } = new();
    public string LoadedAmmoId { get; init; } = "";
    public int UsesSpent { get; init; }
    public int Wear { get; init; }

    public bool IsFungible() => LoadedAmmo == -1 && LoadedAmmoId == "" && UsesSpent == 0 && Wear == 0
        && Mods == new WeaponMods() && Parts == WeaponParts.Empty;
}

internal sealed record WeaponMods(int Sight = 0, int Grip = 0, int Stock = 0,
    int OwnedSight = 0, int OwnedGrip = 0, int OwnedStock = 0)
{
    public bool IsValid() => Valid(Sight, OwnedSight) && Valid(Grip, OwnedGrip) && Valid(Stock, OwnedStock);
    private static bool Valid(int selected, int owned) => selected >= 0 && selected <= owned && owned <= 2;
}

internal sealed record PartLink(string ItemId = "", string InstanceId = "");

internal sealed record WeaponParts
{
    public static readonly WeaponParts Empty = new();
    public PartLink Sight { get; init; } = new();
    public PartLink Grip { get; init; } = new();
    public PartLink Stock { get; init; } = new();
    public PartLink Muzzle { get; init; } = new();
    public PartLink Magazine { get; init; } = new();

    public IEnumerable<PartLink> Links()
    {
        yield return Sight;
        yield return Grip;
        yield return Stock;
        yield return Muzzle;
        yield return Magazine;
    }
}

internal sealed record Listing(string Id, string Seller, long UnitPrice, ItemStack Item);
internal sealed record HistoryEntry(string Id, string ItemId, int Quantity, long UnitPrice, string Status, DateTime Utc);
internal sealed record ReferencePrice(string ItemId, long UnitPrice);
internal sealed record ProductQuote(string Id, bool ForSale, int MaxStack, long Value, long Resale);

internal sealed record StateResponse(bool Ok, string Error, string Account, long Credits,
    ItemStack[] Items, Listing[] Listings, HistoryEntry[] History, ReferencePrice[] References, ProductQuote[] Products,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Token);

internal sealed record ErrorResponse(bool Ok, string Error);

internal sealed class MarketError(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public static MarketError Invalid(string code) => new(400, code);
    public static MarketError Missing(string code) => new(404, code);
    public static MarketError Conflict(string code) => new(409, code);
}
