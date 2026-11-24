namespace Dustline.Market;

internal sealed class Market(Catalog catalog, StateStore store)
{
    private static readonly PasswordDigest UnknownAccount = PasswordDigest.Create(Stash.NewId());
    private readonly Sessions sessions = new();

    public string Authenticate(HttpContext context) => sessions.Authenticate(context.Request.Headers.Authorization);

    public StateResponse Register(MarketRequest request)
    {
        var name = Credentials.Account(request.Account);
        var password = Credentials.Password(request.Password);
        if (store.Read(state => state.Accounts.ContainsKey(name))) throw MarketError.Conflict("AccountExists");
        var account = new AccountState
        {
            Password = PasswordDigest.Create(password), Credits = Catalog.StartingCredits,
            Items = catalog.CreateStarterItems(), History = [], Receipts = []
        };
        return store.Write(state =>
        {
            if (state.Accounts.ContainsKey(name)) throw MarketError.Conflict("AccountExists");
            var next = state.Draft();
            next.Accounts.Add(name, account);
            return (next, Snapshot(next, name));
        });
    }

    public StateResponse Login(MarketRequest request)
    {
        var name = Credentials.Account(request.Account);
        var password = Credentials.Password(request.Password);
        var digest = store.Read(state => state.Accounts.GetValueOrDefault(name)?.Password);
        if (!(digest ?? UnknownAccount).Verify(password) || digest == null)
            throw new MarketError(StatusCodes.Status401Unauthorized, "InvalidCredentials");
        string token = sessions.Issue(name);
        return store.Read(state => Snapshot(state, name, token));
    }

    public StateResponse View(string account) => store.Read(state => Snapshot(state, account));

    public StateResponse Apply(string account, Mutation operation, MarketRequest request)
    {
        var requestId = RequireId(request.RequestId, "InvalidRequestId");
        var receipt = Command(operation, request);
        return store.Write(state =>
        {
            // Committed requests still replay if the catalog changes across a restart.
            if (state.Accounts[account].Receipts.TryGetValue(requestId, out var previous))
            {
                if (previous != receipt) throw MarketError.Conflict("RequestIdConflict");
                return (null, Snapshot(state, account));
            }
            var next = state.Draft();
            var actor = next.Accounts[account];
            switch (operation)
            {
                case Mutation.List: List(next, account, actor, receipt); break;
                case Mutation.Buy: Buy(next, account, actor, receipt); break;
                case Mutation.Cancel: Cancel(next, account, actor, receipt); break;
                case Mutation.Shop: Shop(actor, receipt); break;
                case Mutation.Recycle: Recycle(actor, receipt); break;
            }
            actor.Receipts.Add(requestId, receipt);
            return (next, Snapshot(next, account));
        });
    }

