
namespace StockTimeMachine;

// Resolves the user-selected news provider. One source per investigation;
// a failure in the selected source yields an honest empty state, never a
// silent switch to the other provider.
public interface INewsProviderFactory
{
    INewsProvider Get(string? source);
    string DefaultSource { get; }
}
