using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class NullNewsProviderTests
{
    [Fact]
    public async Task SearchAsync_AlwaysReturnsEmpty()
    {
        var p = new NullNewsProvider(NullLogger<NullNewsProvider>.Instance);

        var result = await p.SearchAsync("TSLA", new DateOnly(2020, 1, 15));

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}