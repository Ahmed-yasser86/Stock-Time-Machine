using System.Net;

namespace StockTimeMachine.Tests;

// Canned HTTP handler so provider tests never touch the network.
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public StubHttpMessageHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _body = body;
        _status = status;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body)
        });
    }
}

// URL-routed canned handler for multi-call provider flows (resolve → fetch).
public sealed class RoutedHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, bool> Match, HttpStatusCode Status, string Body, Dictionary<string, string>? Headers)> _routes = new();
    public int Calls { get; private set; }

    public RoutedHttpMessageHandler When(Func<HttpRequestMessage, bool> match, string body, HttpStatusCode status = HttpStatusCode.OK, Dictionary<string, string>? headers = null)
    {
        _routes.Add((match, status, body, headers));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        foreach (var (match, status, body, headers) in _routes)
        {
            if (match(request))
            {
                var resp = new HttpResponseMessage(status) { Content = new StringContent(body) };
                if (headers is not null)
                    foreach (var (k, v) in headers)
                        resp.Headers.TryAddWithoutValidation(k, v);
                return Task.FromResult(resp);
            }
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
    }
}
