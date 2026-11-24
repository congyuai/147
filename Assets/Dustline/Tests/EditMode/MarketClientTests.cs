using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Dustline.Tests
{
    public sealed class MarketClientTests
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const string Loopback = "http://127.0.0.1:5076/";
        const string Name = "market-test";
        const string ProductQuotes = "[{\"Id\":\"market-only\",\"ForSale\":true,\"MaxStack\":7,\"Value\":5201314099,\"Resale\":0},"
            + "{\"Id\":\"birthday_cake\",\"ForSale\":false,\"MaxStack\":1,\"Value\":111,\"Resale\":52013140}]";
        const string EmptyState = "{\"Ok\":true,\"Account\":\"market-test\",\"Credits\":5201314099,\"Items\":[],\"Listings\":[],\"History\":[],"
            + "\"References\":[],\"Products\":" + ProductQuotes + "}";
        static readonly Type Client = typeof(GameUI).Assembly.GetType("Dustline.MarketClient", true);
        static readonly Type ActionType = typeof(GameUI).Assembly.GetType("Dustline.MarketAction", true);

        [Serializable]
        sealed class OrderFields
        {
            public string Account, RequestId, ItemId, InstanceId, ListingId;
            public int Quantity;
            public long UnitPrice;
        }

        [TestCase("http://localhost:5076", "http://127.0.0.1:5076/")]
        [TestCase("http://127.0.0.1:5076/", "http://127.0.0.1:5076/")]
        [TestCase("http://[::1]:5076", "http://[::1]:5076/")]
        [TestCase(" https://market.invalid:5076 ", "https://market.invalid:5076/")]
        [TestCase("http://market.invalid:5076", null)]
        [TestCase("http://192.168.1.1:5076", null)]
        [TestCase("http://localhost.market.invalid", null)]
        [TestCase("https://account:password@market.invalid", null)]
        [TestCase("https://market.invalid?query=value", null)]
        [TestCase("https://market.invalid/#fragment", null)]
        [TestCase("https://market.invalid/secret-path", null)]
        [TestCase("https://market.invalid/path/..", null)]
        [TestCase("https://market.invalid:0", null)]
        [TestCase("file:///tmp/market", null)]
        [TestCase("", null)]
        public void OnlySafeMarketOriginsAreAccepted(string source, string normalized)
        {
            var test = new Harness();
            using (test)
            {
                Assert.That(test.SetUrl(source), Is.EqualTo(normalized != null));
                Assert.That(test.Value("Url"), Is.EqualTo(normalized ?? ""));
                Assert.That(test.Requests, Is.Empty);
            }
        }

        [Test]
        public void RegisterThenLoginAndRefreshServerInstances() => Run(async test =>
        {
            test.Reply(200, EmptyState);
            test.LoginReply();
            test.Reply(200, EmptyState.Replace("\"Items\":[]", "\"Items\":[{\"ItemId\":\"rifle\",\"InstanceId\":\"owned\",\"Quantity\":1,\"Wear\":17,"
                + "\"Parts\":{\"Sight\":{\"ItemId\":\"optic\",\"InstanceId\":\"part\"}}}]")
                .Replace("\"Listings\":[]", "\"Listings\":[{\"Id\":\"offer\",\"Seller\":\"another\",\"UnitPrice\":5201314099,"
                    + "\"Item\":{\"ItemId\":\"armor\",\"InstanceId\":\"listed\",\"Quantity\":1,\"Wear\":4}}]")
                .Replace("\"History\":[]", "\"History\":[{\"Id\":\"history\",\"ItemId\":\"ammo\",\"Quantity\":3,\"UnitPrice\":23,\"Status\":\"Bought\",\"Utc\":\"2026-11-24T00:00:00Z\"}]")
                .Replace("\"References\":[]", "\"References\":[{\"ItemId\":\"armor\",\"UnitPrice\":5201314099}]"));
            await test.Expect("Authenticate", null, Name, test.Secret, true);
            Assert.That(test.Value("SignedIn"), Is.False);
            await test.Expect("Authenticate", null, Name, test.Secret, false);
            await test.Expect("Refresh", null);

            var register = test.Requests[0];
            Assert.That(register.Path, Is.EqualTo("/v1/register"));
            Assert.That(register.Method, Is.EqualTo("POST"));
            Assert.That(register.HasBearer, Is.False);
            Assert.That(Guid.TryParseExact(register.Fields.RequestId, "N", out _), Is.True);
            Assert.That(register.Fields.Account, Is.EqualTo(Name));
            Assert.That(test.Requests[1].Path, Is.EqualTo("/v1/login"));
            Assert.That(test.Requests[1].HasBearer, Is.False);
            Assert.That(test.Requests[2].Path, Is.EqualTo("/v1/state"));
            Assert.That(test.Requests[2].Method, Is.EqualTo("GET"));
            Assert.That(test.Requests[2].HasBearer, Is.True);
            Assert.That(test.Requests[2].Json, Is.Null);

            object state = test.Value("State");
            Assert.That(Field(state, "Token"), Is.Null);
            Assert.That(Field(state, "Credits"), Is.EqualTo(5201314099L));
            var item = ((ItemStack[])Field(state, "Items"))[0];
            Assert.That(item.InstanceId, Is.EqualTo("owned"));
            Assert.That(item.Wear, Is.EqualTo(17));
            Assert.That(item.Parts.Sight.InstanceId, Is.EqualTo("part"));
            var listing = ((Array)Field(state, "Listings")).GetValue(0);
            Assert.That(Field(listing, "UnitPrice"), Is.EqualTo(5201314099L));
            Assert.That(((ItemStack)Field(listing, "Item")).Wear, Is.EqualTo(4));
            Assert.That(Field(((Array)Field(state, "History")).GetValue(0), "Status"), Is.EqualTo("Bought"));
            Assert.That(Field(((Array)Field(state, "References")).GetValue(0), "UnitPrice"), Is.EqualTo(5201314099L));
            var products = (Array)Field(state, "Products");
            Assert.That(products.Length, Is.EqualTo(2));
            var product = products.GetValue(0);
            Assert.That(Field(product, "Id"), Is.EqualTo("market-only"));
            Assert.That(Field(product, "ForSale"), Is.True);
            Assert.That(Field(product, "MaxStack"), Is.EqualTo(7));
            Assert.That(Field(product, "Value"), Is.EqualTo(5201314099L));
            Assert.That(Field(product, "Resale"), Is.EqualTo(0L));
            var cake = products.GetValue(1);
            Assert.That(Field(cake, "Id"), Is.EqualTo("birthday_cake"));
            Assert.That(Field(cake, "ForSale"), Is.False);
            Assert.That(Field(cake, "Resale"), Is.EqualTo(52013140L));
        });

        [TestCase("List", "list", "InstanceId", 3, 5201314099L)]
        [TestCase("Buy", "buy", "ListingId", 2, 0L)]
        [TestCase("Cancel", "cancel", "ListingId", 0, 0L)]
        [TestCase("Shop", "shop", "ItemId", 1, 5201314099L)]
        [TestCase("Recycle", "recycle", "InstanceId", 1, 0L)]
        public void TradeSendsOnlyItsContractFields(string action, string route, string targetField, int quantity, long price) => Run(async test =>
        {
            await test.Login();
            test.Reply(200, EmptyState);
            await test.Expect("Trade", null, Enum.Parse(ActionType, action), "server-target", quantity, price);
            var request = test.Requests[1];
            Assert.That(request.Path, Is.EqualTo("/v1/" + route));
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.HasBearer, Is.True);
            Assert.That(request.Fields.Quantity, Is.EqualTo(quantity));
            Assert.That(request.Fields.UnitPrice, Is.EqualTo(price));
            Assert.That(request.Json.Contains("\"UnitPrice\""), Is.EqualTo(action == "List" || action == "Shop" || action == "Recycle"));
            Assert.That(Field(request.Fields, targetField), Is.EqualTo("server-target"));
            Assert.That(Guid.TryParseExact(request.Fields.RequestId, "N", out _), Is.True);
            Assert.That(request.Json, Does.Not.Contain("\"Account\"").And.Not.Contain("\"Password\"").And.Not.Contain("\"Credits\"")
                .And.Not.Contain("\"Items\"").And.Not.Contain("\"Parts\""));
            foreach (string other in new[] { "ItemId", "InstanceId", "ListingId" })
                if (other != targetField) Assert.That(request.Json, Does.Not.Contain("\"" + other + "\""));
        });

        [TestCase(null)]
        [TestCase("[]")]
        [TestCase("[{\"Id\":\"bad-limit\",\"MaxStack\":0,\"Value\":10,\"Resale\":1}]")]
        [TestCase("[{\"Id\":\"duplicate\",\"MaxStack\":1},{\"Id\":\"duplicate\",\"MaxStack\":1}]")]
        public void InvalidProductSnapshotsDoNotReplaceConfirmedState(string products) => Run(async test =>
        {
            await test.Login();
            var state = test.Value("State");
            Assert.That(state, Is.Not.Null);
            test.Reply(200, EmptyState.Replace(",\"Products\":" + ProductQuotes, products == null ? "" : ",\"Products\":" + products));
            await test.Expect("Refresh", "InvalidReply");
            Assert.That(test.Value("State"), Is.SameAs(state));
        });

        [Test]
        public void LoginRejectsABalanceWithoutTheProductContract() => Run(async test =>
        {
            test.LoginReply(EmptyState.Replace(",\"Products\":" + ProductQuotes, ""));
            await test.Expect("Authenticate", "InvalidReply", Name, test.Secret, false);
            Assert.That(test.Value("SignedIn"), Is.False);
            Assert.That(test.Value("State"), Is.Null);
        });

        [TestCase("Shop")]
        [TestCase("Recycle")]
        public void SystemTradeRequiresQuotedPrice(string action) => Run(async test =>
        {
            await test.Login();
            await test.Expect("Trade", "InvalidOrder", Enum.Parse(ActionType, action), "server-target", 1, -1L);
            Assert.That(test.Requests, Has.Count.EqualTo(1));
            Assert.That(test.Value("PendingAction"), Is.Null);
        });

        [Test]
        public void RetryKeepsConfirmedPriceAndPriceChangeRequiresFreshConfirmation() => Run(async test =>
        {
            await test.Login();
            string fresh = EmptyState.Replace("\"Value\":5201314099", "\"Value\":45");
            var shop = Enum.Parse(ActionType, "Shop");
            var product = ((Array)Field(test.Value("State"), "Products")).GetValue(0);
            string id = (string)Field(product, "Id");
            long acknowledged = (long)Field(product, "Value");
            test.Reply(503, "{\"Ok\":false,\"Error\":\"StateUnavailable\"}");
            await test.Expect("Trade", "ServiceUnavailable", shop, id, 1, acknowledged);
            var original = test.Requests[1];
            Assert.That(original.Fields.UnitPrice, Is.EqualTo(acknowledged));

            test.Reply(200, fresh);
            await test.Expect("Refresh", null);
            await test.Expect("Trade", "RetryRequired", shop, id, 1, 45L);
            test.Reply(409, "{\"Ok\":false,\"Error\":\"PriceChanged\"}");
            await test.Expect("Retry", "PriceChanged");
            Assert.That(test.Requests[3].Json, Is.EqualTo(original.Json));
            Assert.That(test.Value("PendingAction"), Is.Null);
            Assert.That(test.Value("State"), Is.Null);
            await test.Expect("Trade", "RefreshRequired", shop, id, 1, 45L);
            Assert.That(test.Requests, Has.Count.EqualTo(4));

            test.Reply(200, fresh);
            await test.Expect("Refresh", null);
            var updated = ((Array)Field(test.Value("State"), "Products")).GetValue(0);
            test.Reply(200, fresh);
            await test.Expect("Trade", null, shop, id, 1, (long)Field(updated, "Value"));
            Assert.That(test.Requests[5].Fields.UnitPrice, Is.EqualTo(45L));
            Assert.That(test.Requests[5].Fields.RequestId, Is.Not.EqualTo(original.Fields.RequestId));
        });

        [Test]
        public void TimeoutAndDoubleTapKeepOneOrderAndRetryItsExactPayload() => Run(async test =>
        {
            await test.Login();
            var blocked = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            test.Responses.Enqueue(_ => blocked.Task);
            var first = test.Invoke("Trade", Enum.Parse(ActionType, "Buy"), "offer", 2, 0L);
            try
            {
                await test.Expect("Trade", "RetryRequired", Enum.Parse(ActionType, "Buy"), "offer", 2, 0L);
                await test.ExpectResult(first, "Timeout");
                Assert.That(test.Requests, Has.Count.EqualTo(2));
                Assert.That(test.Value("PendingAccount"), Is.EqualTo(Name));
                Assert.That(test.SetUrl("https://market.invalid"), Is.False);
                Assert.That(test.Call("SignOut"), Is.EqualTo(false));

                test.Reply(503, "{\"Ok\":false,\"Error\":\"StateUnavailable\"}");
                await test.Expect("Retry", "ServiceUnavailable");
                test.Reply(200, "{\"Ok\":true}");
                await test.Expect("Retry", "InvalidReply");
                test.Reply(200, EmptyState);
                await test.Expect("Retry", null);
                var original = test.Requests[1].Json;
                for (int i = 2; i <= 4; i++) Assert.That(test.Requests[i].Json, Is.EqualTo(original));
                Assert.That(test.Value("PendingAction"), Is.Null);
                test.Reply(200, EmptyState);
                await test.Expect("Trade", null, Enum.Parse(ActionType, "Buy"), "offer", 2, 0L);
                Assert.That(test.Requests[5].Fields.RequestId, Is.Not.EqualTo(test.Requests[1].Fields.RequestId));
            }
            finally { blocked.TrySetCanceled(); }
        });

        [Test]
        public void SessionExpiryKeepsOrderForSameAccountOnly() => Run(async test =>
        {
            await test.Login();
            test.Reply(401, "{\"Ok\":false,\"Error\":\"Unauthorized\"}");
            await test.Expect("Trade", "SignInRequired", Enum.Parse(ActionType, "Cancel"), "offer", 0, 0L);
            Assert.That(test.Value("SignedIn"), Is.False);
            Assert.That(test.Value("State"), Is.Null);
            await test.Expect("Authenticate", "AccountLocked", "different", test.Secret, false);
            Assert.That(test.Requests, Has.Count.EqualTo(2));
            await test.Login();
            test.Reply(409, "{\"Ok\":false,\"Error\":\"ListingNotFound\"}");
            await test.Expect("Retry", "ListingNotFound");
            Assert.That(test.Requests[3].Json, Is.EqualTo(test.Requests[1].Json));
            Assert.That(test.Value("PendingAction"), Is.Null);
            Assert.That(test.Value("State"), Is.Null);
            Assert.That(test.Call("SignOut"), Is.EqualTo(true));
        });

        static void Run(Func<Harness, Task> check)
        {
            var context = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                using var test = new Harness();
                Assert.That(test.SetUrl(Loopback), Is.True);
                check(test).GetAwaiter().GetResult();
            }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
        }

        static object Field(object target, string name) => target.GetType().GetField(name, Instance | BindingFlags.Public).GetValue(target);

        sealed class RecordedRequest
        {
            internal string Path, Method, Json;
            internal bool HasBearer;
            internal OrderFields Fields => JsonUtility.FromJson<OrderFields>(Json);
        }

        sealed class Harness : HttpMessageHandler
        {
            internal readonly string Secret = Guid.NewGuid().ToString("N");
            readonly string fakeToken = Guid.NewGuid().ToString("N");
            internal readonly List<RecordedRequest> Requests = new List<RecordedRequest>();
            internal readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> Responses = new Queue<Func<CancellationToken, Task<HttpResponseMessage>>>();
            readonly HttpClient transport;
            readonly object client;

            internal Harness()
            {
                transport = (HttpClient)Client.GetMethod("CreateHttp", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { this });
                client = Activator.CreateInstance(Client, Instance, null, new object[] { transport, TimeSpan.FromMilliseconds(400) }, null);
            }

            internal object Value(string property) => Client.GetProperty(property, Instance).GetValue(client);
            internal object Call(string method, params object[] args) => Client.GetMethod(method, Instance).Invoke(client, args);
            internal bool SetUrl(string url) => (bool)Call("SetEndpoint", url);
            internal Task Invoke(string method, params object[] args) => (Task)Call(method, args);
            internal Task Expect(string method, string error, params object[] args) => ExpectResult(Invoke(method, args), error);

            internal async Task ExpectResult(Task task, string error)
            {
                await task;
                var result = task.GetType().GetProperty("Result").GetValue(task);
                Assert.That(Field(result, "Error"), Is.EqualTo(error));
                Assert.That(Field(result, "Ok"), Is.EqualTo(error == null));
            }

            internal void Reply(int code, string json) => Responses.Enqueue(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));
            internal void LoginReply(string state = EmptyState) => Reply(200, state.Insert(1, "\"Token\":\"" + fakeToken + "\","));
            internal Task Login()
            {
                LoginReply();
                return Expect("Authenticate", null, Name, Secret, false);
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string json = request.Content == null ? null : await request.Content.ReadAsStringAsync();
                bool credentials = request.RequestUri.AbsolutePath == "/v1/login" || request.RequestUri.AbsolutePath == "/v1/register";
                if (credentials)
                {
                    Assert.That(json.Contains("\"Password\":\"" + Secret + "\""), Is.True, "Credential field missing");
                    json = json.Replace(Secret, "<test>");
                }
                Requests.Add(new RecordedRequest { Path = request.RequestUri.AbsolutePath, Method = request.Method.Method, Json = json,
                    HasBearer = request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == fakeToken });
                return await Responses.Dequeue()(cancellationToken);
            }

            internal new void Dispose() => transport.Dispose();
        }
    }
}
