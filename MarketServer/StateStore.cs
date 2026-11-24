using System.Text.Json;

namespace Dustline.Market;

internal enum Mutation { List, Buy, Cancel, Shop, Recycle }
internal sealed record MutationReceipt(Mutation Operation, string TargetId, int Quantity, long UnitPrice);

internal sealed record AccountState
{
    public required PasswordDigest Password { get; init; }
    public required long Credits { get; set; }
    public required List<ItemStack> Items { get; init; }
    public required List<HistoryEntry> History { get; init; }
    public required Dictionary<string, MutationReceipt> Receipts { get; init; }

    public AccountState Draft() => this with { Items = [.. Items], History = [.. History], Receipts = new(Receipts) };
}

internal sealed record PersistedState
{
    public const int CurrentVersion = 2;
    public required int Version { get; init; }
    public required Dictionary<string, AccountState> Accounts { get; init; }
    public required Dictionary<string, Listing> Listings { get; init; }
    public HashSet<string> RecoveryListingIds { get; init; } = new(StringComparer.Ordinal);

    public PersistedState Draft() => this with
    {
        Accounts = Accounts.ToDictionary(pair => pair.Key, pair => pair.Value.Draft(), StringComparer.Ordinal),
        Listings = new(Listings, StringComparer.Ordinal),
        RecoveryListingIds = new(RecoveryListingIds, StringComparer.Ordinal)
    };

    public static PersistedState Empty() => new() { Version = CurrentVersion, Accounts = [], Listings = [] };
}

internal sealed class StateStore : IDisposable
{
    private readonly object gate = new();
    private readonly string path;
    private readonly FileStream fileLock;
    private PersistedState current;

    public StateStore(string path, Catalog catalog)
    {
        this.path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { fileLock = OpenPrivate(path + ".lock", FileMode.OpenOrCreate); }
        catch (IOException ex) { throw new IOException("The market state lock is unavailable.", ex); }
        try
        {
            var loaded = Load();
            Validate(loaded, catalog);
            if (loaded.Version == 1)
            {
                loaded = loaded with
                {
                    Version = PersistedState.CurrentVersion,
                    Accounts = loaded.Accounts.ToDictionary(pair => pair.Key, pair => pair.Value with
                    {
                        Items = Stash.MigrateCakes(catalog, pair.Value.Items)
                    }, StringComparer.Ordinal),
                    RecoveryListingIds = loaded.Listings.Values.Where(listing => listing.Item.ItemId == Catalog.Cake)
                        .Select(listing => listing.Id).ToHashSet(StringComparer.Ordinal)
                };
                Validate(loaded, catalog);
                Save(loaded);
            }
            current = loaded;
        }
        catch
        {
            fileLock.Dispose();
            throw;
        }
    }

    public T Read<T>(Func<PersistedState, T> read)
    {
        lock (gate) return read(current);
    }

    public T Write<T>(Func<PersistedState, (PersistedState? Next, T Result)> write)
    {
        lock (gate)
        {
            var (next, result) = write(current);
            if (next != null)
            {
                Save(next);
                current = next;
            }
            return result;
        }
    }

    private PersistedState Load()
    {
        try
        {
            using var input = File.OpenRead(path);
            return JsonSerializer.Deserialize<PersistedState>(input, MarketJson.Options)
                ?? throw new InvalidDataException("Market state is empty.");
        }
        catch (FileNotFoundException) { return PersistedState.Empty(); }
    }

    private void Save(PersistedState next)
    {
        // A sibling file keeps the flushed write and its final rename on the same volume.
        string staged = path + "." + Stash.NewId() + ".tmp";
        bool created = false;
        try
        {
            using (var output = OpenPrivate(staged, FileMode.CreateNew))
            {
                created = true;
                JsonSerializer.Serialize(output, next, MarketJson.Options);
                output.Flush(flushToDisk: true);
            }
            File.Move(staged, path, overwrite: true);
        }
        catch
        {
            if (created)
            {
                try { File.Delete(staged); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
    }

    private static FileStream OpenPrivate(string path, FileMode mode)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    private static void Validate(PersistedState state, Catalog catalog)
    {
        bool oldCakeLayout = state.Version == 1;
        if ((!oldCakeLayout && state.Version != PersistedState.CurrentVersion) || state.Accounts == null || state.Listings == null ||
            state.RecoveryListingIds == null || oldCakeLayout && state.RecoveryListingIds.Count != 0)
            throw new InvalidDataException("Invalid market state version or structure.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, account) in state.Accounts)
        {
            if (!ValidAccount(name) || account?.Password == null || !account.Password.IsValid() || account.Credits < 0 ||
                account.Items == null || account.History == null || account.Receipts == null ||
                account.History.Any(entry => entry == null) || account.Receipts.Any(pair => pair.Value == null))
                throw new InvalidDataException("Invalid persisted account.");
            foreach (var item in account.Items) ValidateItem(item, allowRecovery: !oldCakeLayout);
            _ = new Stash(catalog, account.Items, oldCakeLayout);
        }
        foreach (var (id, listing) in state.Listings)
        {
            if (listing == null || !Identifiers.Valid(id) || id != listing.Id || listing.Seller == null ||
                !state.Accounts.ContainsKey(listing.Seller) || listing.UnitPrice <= 0)
                throw new InvalidDataException("Invalid persisted listing.");
            ValidateItem(listing.Item, allowRecovery: false);
            if (listing.UnitPrice > long.MaxValue / listing.Item.Quantity)
                throw new InvalidDataException("Invalid persisted listing total.");
        }

        if (state.RecoveryListingIds.Any(id => !Identifiers.Valid(id) || state.Listings.GetValueOrDefault(id)?.Item.ItemId != Catalog.Cake))
            throw new InvalidDataException("Invalid recovery escrow.");

        void ValidateItem(ItemStack item, bool allowRecovery)
        {
            if (item == null || !Identifiers.Valid(item.ItemId) || !Identifiers.Valid(item.InstanceId) ||
                catalog.Find(item.ItemId) is not { } definition || item.Quantity < 1 || item.Quantity > definition.MaxStack ||
                (item.Area != ItemStack.MainArea && !(allowRecovery && item.Area == ItemStack.RecoveryArea && item.ItemId == Catalog.Cake)) ||
                item.Slot != 0 || !catalog.ValidCondition(item) ||
                ((definition.MaxStack > 1 || item.ItemId == Catalog.Cake) && !item.IsFungible()) || !ids.Add(item.InstanceId))
                throw new InvalidDataException("Invalid persisted item.");
            foreach (var link in item.Parts.Links())
                if (link.InstanceId.Length != 0 && !ids.Add(link.InstanceId)) throw new InvalidDataException("Duplicate part instance.");
        }
    }

    private static bool ValidAccount(string name)
    {
        try { return name == Credentials.Account(name); }
        catch (MarketError) { return false; }
    }

    public void Dispose() => fileLock.Dispose();
}
