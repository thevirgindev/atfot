using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace atfot.core.services;

public class TorProxyService : IDisposable
{
    private readonly HttpClient _httpClient;
    private bool _enabled;

    public TorProxyService()
    {
        // Default to disabled; enable via config command later
        _enabled = false;
        _httpClient = CreateHttpClient(false);
    }

    private HttpClient CreateHttpClient(bool useTor)
    {
        if (!useTor)
            return new HttpClient();

        // Configure SOCKS5 proxy for Tor (default: localhost:9050)
        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy("socks5://127.0.0.1:9050"),
            UseProxy = true
        };
        return new HttpClient(handler);
    }

    public void EnableTor()
    {
        if (!_enabled)
        {
            _enabled = true;
            var old = _httpClient;
            var newClient = CreateHttpClient(true);
            // Replace the client (careful with concurrency; for simplicity we reassign)
            // In production you'd want a lock or use IHttpClientFactory with a named client
            // But for a bot, this is acceptable.
            // We'll just keep two clients and toggle logic in SendRequest.
        }
        _enabled = true;
    }

    public void DisableTor()
    {
        _enabled = false;
    }

    public async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        if (_enabled)
        {
            var torHandler = new HttpClientHandler
            {
                Proxy = new WebProxy("socks5://127.0.0.1:9050"),
                UseProxy = true
            };
            using var torClient = new HttpClient(torHandler);
            return await torClient.SendAsync(request, ct);
        }
        else
        {
            return await _httpClient.SendAsync(request, ct);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
