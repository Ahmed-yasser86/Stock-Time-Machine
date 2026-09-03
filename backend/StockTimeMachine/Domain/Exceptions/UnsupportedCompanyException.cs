namespace StockTimeMachine;

public class UnsupportedCompanyException : Exception
{
    public UnsupportedCompanyException(string message) : base(message) { }
}
