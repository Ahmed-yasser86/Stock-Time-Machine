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
    private readonly List<(Func<HttpRequestMessage, bool> Match, HttpStatusCode Status, string Body)> _routes = new();
    public int Calls { get; private set; }

    public RoutedHttpMessageHandler When(Func<HttpRequestMessage, bool> match, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes.Add((match, status, body));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        foreach (var (match, status, body) in _routes)
        {
            if (match(request))
                return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
    }
}
