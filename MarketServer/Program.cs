using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.RateLimiting;
using Dustline.Market;

if (args is ["--help"])
{
    Console.WriteLine(ServerOptions.Usage);
    return 0;
}

try
{
    var options = ServerOptions.Parse(args);
    using var certificate = options.CertificatePath == null ? null : new X509Certificate2(
        options.CertificatePath, Environment.GetEnvironmentVariable("DUSTLINE_MARKET_CERT_PASSWORD"),
        X509KeyStorageFlags.EphemeralKeySet);
    if (certificate != null && !certificate.HasPrivateKey) throw new InvalidDataException("HTTPS requires a private key.");

    var catalog = Catalog.Load(options.CatalogPath);
    using var store = new StateStore(options.StatePath, catalog);
    var market = new Market(catalog, store);
    var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
    {
        Args = [], EnvironmentName = Environments.Production, ContentRootPath = AppContext.BaseDirectory
    });
    builder.Configuration.Sources.Clear();
    builder.Configuration.AddInMemoryCollection();
    builder.WebHost.PreferHostingUrls(false);
    builder.WebHost.UseKestrel(server =>
    {
        server.AddServerHeader = false;
        server.Limits.MaxRequestBodySize = 8192;
        server.Listen(options.Address, options.Port, endpoint =>
        {
            if (certificate != null) endpoint.UseHttps(certificate);
        });
    });
    builder.Services.AddRouting();
    builder.Services.AddRateLimiter(limits =>
    {
        limits.AddPolicy("credentials", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress ?? IPAddress.None, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));
        limits.OnRejected = async (context, cancellation) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            await context.HttpContext.Response.WriteAsJsonAsync(new ErrorResponse(false, "TooManyRequests"),
                MarketJson.Options, cancellation);
        };
    });

    await using var app = builder.Build();
    app.UseRouting();
    app.UseRateLimiter();
    app.MapPost("/v1/register", (HttpContext context) => Respond(context, async () => market.Register(await ReadRequest(context))))
        .RequireRateLimiting("credentials");
    app.MapPost("/v1/login", (HttpContext context) => Respond(context, async () => market.Login(await ReadRequest(context))))
        .RequireRateLimiting("credentials");
    app.MapGet("/v1/state", (HttpContext context) => Respond(context, () => ValueTask.FromResult(market.View(market.Authenticate(context)))));
    foreach (var operation in Enum.GetValues<Mutation>())
    {
        app.MapPost("/v1/" + operation.ToString().ToLowerInvariant(), (HttpContext context) => Respond(context, async () =>
        {
            var name = market.Authenticate(context);
            return market.Apply(name, operation, await ReadRequest(context));
        }));
    }
    await app.StartAsync();
    Console.WriteLine($"Listening: {app.Urls.Single()}");
    await app.WaitForShutdownAsync();
    return 0;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException or CryptographicException)
{
    Console.Error.WriteLine($"MarketServer: {ex.Message}");
    return 1;
}

static async Task<MarketRequest> ReadRequest(HttpContext context)
{
    if (!context.Request.HasJsonContentType()) throw new MarketError(StatusCodes.Status415UnsupportedMediaType, "ExpectedJson");
    using var json = await JsonDocument.ParseAsync(context.Request.Body, new JsonDocumentOptions { MaxDepth = MarketJson.Options.MaxDepth },
        context.RequestAborted);
    if (json.RootElement.ValueKind != JsonValueKind.Object) throw MarketError.Invalid("InvalidJson");
    var names = new HashSet<string>(StringComparer.Ordinal);
    foreach (var property in json.RootElement.EnumerateObject())
        if (!names.Add(property.Name)) throw MarketError.Invalid("InvalidJson");
    return json.RootElement.Deserialize<MarketRequest>(MarketJson.Options)!;
}

static async Task Respond(HttpContext context, Func<ValueTask<StateResponse>> action)
{
    context.Response.Headers.CacheControl = "no-store";
    IResult result;
    try { result = Results.Json(await action(), MarketJson.Options); }
    catch (MarketError ex)
    {
        if (ex.Status == StatusCodes.Status401Unauthorized) context.Response.Headers.WWWAuthenticate = "Bearer";
        result = Failure(ex.Status, ex.Message);
    }
    catch (JsonException) { result = Failure(StatusCodes.Status400BadRequest, "InvalidJson"); }
    catch (BadHttpRequestException ex) { result = Failure(ex.StatusCode, "InvalidRequest"); }
    catch (IOException) { result = Failure(StatusCodes.Status503ServiceUnavailable, "StateUnavailable"); }
    catch (UnauthorizedAccessException) { result = Failure(StatusCodes.Status503ServiceUnavailable, "StateUnavailable"); }
    await result.ExecuteAsync(context);

    static IResult Failure(int status, string error) => Results.Json(new ErrorResponse(false, error), MarketJson.Options, statusCode: status);
}

internal sealed record ServerOptions(string CatalogPath, string StatePath, IPAddress Address, int Port, string? CertificatePath)
{
    public const string Usage = "MarketServer --catalog PATH --state PATH [--listen IP] [--port PORT] [--https-pfx PATH]";

    public static ServerOptions Parse(string[] args)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            string flag = args[i];
            if (i + 1 == args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                flag is not ("--catalog" or "--state" or "--listen" or "--port" or "--https-pfx") || !fields.TryAdd(flag, args[i + 1]))
                throw new ArgumentException(Usage);
        }
        if (!fields.TryGetValue("--catalog", out string? catalog) || !fields.TryGetValue("--state", out string? state))
            throw new ArgumentException(Usage);
        if (!IPAddress.TryParse(fields.GetValueOrDefault("--listen", "127.0.0.1"), out var address) ||
            !int.TryParse(fields.GetValueOrDefault("--port", "5076"), NumberStyles.None, CultureInfo.InvariantCulture, out int port) ||
            port is < 0 or > 65535)
            throw new ArgumentException("Invalid listen IP or port.");
        var pfx = fields.GetValueOrDefault("--https-pfx");
        if (!IPAddress.IsLoopback(address) && pfx == null) throw new ArgumentException("Non-loopback endpoints require HTTPS (--https-pfx).");
        string catalogPath = Path.GetFullPath(catalog), statePath = Path.GetFullPath(state);
        if (string.Equals(catalogPath, statePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Catalog and state must have separate paths.");
        return new(catalogPath, statePath, address, port, pfx);
    }
}
