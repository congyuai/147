using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

internal sealed class IntegrationHost(string dotnet, string server)
{
    public async Task<RunningServer> Start(string catalog, string state, string? pfx = null, string? password = null)
    {
        Process process = Launch(catalog, state, pfx == null ? [] : ["--https-pfx", pfx], password);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            const string prefix = "Listening: ";
            string? ready;
            do { ready = await process.StandardOutput.ReadLineAsync(timeout.Token); }
            while (ready != null && !ready.StartsWith(prefix, StringComparison.Ordinal));
            if (ready == null)
                throw new InvalidOperationException("Server not ready: " + await process.StandardError.ReadToEndAsync(timeout.Token));
            var endpoint = new Uri(ready[prefix.Length..]);
            if (!endpoint.IsLoopback || endpoint.Host != "127.0.0.1" || endpoint.Scheme != (pfx == null ? "http" : "https"))
                throw new InvalidOperationException("Unexpected server binding.");
            return new RunningServer(process, endpoint);
        }
        catch
        {
            await RunningServer.Stop(process);
            throw;
        }
    }

    public async Task MustReject(string catalog, string state, string expected, params string[] options)
    {
        Process process = Launch(catalog, state, options, null);
        try
        {
            string output = await process.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10));
            string error = await process.StandardError.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (output.Contains("Listening: ", StringComparison.Ordinal) || process.ExitCode == 0 || !error.Contains(expected, StringComparison.Ordinal))
                throw new InvalidOperationException("Expected startup rejection: " + expected + "; got " + output + error);
        }
        finally { await RunningServer.Stop(process); }
    }

    private Process Launch(string catalog, string state, string[] options, string? password)
    {
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { server, "--catalog", catalog, "--state", state, "--port", "0" }.Concat(options))
            start.ArgumentList.Add(arg);
        start.Environment.Remove("DUSTLINE_MARKET_CERT_PASSWORD");
        if (password != null) start.Environment["DUSTLINE_MARKET_CERT_PASSWORD"] = password;
        return Process.Start(start) ?? throw new InvalidOperationException("Could not launch server.");
    }
}

internal sealed class RunningServer(Process process, Uri address) : IAsyncDisposable
{
    public Api Client(string? thumbprint = null)
    {
        var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
        if (thumbprint != null)
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate?.Thumbprint == thumbprint;
        return new Api(new HttpClient(handler) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(20) });
    }

    public ValueTask DisposeAsync() => new(Stop(process));
    public static async Task Stop(Process process)
    {
        using (process)
        {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}

internal sealed record ApiReply(HttpStatusCode Status, JsonElement Json)
{
    public bool Ok => Json.GetProperty("Ok").GetBoolean();
    public string Error => Json.GetProperty("Error").GetString()!;

    public JsonElement Success(string operation)
    {
        if (Status != HttpStatusCode.OK || !Ok || Error != "")
            throw new InvalidOperationException($"{operation}: expected success; got {(int)Status} ({Error}).");
        return Json;
    }

    public void Reject(int status, string error)
    {
        if ((int)Status != status || Ok || Error != error || Json.EnumerateObject().Count() != 2)
            throw new InvalidOperationException($"Expected {status} ({error}); got {(int)Status} ({Error}).");
    }
}

internal sealed class Api(HttpClient client) : IDisposable
{
    public async Task<ApiReply> Send(string endpoint, object? body = null, string? token = null)
    {
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, "/v1/" + endpoint);
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) request.Content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        await using var content = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(content);
        return new ApiReply(response.StatusCode, json.RootElement.Clone());
    }

    public async Task<JsonElement> Good(string endpoint, object? body = null, string? token = null) =>
        (await Send(endpoint, body, token)).Success(endpoint);

    public void Dispose() => client.Dispose();
}
