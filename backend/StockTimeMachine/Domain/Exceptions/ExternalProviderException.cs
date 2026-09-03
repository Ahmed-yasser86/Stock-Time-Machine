namespace StockTimeMachine;

public class ExternalProviderException : Exception
{
    public ExternalProviderException(string message) : base(message) { }
    public ExternalProviderException(string message, Exception inner) : base(message, inner) { }
}