    private static MutationReceipt Command(Mutation operation, MarketRequest request)
    {
        string target = operation switch
        {
            Mutation.List or Mutation.Recycle => RequireId(request.InstanceId, "InvalidInstanceId"),
            Mutation.Buy or Mutation.Cancel => RequireId(request.ListingId, "InvalidListingId"),
            Mutation.Shop => RequireId(request.ItemId, "InvalidItemId"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        int quantity = operation == Mutation.Cancel ? 0 : request.Quantity;
        if (operation != Mutation.Cancel && quantity <= 0) throw MarketError.Invalid("InvalidQuantity");
        long price = 0;
        if (operation is Mutation.List or Mutation.Shop or Mutation.Recycle)
        {
            if (request.UnitPrice is not long unitPrice || unitPrice < 0 || (operation == Mutation.List && unitPrice == 0))
                throw MarketError.Invalid("InvalidUnitPrice");
            price = unitPrice;
        }
        return new(operation, target, quantity, price);
    }

    private static string RequireId(string? id, string error) => Identifiers.Valid(id) ? id! : throw MarketError.Invalid(error);

    private static void List(PersistedState state, string name, AccountState seller, MutationReceipt command)
    {
        _ = Total(command.UnitPrice, command.Quantity);
        var item = Stash.Take(seller.Items, command.TargetId, command.Quantity);
        string id = Stash.NewId();
        state.Listings.Add(id, new(id, name, command.UnitPrice, item));
        Record(seller, item.ItemId, item.Quantity, command.UnitPrice, "Listed", DateTime.UtcNow);
    }

    private void Buy(PersistedState state, string name, AccountState buyer, MutationReceipt command)
    {
        var listing = FindListing(state, command.TargetId);
        if (listing.Seller == name) throw MarketError.Conflict("OwnListing");
        if (command.Quantity > listing.Item.Quantity) throw MarketError.Conflict("InsufficientQuantity");
        long price = Total(listing.UnitPrice, command.Quantity);
        Debit(buyer, price);
        var seller = state.Accounts[listing.Seller];
        Credit(seller, price);
        new Stash(catalog, buyer.Items).Insert(Stash.Extract(listing.Item, command.Quantity));
        if (command.Quantity == listing.Item.Quantity) state.Listings.Remove(listing.Id);
        else state.Listings[listing.Id] = listing with { Item = listing.Item with { Quantity = listing.Item.Quantity - command.Quantity } };
        var now = DateTime.UtcNow;
        Record(buyer, listing.Item.ItemId, command.Quantity, listing.UnitPrice, "Bought", now);
        Record(seller, listing.Item.ItemId, command.Quantity, listing.UnitPrice, "Sold", now);
    }

    private void Cancel(PersistedState state, string name, AccountState seller, MutationReceipt command)
    {
        var listing = FindListing(state, command.TargetId);
        if (listing.Seller != name) throw new MarketError(StatusCodes.Status403Forbidden, "ListingNotOwned");
        new Stash(catalog, seller.Items).Insert(listing.Item);
        state.Listings.Remove(listing.Id);
        Record(seller, listing.Item.ItemId, listing.Item.Quantity, listing.UnitPrice, "Cancelled", DateTime.UtcNow);
    }

    private void Shop(AccountState buyer, MutationReceipt command)
    {
        var item = catalog.Find(command.TargetId) ?? throw MarketError.Missing("UnknownItem");
        if (!item.ForSale) throw MarketError.Conflict("NotForSale");
        if (command.UnitPrice != item.Value) throw MarketError.Conflict("PriceChanged");
        if (command.Quantity > item.MaxStack) throw MarketError.Invalid("InvalidQuantity");
        Debit(buyer, Total(item.Value, command.Quantity));
        new Stash(catalog, buyer.Items).AddStock(item, command.Quantity);
        Record(buyer, item.Id, command.Quantity, item.Value, "Shop", DateTime.UtcNow);
    }

    private void Recycle(AccountState account, MutationReceipt command)
    {
        var item = Stash.Take(account.Items, command.TargetId, command.Quantity);
        var definition = catalog[item.ItemId];
        if (command.UnitPrice != definition.Resale) throw MarketError.Conflict("PriceChanged");
        Credit(account, Total(definition.Resale, item.Quantity));
        Record(account, item.ItemId, item.Quantity, definition.Resale, "Recycled", DateTime.UtcNow);
    }

    private static Listing FindListing(PersistedState state, string id) => state.Listings.GetValueOrDefault(id)
        ?? throw MarketError.Missing("ListingNotFound");

    private static long Total(long price, int quantity) => price <= long.MaxValue / quantity ? price * quantity
        : throw MarketError.Invalid("PriceOverflow");

    private static void Debit(AccountState account, long amount)
    {
        if (amount > account.Credits) throw MarketError.Conflict("InsufficientCredits");
        account.Credits -= amount;
    }

    private static void Credit(AccountState account, long amount)
    {
        if (amount > long.MaxValue - account.Credits) throw MarketError.Conflict("CreditOverflow");
        account.Credits += amount;
    }

    private static void Record(AccountState account, string itemId, int quantity, long price, string status, DateTime utc) =>
        account.History.Add(new(Stash.NewId(), itemId, quantity, price, status, utc));

    private StateResponse Snapshot(PersistedState state, string name, string? token = null)
    {
        var account = state.Accounts[name];
        var listings = state.Listings.Values.OrderBy(listing => listing.Item.ItemId, StringComparer.Ordinal)
            .ThenBy(listing => listing.UnitPrice).ThenBy(listing => listing.Id, StringComparer.Ordinal).ToArray();
        var references = listings.GroupBy(listing => listing.Item.ItemId, StringComparer.Ordinal)
            .Select(group => new ReferencePrice(group.Key, group.Min(listing => listing.UnitPrice))).ToArray();
        return new(true, "", name, account.Credits, account.Items.ToArray(), listings,
            account.History.AsEnumerable().Reverse().ToArray(), references, catalog.Products, token);
    }
}
