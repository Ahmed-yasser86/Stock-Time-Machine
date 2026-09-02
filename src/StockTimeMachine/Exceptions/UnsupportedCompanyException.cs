namespace StockTimeMachine.Exceptions;

public class UnsupportedCompanyException : Exception
{
    public UnsupportedCompanyException(string message) : base(message) { }
}
