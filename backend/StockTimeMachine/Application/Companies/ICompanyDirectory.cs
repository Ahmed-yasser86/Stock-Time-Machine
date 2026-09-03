namespace StockTimeMachine;

public interface ICompanyDirectory
{
    bool TryGet(string symbol, out CompanyInfo? company);
    bool TryGetCik(string symbol, out string cik);
    IReadOnlyList<CompanyInfo> Search(string query, int maxResults = 10);
    IReadOnlyList<CompanyInfo> All();
}