using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: MarketServer.Integration DOTNET_PATH SERVER_DLL_PATH");
    return 2;
}

var dotnet = Path.GetFileName(args[0]) == args[0] ? args[0] : Path.GetFullPath(args[0]);
var host = new IntegrationHost(dotnet, Path.GetFullPath(args[1]));
var scratch = Directory.CreateTempSubdirectory("dustline-market-v060-");
try
{
    await Integration.Trade(host, scratch.FullName);
    await Integration.ProductQuotes(host, scratch.FullName);
    await Integration.Capacity(host, scratch.FullName);
    await Integration.PersistenceFailure(host, scratch.FullName);
    await Integration.Transport(host, scratch.FullName);
    Console.WriteLine("PASS: 5/5 integration scenarios.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
finally { scratch.Delete(recursive: true); }

internal static class Integration
{
    private const string Cake = "birthday_cake";
    private const long StartingCredits = 1_000_000L;
    private const long CakeResale = 52_013_140L;
    private static readonly string TestPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private static readonly JsonSerializerOptions CatalogJson = new() { WriteIndented = true };

    public static async Task Trade(IntegrationHost host, string root)
    {
        string catalog = SaveCatalog(root, "trade", [
            Def(Cake, 1, 1, 3, 111, 9, true, 3), Def("ammo", 1, 1, 10, 50, 19, true, 8),
            Def("rifle", 4, 2), Def("sight", 1, 1), Def("grip", 1, 1), Def("stock", 1, 1),
            Def("muzzle", 1, 1), Def("magazine", 1, 1), Def("kit", 2, 1)
        ], [
            new { ItemId = "rifle", LoadedAmmo = 9, LoadedAmmoId = "ammo", Wear = 17,
                Mods = new { Sight = 1, Grip = 2, Stock = 0, OwnedSight = 2, OwnedGrip = 2, OwnedStock = 1 },
                Parts = new { Sight = "sight", Grip = "grip", Stock = "stock", Muzzle = "muzzle", Magazine = "magazine" } },
            new { ItemId = "kit", UsesSpent = 2 }
        ]);
        string state = Path.Combine(root, "trade.state.json");
        string alice, bob, bobSecond, gunId, gunListing = "", buyerRequest = "", bobCakeListing;
        string aliceCake, bobCake;
        JsonElement aliceBeforeRestart, bobBeforeRestart, initialGun;
        await using (var running = await host.Start(catalog, state))
        using (var api = running.Client())
        {
            (await api.Send("state")).Reject(401, "Unauthorized");
            (await api.Send("register", new { Account = "Alice", Password = TestPassword, Credits = 5L })).Reject(400, "InvalidJson");
            (await api.Send("register", new { account = "Alice", Password = TestPassword })).Reject(400, "InvalidJson");
            var a = await Register(api, "Alice");
            var b = await Register(api, "bob");
            StateShape(a, "alice");
            StateShape(b, "bob");
            Quote(a, Cake, false, 1, 111, CakeResale);
            Check(Credits(a) == StartingCredits && Credits(b) == StartingCredits, "Incorrect starting credits.");
            Check(Count(a, Cake) == 1 && Count(b, Cake) == 1, "Exactly one cake should be minted per account.");
            aliceCake = Instance(OneItem(a, Cake));
            bobCake = Instance(OneItem(b, Cake));
            Check(aliceCake != bobCake, "Shared starter cake instance.");
            var aAmmo = OneItem(a, "ammo");
            initialGun = OneItem(a, "rifle");
            gunId = Instance(initialGun);
            Check(Instance(OneItem(b, "rifle")) != gunId && Qty(initialGun) == 1, "Rifle identity is not account-specific.");
            ValidateGun(initialGun, OneItem(b, "rifle"));
            Check(Number(OneItem(a, "kit"), "UsesSpent") == 2, "Trusted use count not initialized.");
            (await api.Send("register", Credentials("ALICE"))).Reject(409, "AccountExists");
            (await api.Send("login", new { Account = "alice", Password = TestPassword + "X" })).Reject(401, "InvalidCredentials");
            (await api.Send("login", Credentials("nobody"))).Reject(401, "InvalidCredentials");
            alice = await Login(api, "ALICE");
            bob = await Login(api, "bob");
            bobSecond = await Login(api, "bob");
            Check(new[] { alice, bob, bobSecond }.Distinct().Count() == 3, "A session token was reused.");
            StateShape(await api.Good("state", token: alice), "alice");
            StateShape(await api.Good("state", token: bob), "bob");
            (await api.Send("state", token: new string('0', 64))).Reject(401, "Unauthorized");
            (await api.Send("list", new { RequestId = "inject", InstanceId = gunId, Quantity = 1, UnitPrice = 1L,
                Parts = new { Sight = "sight" } }, alice)).Reject(400, "InvalidJson");
            (await api.Send("list", new { RequestId = "foreign", InstanceId = gunId, Quantity = 1, UnitPrice = 1L }, bob))
                .Reject(404, "ItemNotOwned");
            (await api.Send("shop", "{\"ItemId\":\"ammo\",\"Quantity\":1,\"Quantity\":2,\"RequestId\":\"dup\"}", alice))
                .Reject(400, "InvalidJson");
            (await api.Send("list", List("overflow", aAmmo, 2, long.MaxValue), alice)).Reject(400, "PriceOverflow");
            (await api.Send("shop", new { ItemId = "ammo", Quantity = 1 }, alice)).Reject(400, "InvalidRequestId");

            var put = List("same-request-id", aAmmo, 5, 120);
            a = await api.Good("list", put, alice);
            var listing = OnlyListing(a);
            string ammoListing = Id(listing);
            Check(Qty(Item(listing)) == 5 && Instance(Item(listing)) != Instance(aAmmo) &&
                Qty(OneItem(a, "ammo")) == 3 && Instance(OneItem(a, "ammo")) == Instance(aAmmo), "Stack was not split in escrow.");
            Check(Price(listing) == 120 && Text(listing, "Seller") == "alice" && Reference(a, "ammo") == 120, "Wrong listing price/reference.");
            Latest(a, "ammo", 5, 120, "Listed");
            Same(a, await api.Good("list", put, alice), "List replay");
            (await api.Send("list", List("same-request-id", aAmmo, 5, 121), alice)).Reject(409, "RequestIdConflict");
            (await api.Send("cancel", Cancel("not-owner", ammoListing), bob)).Reject(403, "ListingNotOwned");
            (await api.Send("buy", Buy("self", ammoListing, 1), alice)).Reject(409, "OwnListing");
            var buyAmmo = Buy("same-request-id", ammoListing, 2);
            b = await api.Good("buy", buyAmmo, bob);
            a = await api.Good("state", token: alice);
            Check(Credits(a) == StartingCredits + 240 && Credits(b) == StartingCredits - 240 && Count(b, "ammo") == 10 &&
                Qty(Item(OnlyListing(b))) == 3, "Partial buy did not settle exactly once.");
            Latest(a, "ammo", 2, 120, "Sold");
            Latest(b, "ammo", 2, 120, "Bought");
            Same(b, await api.Good("buy", buyAmmo, bobSecond), "Partial buy from another session");
            var cancelAmmo = Cancel("cancel-ammo", ammoListing);
            a = await api.Good("cancel", cancelAmmo, alice);
            Check(Count(a, "ammo") == 6 && Listings(a).Length == 0 && Fields(a, "References").Length == 0, "Cancel did not return remaining units.");
            Latest(a, "ammo", 3, 120, "Cancelled");
            Same(a, await api.Good("cancel", cancelAmmo, alice), "Cancel replay");

            a = await api.Good("list", List("gun-list", initialGun, 1, 451), alice);
            gunListing = Id(OnlyListing(a));
            Same(initialGun, Item(OnlyListing(a)), "Full gun escrow");
            Check(Count(a, "rifle") == 0, "Listed rifle still in seller stash.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<ApiReply> Race(string token, string requestId)
            {
                await start.Task;
                return await api.Send("buy", Buy(requestId, gunListing, 1), token);
            }
            var first = Race(bob, "last-first");
            var second = Race(bobSecond, "last-second");
            start.SetResult();
            var results = await Task.WhenAll(first, second);
            Check(results.Count(result => result.Ok) == 1, "Concurrent last-item sale had other than one winner.");
            var winner = results[0].Ok ? 0 : 1;
            buyerRequest = winner == 0 ? "last-first" : "last-second";
            b = results[winner].Success("buy");
            results[1 - winner].Reject(404, "ListingNotFound");
            var delivered = Items(b).Single(item => Instance(item) == gunId);
            SameCondition(initialGun, delivered);
            Check(Number(delivered, "X") != Number(initialGun, "X") || Number(delivered, "Y") != Number(initialGun, "Y"),
                "Recipient placement overlapped its original rifle.");
            a = await api.Good("state", token: alice);
            Check(Count(b, "rifle") == 2 && Credits(a) == StartingCredits + 691 && Credits(b) == StartingCredits - 691,
                "Concurrent balance or ownership mismatch.");
            Latest(a, "rifle", 1, 451, "Sold");
            Latest(b, "rifle", 1, 451, "Bought");
            var buyGun = Buy(buyerRequest, gunListing, 1);
            var replays = await Task.WhenAll(api.Good("buy", buyGun, bob), api.Good("buy", buyGun, bobSecond));
            foreach (var replay in replays) Same(b, replay, "Concurrent replay");
            (await api.Send("buy", Buy(buyerRequest, gunListing, 2), bob)).Reject(409, "RequestIdConflict");

            (await api.Send("shop", Shop("cake-shop", Cake, 1, 0), alice)).Reject(409, "NotForSale");
            (await api.Send("recycle", Recycle("foreign-cake", OneItem(a, Cake), 1, CakeResale), bob)).Reject(404, "ItemNotOwned");
            a = await api.Good("list", List("alice-cake-market", OneItem(a, Cake), 1, 1234), alice);
            string aliceCakeListing = Id(OnlyListing(a));
            b = await api.Good("list", List("bob-cake-market", OneItem(b, Cake), 1, 765), bob);
            bobCakeListing = Id(Listings(b).Single(entry => Text(entry, "Seller") == "bob"));
            a = await api.Good("list", List("cheap-ammo", OneItem(a, "ammo"), 1, 7), alice);
            Check(Reference(a, Cake) == 765 && Reference(a, "ammo") == 7, "Reference price mixed distinct ItemIds or system prices.");
            a = await api.Good("cancel", Cancel("cancel-alice-cake", aliceCakeListing), alice);
            var cake = OneItem(a, Cake);
            Check(Instance(cake) == aliceCake, "Cancelled cake lost its original identity.");
            var recycleCake = Recycle("recycle-alice-cake", cake, 1, CakeResale);
            aliceBeforeRestart = await api.Good("recycle", recycleCake, alice);
            Check(Count(aliceBeforeRestart, Cake) == 0 && Credits(aliceBeforeRestart) == StartingCredits + 691 + CakeResale,
                "Cake resale must be fixed and local to the owner.");
            Latest(aliceBeforeRestart, Cake, 1, CakeResale, "Recycled");
            Same(aliceBeforeRestart, await api.Good("recycle", recycleCake, alice), "Recycle replay");
            bobBeforeRestart = await api.Good("state", token: bob);
            Check(Credits(bobBeforeRestart) == StartingCredits - 691 && Count(bobBeforeRestart, Cake) == 0 &&
                Instance(Item(Listings(bobBeforeRestart).Single(entry => Id(entry) == bobCakeListing))) == bobCake,
                "Alice recycling affected Bob's cake or balance.");
            await host.MustReject(catalog, state, "market state lock is unavailable");
            InspectSyntheticState(state, alice, bob, bobSecond);
        }

        await using (var restarted = await host.Start(catalog, state))
        using (var api = restarted.Client())
        {
            foreach (string previous in new[] { alice, bob, bobSecond }) (await api.Send("state", token: previous)).Reject(401, "Unauthorized");
            alice = await Login(api, "alice");
            bob = await Login(api, "bob");
            var a = await api.Good("state", token: alice);
            var b = await api.Good("state", token: bob);
            Same(aliceBeforeRestart, a, "Seller restart snapshot");
            Same(bobBeforeRestart, b, "Buyer restart snapshot");
            SameCondition(initialGun, Items(b).Single(item => Instance(item) == gunId));
            Same(b, await api.Good("buy", Buy(buyerRequest, gunListing, 1), bob), "Buy replay across restart");
            Same(a, await api.Good("recycle", new { RequestId = "recycle-alice-cake", InstanceId = aliceCake, Quantity = 1, UnitPrice = CakeResale }, alice),
                "Recycle replay across restart");
            b = await api.Good("cancel", Cancel("cancel-bob-cake", bobCakeListing), bob);
            Check(Instance(OneItem(b, Cake)) == bobCake && Count(a, Cake) == 0, "Escrow or one-time cake grant did not survive restart.");
            Latest(b, Cake, 1, 765, "Cancelled");
            (await api.Send("register", Credentials("alice"))).Reject(409, "AccountExists");
        }
        Console.WriteLine("PASS trade: two accounts, separate sessions, full PascalCase snapshots, split/buy/credit/cancel/history, last-item race, replays, cake, file lock, kill/restart, whole-rifle round trip.");
    }

    public static async Task ProductQuotes(IntegrationHost host, string root)
    {
        const string id = "market-only";
        const long oldValue = 45, oldResale = 17, newValue = 71, newResale = 4_000_000_000L;
        string catalog = SaveCatalog(root, "quotes", [Def(Cake, 1, 1), Def(id, 1, 1, 4, oldValue, oldResale, true, 2),
            Def("gift", 1, 1, 1, 0, 0)]);
        string state = Path.Combine(root, "quotes.state.json");
        var paid = Shop("paid", id, 2, oldValue);
        object recycled, staleRecycle;
        await using (var running = await host.Start(catalog, state))
        using (var api = running.Client())
        {
            var a = await Register(api, "alice");
            Quote(a, id, true, 4, oldValue, oldResale);
            Quote(a, "gift", true, 1, 0, 0);
            string token = await Login(api, "alice");
            (await api.Send("shop", new { RequestId = "unquoted-shop", ItemId = "gift", Quantity = 1 }, token))
                .Reject(400, "InvalidUnitPrice");
            (await api.Send("recycle", new { RequestId = "unquoted-recycle", InstanceId = Instance(OneItem(a, id)), Quantity = 1 }, token))
                .Reject(400, "InvalidUnitPrice");
            (await api.Send("shop", Shop("above-limit", id, 5, oldValue), token)).Reject(400, "InvalidQuantity");
            Same(a, await api.Good("state", token: token), "Missing quote or above-limit purchase");

            a = await api.Good("shop", Shop("gift-shop", "gift", 1, 0), token);
            Latest(a, "gift", 1, 0, "Shop");
            Check(Credits(a) == StartingCredits && Count(a, "gift") == 1, "Explicit free purchase did not settle.");
            a = await api.Good("recycle", Recycle("gift-recycle", OneItem(a, "gift"), 1, 0), token);
            Latest(a, "gift", 1, 0, "Recycled");
            Check(Credits(a) == StartingCredits && Count(a, "gift") == 0, "Explicit zero resale did not settle.");

            a = await api.Good("shop", paid, token);
            Check(Credits(a) == StartingCredits - 2 * oldValue && Qty(OneItem(a, id)) == 4, "Quoted purchase did not settle.");
            Latest(a, id, 2, oldValue, "Shop");
            recycled = Recycle("recycled", OneItem(a, id), 1, oldResale);
            a = await api.Good("recycle", recycled, token);
            Check(Credits(a) == StartingCredits - 2 * oldValue + oldResale && Qty(OneItem(a, id)) == 3, "Quoted resale did not settle.");
            Latest(a, id, 1, oldResale, "Recycled");
            staleRecycle = Recycle("stale-recycle", OneItem(a, id), 1, oldResale);
        }

        string beforeRestart = File.ReadAllText(state);
        catalog = SaveCatalog(root, "quotes-new", [Def(Cake, 1, 1), Def(id, 1, 1, 4, newValue, newResale, true, 2),
            Def("gift", 1, 1, 1, 0, 0)]);
        await using (var running = await host.Start(catalog, state))
        using (var api = running.Client())
        {
            string token = await Login(api, "alice");
            var a = await api.Good("state", token: token);
            Quote(a, id, true, 4, newValue, newResale);
            (await api.Send("shop", Shop("stale-shop", id, 1, oldValue), token)).Reject(409, "PriceChanged");
            (await api.Send("recycle", staleRecycle, token)).Reject(409, "PriceChanged");
            Same(a, await api.Good("state", token: token), "Stale quotes");
            Same(a, await api.Good("shop", paid, token), "Committed shop replay after price change");
            Same(a, await api.Good("recycle", recycled, token), "Committed recycle replay after price change");
            (await api.Send("shop", Shop("paid", id, 2, newValue), token)).Reject(409, "RequestIdConflict");
            (await api.Send("recycle", Recycle("recycled", OneItem(a, id), 1, newResale), token)).Reject(409, "RequestIdConflict");
            Check(File.ReadAllText(state) == beforeRestart, "Replays or stale quotes changed persisted state.");

            long balance = Credits(a);
            a = await api.Good("shop", Shop("stale-shop", id, 1, newValue), token);
            Latest(a, id, 1, newValue, "Shop");
            Check(Credits(a) == balance - newValue && Qty(OneItem(a, id)) == 4, "Refreshed purchase did not settle.");
            a = await api.Good("recycle", Recycle("stale-recycle", OneItem(a, id), 1, newResale), token);
            Latest(a, id, 1, newResale, "Recycled");
            Check(Credits(a) == balance - newValue + newResale && Qty(OneItem(a, id)) == 3, "Refreshed resale did not settle.");
        }
        Console.WriteLine("PASS quotes: authoritative Products, quantity limit, zero prices, stale quote rollback, and committed replay across catalog changes.");
    }

    public static async Task Capacity(IntegrationHost host, string root)
    {
        string catalog = SaveCatalog(root, "capacity", [Def(Cake, 1, 1), Def("ammo", 1, 1, 10, 50, 19, true, 8),
            Def("spacer", 1, 1, 1, 11, 4_000_000_000L, true, 357), Def("token", 1, 1, 1, 1, 1, false, 1)]);
        string state = Path.Combine(root, "capacity.state.json");
        await using var running = await host.Start(catalog, state);
        using var api = running.Client();
        await Register(api, "alice");
        await Register(api, "bob");
        string alice = await Login(api, "alice"), bob = await Login(api, "bob");
        var a = await api.Good("state", token: alice);
        var b = await api.Good("state", token: bob);
        foreach (var account in new[] { a, b })
        {
            var cells = Items(account).Select(item => (Number(item, "X"), Number(item, "Y"))).ToHashSet();
            Check(Items(account).Length == 360 && cells.Count == 360 && cells.All(cell => cell.Item1 is >= 0 and < 12 && cell.Item2 is >= 0 and < 30),
                "Online stash does not cover exactly 12x30 unique cells.");
        }
        a = await api.Good("list", List("overflow-ammo-list", OneItem(a, "ammo"), 5, 25), alice);
        b = await api.Good("state", token: bob);
        string listing = Id(OnlyListing(a));
        var buy = Buy("no-room-buy", listing, 5);
        string beforeFile = File.ReadAllText(state);
        (await api.Send("buy", buy, bob)).Reject(409, "StashFull");
        (await api.Send("shop", Shop("no-room-shop", "ammo", 5, 50), bob)).Reject(409, "StashFull");
        Same(a, await api.Good("state", token: alice), "Failed buy changed seller state");
        Same(b, await api.Good("state", token: bob), "Failed buy/shop partially merged or debited");
        Check(File.ReadAllText(state) == beforeFile, "Rollback changed persisted state.");
        b = await api.Good("recycle", Recycle("clear-space-bob", Items(b).First(item => ItemId(item) == "spacer"), 1, 4_000_000_000L), bob);
        Check(Credits(b) == StartingCredits + 4_000_000_000L, "Credits did not preserve long range.");
        b = await api.Good("buy", buy, bob);
        Check(Count(b, "ammo") == 13 && Credits(b) == StartingCredits + 4_000_000_000L - 125,
            "Failed buy request could not be retried after freeing a cell.");
        a = await api.Good("state", token: alice);
        var staged = Items(a).First(item => ItemId(item) == "spacer");
        a = await api.Good("list", List("fill-list", staged, 1, 32), alice);
        var cancel = Cancel("no-room-cancel", Id(OnlyListing(a)));
        var shop = Shop("fill-shop", "spacer", 1, 11);
        a = await api.Good("shop", shop, alice);
        Check(Credits(a) == StartingCredits + 125 - 11, "Shop did not debit the catalog price.");
        Latest(a, "spacer", 1, 11, "Shop");
        Same(a, await api.Good("shop", shop, alice), "Shop replay");
        beforeFile = File.ReadAllText(state);
        (await api.Send("cancel", cancel, alice)).Reject(409, "StashFull");
        Same(a, await api.Good("state", token: alice), "Failed cancel lost escrow or changed history");
        Check(File.ReadAllText(state) == beforeFile, "Failed cancel changed persisted state.");
        a = await api.Good("recycle", Recycle("clear-space-alice", OneItem(a, "token"), 1, 1), alice);
        a = await api.Good("cancel", cancel, alice);
        Check(Items(a).Any(item => Instance(item) == Instance(staged)) && Listings(a).Length == 0, "Retry did not return the escrowed instance.");
        Console.WriteLine("PASS capacity: 12x30 layout, seller/buyer rollback on failed buy, shop/cancel rollback, retry after freeing space, long credits and trusted shop pricing.");
    }

    public static async Task PersistenceFailure(IntegrationHost host, string root)
    {
        string catalog = SaveCatalog(root, "save-failure", [Def(Cake, 1, 1)]);
        string state = Path.Combine(root, "save-failure.state.json");
        await using var running = await host.Start(catalog, state);
        using var api = running.Client();
        Directory.CreateDirectory(state);
        (await api.Send("register", Credentials("alice"))).Reject(503, "StateUnavailable");
        (await api.Send("login", Credentials("alice"))).Reject(401, "InvalidCredentials");
        Check(!Directory.EnumerateFiles(root, "save-failure.state.json.*.tmp").Any(), "Failed replace left a staged file.");
        Directory.Delete(state);
        var a = await Register(api, "alice");
        Check(File.Exists(state) && Count(a, Cake) == 1 && Credits(a) == StartingCredits, "Retry after failed replacement did not commit cleanly.");
        Console.WriteLine("PASS persistence failure: HTTP 503, no in-memory commit or staged file, clean retry.");
    }

    public static async Task Transport(IntegrationHost host, string root)
    {
        string catalog = SaveCatalog(root, "transport", [Def(Cake, 1, 1)]);
        string state = Path.Combine(root, "transport.state.json");
        await host.MustReject(catalog, state, "Non-loopback endpoints require HTTPS", "--listen", "0.0.0.0");
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(15));
        string pfx = Path.Combine(root, "loopback-test.pfx");
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        File.WriteAllBytes(pfx, certificate.Export(X509ContentType.Pkcs12, password));
        await using var running = await host.Start(catalog, state, pfx, password);
        using var api = running.Client(certificate.Thumbprint);
        await Register(api, "tls-user");
        StateShape(await api.Good("state", token: await Login(api, "tls-user")), "tls-user");
        Console.WriteLine("PASS transport: non-loopback HTTP refused, configured HTTPS authenticated over loopback.");
    }

    private static object Def(string id, int width, int height, int maxStack = 1, long value = 100, long resale = 20,
        bool forSale = true, int starter = 0) =>
        new { Id = id, Width = width, Height = height, MaxStack = maxStack, Value = value, Resale = resale, ForSale = forSale, StarterQuantity = starter };

    private static string SaveCatalog(string root, string name, object[] items, object[]? builds = null)
    {
        string path = Path.Combine(root, name + ".catalog.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { Items = items, StarterBuilds = builds ?? [] }, CatalogJson));
        return path;
    }

    private static object Credentials(string account) => new { Account = account, Password = TestPassword };
    private static Task<JsonElement> Register(Api api, string account) => api.Good("register", Credentials(account));
    private static async Task<string> Login(Api api, string account)
    {
        var state = await api.Good("login", Credentials(account));
        StateShape(state, account.ToLowerInvariant(), login: true);
        string token = Text(state, "Token");
        Check(token.Length == 64 && token.All(char.IsAsciiHexDigit), "Invalid token shape.");
        return token;
    }

    private static object List(string id, JsonElement item, int count, long price) => new { RequestId = id, InstanceId = Instance(item), Quantity = count, UnitPrice = price };
    private static object Buy(string id, string listing, int count) => new { RequestId = id, ListingId = listing, Quantity = count };
    private static object Cancel(string id, string listing) => new { RequestId = id, ListingId = listing };
    private static object Shop(string id, string item, int count, long price) => new { RequestId = id, ItemId = item, Quantity = count, UnitPrice = price };
    private static object Recycle(string id, JsonElement item, int count, long price) => new { RequestId = id, InstanceId = Instance(item), Quantity = count, UnitPrice = price };

    private static string Id(JsonElement json) => Text(json, "Id");
    private static string Instance(JsonElement json) => Text(json, "InstanceId");
    private static string ItemId(JsonElement json) => Text(json, "ItemId");
    private static long Qty(JsonElement json) => Number(json, "Quantity");
    private static long Price(JsonElement json) => Number(json, "UnitPrice");
    private static long Credits(JsonElement json) => Number(json, "Credits");
    private static string Text(JsonElement json, string property) => json.GetProperty(property).GetString()!;
    private static long Number(JsonElement json, string property) => json.GetProperty(property).GetInt64();
    private static JsonElement[] Fields(JsonElement json, string property) => json.GetProperty(property).EnumerateArray().ToArray();
    private static JsonElement[] Items(JsonElement json) => Fields(json, "Items");
    private static JsonElement[] Listings(JsonElement json) => Fields(json, "Listings");
    private static JsonElement Item(JsonElement listing) => listing.GetProperty("Item");
    private static JsonElement OneItem(JsonElement json, string item) => Items(json).Single(x => ItemId(x) == item);
    private static JsonElement OnlyListing(JsonElement json) => Listings(json).Single();
    private static long Count(JsonElement json, string item) => Items(json).Where(x => ItemId(x) == item).Sum(Qty);
    private static long Reference(JsonElement json, string item) => Price(Fields(json, "References").Single(x => ItemId(x) == item));

    private static void Quote(JsonElement json, string id, bool forSale, int maxStack, long value, long resale)
    {
        var product = Fields(json, "Products").Single(p => Id(p) == id);
        Check(product.GetProperty("ForSale").GetBoolean() == forSale && Number(product, "MaxStack") == maxStack &&
            Number(product, "Value") == value && Number(product, "Resale") == resale, "Wrong server quote for " + id + ".");
    }

    private static void ValidateGun(JsonElement gun, JsonElement other)
    {
        Check(Number(gun, "Wear") == 17 && Number(gun, "LoadedAmmo") == 9 && Text(gun, "LoadedAmmoId") == "ammo", "Gun condition missing.");
        Check(Number(gun.GetProperty("Mods"), "Sight") == 1 && Number(gun.GetProperty("Mods"), "OwnedSight") == 2, "Gun mods missing.");
        var ids = new HashSet<string> { Instance(gun), Instance(other) };
        foreach (string slot in new[] { "Sight", "Grip", "Stock", "Muzzle", "Magazine" })
        {
            var link = gun.GetProperty("Parts").GetProperty(slot);
            var otherLink = other.GetProperty("Parts").GetProperty(slot);
            Check(ItemId(link) == slot.ToLowerInvariant() && ItemId(otherLink) == ItemId(link) &&
                Instance(link).Length > 0 && Instance(otherLink).Length > 0 && ids.Add(Instance(link)) && ids.Add(Instance(otherLink)),
                "Starter part instances are invalid or shared between accounts.");
        }
    }

    private static void StateShape(JsonElement json, string account, bool login = false)
    {
        var expected = new List<string> { "Ok", "Error", "Account", "Credits", "Items", "Listings", "History", "References", "Products" };
        if (login) expected.Add("Token");
        Shape(json, expected.ToArray());
        Check(Text(json, "Account") == account && Credits(json) >= 0, "Account leaked or wrong credit shape.");
        foreach (var item in Items(json)) ItemShape(item);
        foreach (var listing in Listings(json)) { Shape(listing, "Id", "Seller", "UnitPrice", "Item"); ItemShape(Item(listing)); }
        foreach (var record in Fields(json, "History"))
        {
            Shape(record, "Id", "ItemId", "Quantity", "UnitPrice", "Status", "Utc");
            Check(Id(record).Length > 0 && record.GetProperty("Utc").GetDateTime().Kind == DateTimeKind.Utc, "History timestamp is not UTC.");
        }
        foreach (var reference in Fields(json, "References")) Shape(reference, "ItemId", "UnitPrice");
        foreach (var product in Fields(json, "Products")) Shape(product, "Id", "ForSale", "MaxStack", "Value", "Resale");
    }

    private static void ItemShape(JsonElement item)
    {
        Shape(item, "InstanceId", "ItemId", "Quantity", "X", "Y", "Rotated", "Area", "Slot", "LoadedAmmo", "Mods", "Parts", "LoadedAmmoId", "UsesSpent", "Wear");
        Check(Number(item, "Area") == 0 && Number(item, "Slot") == 0 && Qty(item) > 0, "Item is outside online stash.");
        _ = item.GetProperty("Rotated").GetBoolean();
        Shape(item.GetProperty("Mods"), "Sight", "Grip", "Stock", "OwnedSight", "OwnedGrip", "OwnedStock");
        var parts = item.GetProperty("Parts");
        Shape(parts, "Sight", "Grip", "Stock", "Muzzle", "Magazine");
        foreach (var part in parts.EnumerateObject()) Shape(part.Value, "ItemId", "InstanceId");
    }

    private static void Latest(JsonElement json, string item, int quantity, long price, string status)
    {
        var latest = Fields(json, "History").First();
        Check(ItemId(latest) == item && Qty(latest) == quantity && Price(latest) == price && Text(latest, "Status") == status,
            "Wrong latest history entry for " + status + ".");
        StateShape(json, Text(json, "Account"));
    }

    private static void Shape(JsonElement json, params string[] properties) =>
        Check(json.EnumerateObject().Select(property => property.Name).Order().SequenceEqual(properties.Order()), "Wrong JSON shape.");

    private static void Same(JsonElement expected, JsonElement actual, string label) =>
        Check(expected.GetRawText() == actual.GetRawText(), label + " changed the snapshot.");

    private static void SameCondition(JsonElement expected, JsonElement actual)
    {
        ItemShape(actual);
        foreach (var field in expected.EnumerateObject())
            if (field.Name is not ("X" or "Y" or "Rotated")) Same(field.Value, actual.GetProperty(field.Name), "Rifle field " + field.Name);
    }

    private static void InspectSyntheticState(string path, params string[] tokens)
    {
        string saved = File.ReadAllText(path);
        Check(!saved.Contains(TestPassword, StringComparison.Ordinal) && tokens.All(token => !saved.Contains(token, StringComparison.Ordinal)),
            "Plaintext test password or bearer token reached disk.");
        using var json = JsonDocument.Parse(saved);
        var accounts = json.RootElement.GetProperty("Accounts");
        var a = accounts.GetProperty("alice").GetProperty("Password");
        var b = accounts.GetProperty("bob").GetProperty("Password");
        Check(Text(a, "Salt") != Text(b, "Salt") && Text(a, "Hash") != Text(b, "Hash"), "Equal passwords did not receive unique salts.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
