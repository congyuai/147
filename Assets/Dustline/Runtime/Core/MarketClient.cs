using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Dustline
{
    [Serializable]
    internal sealed class MarketListing
    {
        public string Id, Seller;
        public long UnitPrice;
        public ItemStack Item;
    }

    [Serializable]
    internal sealed class MarketHistory
    {
        public string Id, ItemId, Status, Utc;
        public int Quantity;
        public long UnitPrice;
    }

    [Serializable]
    internal sealed class MarketReference
    {
        public string ItemId;
        public long UnitPrice;
    }

    [Serializable]
    internal sealed class MarketProduct
    {
        public string Id;
        public bool ForSale;
        public int MaxStack;
        public long Value, Resale;
    }

    [Serializable]
    internal sealed class MarketState
    {
        public bool Ok;
        public string Error, Token, Account;
        public long Credits;
        public ItemStack[] Items;
        public MarketListing[] Listings;
        public MarketHistory[] History;
        public MarketReference[] References;
        public MarketProduct[] Products;
    }

    internal enum MarketAction { List, Buy, Cancel, Shop, Recycle }

    internal readonly struct MarketResult
    {
        internal readonly bool Ok;
        internal readonly string Error;
        internal MarketResult(bool ok, string error = null) { Ok = ok; Error = error; }
    }

    internal sealed class MarketClient
    {
        [Serializable] sealed class LoginBody { public string Account, Password; }
        [Serializable] sealed class RegisterBody { public string Account, Password, RequestId; }
        [Serializable] sealed class ListBody { public string RequestId, InstanceId; public int Quantity; public long UnitPrice; }
        [Serializable] sealed class BuyBody { public string RequestId, ListingId; public int Quantity; }
        [Serializable] sealed class CancelBody { public string RequestId, ListingId; }
        [Serializable] sealed class ShopBody { public string RequestId, ItemId; public int Quantity; public long UnitPrice; }
        [Serializable] sealed class RecycleBody { public string RequestId, InstanceId; public int Quantity; public long UnitPrice; }

        sealed class PendingOrder
        {
            internal string Account, Path, Body;
            internal MarketAction Action;
        }

        const int MaxResponseBytes = 8 * 1024 * 1024;
        static readonly Lazy<HttpClient> SharedHttp = new Lazy<HttpClient>(() => CreateHttp(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false
        }));

        readonly HttpClient transport;
        readonly TimeSpan timeout;
        Uri endpoint;
        string token, account, registrationAccount, registrationId;
        PendingOrder pending;
        int inFlight;

        internal MarketClient() { timeout = TimeSpan.FromSeconds(10); }
        internal MarketClient(HttpClient transport, TimeSpan timeout) { this.transport = transport; this.timeout = timeout; }

        internal string Url => endpoint?.AbsoluteUri ?? "";
        internal string Account => account;
        internal bool SignedIn => token != null;
        internal MarketState State { get; private set; }
        internal string PendingAccount => pending?.Account;
        internal MarketAction? PendingAction => pending?.Action;
        bool InFlight => Volatile.Read(ref inFlight) != 0;

        internal static HttpClient CreateHttp(HttpMessageHandler handler) => new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = MaxResponseBytes
        };

        internal static bool TryEndpoint(string value, out Uri url)
        {
            url = null;
            value = value?.Trim();
            if (string.IsNullOrEmpty(value) || value.Length > 512) return false;
            int schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd < 0) return false;
            int slash = value.IndexOf('/', schemeEnd + 3);
            if (slash >= 0 && slash != value.Length - 1) return false;
            foreach (char c in value)
                if (char.IsControl(c) || char.IsWhiteSpace(c) || "\\?#@%".IndexOf(c) >= 0) return false;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) || candidate.HostNameType < UriHostNameType.Dns
                || !string.IsNullOrEmpty(candidate.UserInfo) || candidate.AbsolutePath != "/" || candidate.Port <= 0) return false;
            if (candidate.Scheme == Uri.UriSchemeHttp)
            {
                string host = candidate.Host.Trim('[', ']');
                if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    // Don't resolve a hostname for cleartext traffic.
                    candidate = new UriBuilder(candidate) { Host = "127.0.0.1" }.Uri;
                }
                else if (!IPAddress.TryParse(host, out var ip) || !IPAddress.IsLoopback(ip)) return false;
            }
            else if (candidate.Scheme != Uri.UriSchemeHttps) return false;
            url = candidate;
            return true;
        }

        internal bool SetEndpoint(string value)
        {
            if (!TryEndpoint(value, out var url)) return false;
            if (endpoint == url) return true;
            if (InFlight || pending != null || SignedIn) return false;
            endpoint = url;
            registrationAccount = registrationId = null;
            return true;
        }

        internal bool SignOut()
        {
            if (InFlight || pending != null) return false;
            ClearSession();
            registrationAccount = registrationId = null;
            return true;
        }

        internal async Task<MarketResult> Authenticate(string name, string password, bool register)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(password)) return new MarketResult(false, "CredentialsRequired");
            if (SignedIn || (pending != null && (register || !SameAccount(name, pending.Account))))
                return new MarketResult(false, "AccountLocked");
            if (endpoint == null) return new MarketResult(false, "InvalidUrl");
            if (!Begin()) return new MarketResult(false, "Busy");
            try
            {
                if (register && !SameAccount(name, registrationAccount))
                {
                    registrationAccount = name;
                    registrationId = NewRequestId();
                }
                string body = register
                    ? JsonUtility.ToJson(new RegisterBody { Account = name, Password = password, RequestId = registrationId })
                    : JsonUtility.ToJson(new LoginBody { Account = name, Password = password });
                password = null;
                var reply = await Send(register ? "register" : "login", body, null);
                body = null;
                if (!reply.Result.Ok)
                {
                    if (register && reply.Definitive) registrationAccount = registrationId = null;
                    return reply.Result;
                }
                var data = reply.State;
                if (!SameAccount(data.Account, name) || (pending != null && !SameAccount(data.Account, pending.Account)))
                    return new MarketResult(false, "InvalidReply");
                if (register && string.IsNullOrEmpty(data.Token))
                {
                    registrationAccount = registrationId = null;
                    return reply.Result;
                }
                if (!ValidToken(data.Token) || !IsSnapshot(data)) return new MarketResult(false, "InvalidReply");

                token = data.Token;
                data.Token = null;
                account = data.Account;
                State = data;
                registrationAccount = registrationId = null;
                return reply.Result;
            }
            finally { End(); }
        }

        internal async Task<MarketResult> Refresh()
        {
            if (!SignedIn) return new MarketResult(false, "SignInRequired");
            if (!Begin()) return new MarketResult(false, "Busy");
            try
            {
                var reply = await Send("state", null, token);
                if (reply.Unauthorized) ClearSession();
                if (!reply.Result.Ok) return reply.Result;
                return AcceptSnapshot(reply.State) ? reply.Result : new MarketResult(false, "InvalidReply");
            }
            finally { End(); }
        }

        internal async Task<MarketResult> Trade(MarketAction action, string id, int quantity = 0, long unitPrice = -1)
        {
            if (!SignedIn) return new MarketResult(false, "SignInRequired");
            if (pending != null) return new MarketResult(false, "RetryRequired");
            if (State == null) return new MarketResult(false, "RefreshRequired");
            bool priced = action == MarketAction.List || action == MarketAction.Shop || action == MarketAction.Recycle;
            if (string.IsNullOrWhiteSpace(id) || (action != MarketAction.Cancel && quantity <= 0)
                || (priced && (unitPrice < 0 || (action == MarketAction.List && unitPrice == 0) || unitPrice > long.MaxValue / quantity)))
                return new MarketResult(false, "InvalidOrder");
            if (!Begin()) return new MarketResult(false, "Busy");
            try
            {
                string requestId = NewRequestId();
                string path, body;
                switch (action)
                {
                    case MarketAction.List:
                        path = "list";
                        body = JsonUtility.ToJson(new ListBody { RequestId = requestId, InstanceId = id, Quantity = quantity, UnitPrice = unitPrice });
                        break;
                    case MarketAction.Buy:
                        path = "buy";
                        body = JsonUtility.ToJson(new BuyBody { RequestId = requestId, ListingId = id, Quantity = quantity });
                        break;
                    case MarketAction.Cancel:
                        path = "cancel";
                        body = JsonUtility.ToJson(new CancelBody { RequestId = requestId, ListingId = id });
                        break;
                    case MarketAction.Shop:
                        path = "shop";
                        body = JsonUtility.ToJson(new ShopBody { RequestId = requestId, ItemId = id, Quantity = quantity, UnitPrice = unitPrice });
                        break;
                    case MarketAction.Recycle:
                        path = "recycle";
                        body = JsonUtility.ToJson(new RecycleBody { RequestId = requestId, InstanceId = id, Quantity = quantity, UnitPrice = unitPrice });
                        break;
                    default: return new MarketResult(false, "InvalidOrder");
                }
                pending = new PendingOrder { Account = account, Path = path, Body = body, Action = action };
                return await SendOrder();
            }
            finally { End(); }
        }

        internal async Task<MarketResult> Retry()
        {
            if (!SignedIn) return new MarketResult(false, "SignInRequired");
            if (pending == null || !SameAccount(account, pending.Account)) return new MarketResult(false, "AccountLocked");
            if (!Begin()) return new MarketResult(false, "Busy");
            try { return await SendOrder(); }
            finally { End(); }
        }

        async Task<MarketResult> SendOrder()
        {
            var reply = await Send(pending.Path, pending.Body, token);
            if (reply.Unauthorized) ClearSession();
            if (reply.Result.Ok)
            {
                if (!AcceptSnapshot(reply.State)) return new MarketResult(false, "InvalidReply");
                pending = null;
            }
            else if (reply.Definitive)
            {
                pending = null;
                State = null;
            }
            return reply.Result;
        }

        bool AcceptSnapshot(MarketState data)
        {
            data.Token = null;
            if (!SameAccount(data.Account, account) || !IsSnapshot(data)) return false;
            State = data;
            return true;
        }

        static bool IsSnapshot(MarketState data)
        {
            if (data.Credits < 0 || data.Items == null || data.Listings == null || data.History == null
                || data.References == null || data.Products == null || data.Products.Length == 0) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var product in data.Products)
                if (product == null || string.IsNullOrWhiteSpace(product.Id) || product.MaxStack < 1
                    || product.Value < 0 || product.Resale < 0 || !ids.Add(product.Id)) return false;
            return true;
        }

        void ClearSession() { State = null; token = account = null; }
        bool Begin() => Interlocked.CompareExchange(ref inFlight, 1, 0) == 0;
        void End() => Volatile.Write(ref inFlight, 0);
        static bool SameAccount(string a, string b) => a != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        static string NewRequestId() => Guid.NewGuid().ToString("N");

        static bool ValidToken(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 1024) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || "-._~+/=".IndexOf(c) >= 0)) return false;
            return true;
        }

        async Task<(MarketResult Result, MarketState State, bool Definitive, bool Unauthorized)> Send(string route, string body, string bearer)
        {
            using var stop = new CancellationTokenSource();
            try
            {
                var deadline = Task.Delay(timeout, stop.Token);
                var exchange = Exchange(new Uri(endpoint, "v1/" + route), body, bearer, stop.Token);
                if (await Task.WhenAny(exchange, deadline).ConfigureAwait(false) != exchange)
                    return (new MarketResult(false, "Timeout"), null, false, false);
                var (status, json) = await exchange.ConfigureAwait(false);
                if (status == 0) return (new MarketResult(false, "ConnectionFailed"), null, false, false);
                if (status == 429) return (new MarketResult(false, "TooManyRequests"), null, false, false);
                if (status == 401 && bearer != null) return (new MarketResult(false, "SignInRequired"), null, false, true);
                if (status >= 300 && status <= 399) return (new MarketResult(false, "Redirect"), null, false, false);
                if (status >= 500) return (new MarketResult(false, "ServiceUnavailable"), null, false, false);

                var data = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<MarketState>(json);
                if (status >= 200 && status <= 299 && data?.Ok == true)
                    return (new MarketResult(true), data, true, false);
                // A protocol rejection is definitive; a gateway failure may have happened after commit.
                if (data?.Ok == false && !string.IsNullOrWhiteSpace(data.Error)
                    && (status >= 200 && status <= 299 || status >= 400 && status <= 499 && status != 408 && status != 429))
                    return (new MarketResult(false, data.Error), null, true, false);
                return (new MarketResult(false, "InvalidReply"), null, false, false);
            }
            catch (ArgumentException) { return (new MarketResult(false, "InvalidReply"), null, false, false); }
            finally { stop.Cancel(); }
        }

        async Task<(int Status, string Json)> Exchange(Uri url, string body, string bearer, CancellationToken stop)
        {
            try
            {
                using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, url);
                if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await (transport ?? SharedHttp.Value).SendAsync(request, HttpCompletionOption.ResponseContentRead, stop).ConfigureAwait(false);
                return ((int)response.StatusCode, response.Content == null ? null : await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is FormatException
                || e is InvalidOperationException || e is NotSupportedException || e is System.IO.IOException
                || e is WebException || e is System.Security.Authentication.AuthenticationException)
            {
                // Host/handler errors can contain request details. Surface only a fixed client code.
                return (0, null);
            }
        }
    }
}
