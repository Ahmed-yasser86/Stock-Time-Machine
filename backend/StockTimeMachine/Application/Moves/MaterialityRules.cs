using System.Text.RegularExpressions;

namespace StockTimeMachine;

// Deterministic materiality fallback for the shared relevance gate. This is
// NOT a second filter and NOT a UI concern: RelevanceService invokes it when
// the AI classifier is unavailable (disabled/failed), so the gate always
// produces real verdicts instead of passing unknowns through silently.
//
// Conservative by design, mirroring the AI prompt's policy:
// - A company mention alone NEVER makes an article relevant; it must co-occur
//   with a material business signal (financial, corporate-action, management,
//   labor/operations, legal, regulatory, product).
// - No company mention + only generic vocabulary → IRRELEVANT, even when the
//   provider associated the article with the symbol (entity match ≠ relevance).
// - Anything plausibly material but undecidable deterministically → UNCERTAIN,
//   which the gate excludes from admitted evidence (surfaced for user review).
// AI verdicts always supersede RULE verdicts; USER verdicts are final.
public static class MaterialityRules
{
    public sealed record RuleVerdict(
        bool? Relevant, string Decision, string Category, double Confidence, string Reason);

    // Material business signals grouped by relevance category. Whole-word
    // matched (regex \b) against title + description. Deliberately tight:
    // weak signals ("deal", "partnership", "release", "review", "probe",
    // "investigation") are excluded — they fire on entertainment coverage
    // (content deals, true-crime documentaries) far more often than on
    // company-material events.
    private static readonly (string Category, string[] Terms)[] MaterialGroups = new[]
    {
        ("FINANCIAL", new[]
        {
            "earnings", "revenue", "revenues", "profit", "profits", "profitable", "loss", "losses",
            "margin", "margins", "eps", "guidance", "forecast", "outlook", "subscriber", "subscribers",
            "subscription", "subscriptions", "churn", "dividend", "dividends", "buyback", "buy back",
            "stock split", "downgrade", "downgrades", "downgraded", "upgrade", "upgrades", "upgraded",
            "price target", "quarterly results", "record quarter", "sales", "growth", "beats expectations",
            "misses expectations", "shortfall", "write-down", "writedown", "impairment",
        }),
        ("STRATEGY", new[]
        {
            "merger", "mergers", "acquisition", "acquisitions", "acquire", "acquires", "acquired",
            "acquiring", "takeover", "take over", "spinoff", "spin-off", "divest", "divestiture",
            "divests", "joint venture", "buyout", "merges with", "to acquire",
        }),
        ("MANAGEMENT", new[]
        {
            "ceo", "cfo", "cto", "coo", "chief executive", "chief financial", "chairman",
            "chairwoman", "chairperson", "founder", "co-founder", "co founder", "board of directors",
            "steps down", "step down", "stepping down", "resign", "resigns", "resigned", "resignation",
            "appoint", "appoints", "appointed", "oust", "ousts", "ousted", "succeeds as ceo",
        }),
        ("OPERATIONS", new[]
        {
            "layoff", "layoffs", "lay off", "lays off", "laid off", "job cuts", "cutting jobs",
            "strike", "strikes", "striking", "union", "unionize", "unionized", "hiring freeze",
            "hiring pause", "outage", "outages", "data breach", "breach", "hack", "hacked",
            "hacking", "cyberattack", "cyber attack", "ransomware",
        }),
        ("LEGAL", new[]
        {
            "lawsuit", "lawsuits", "sue", "sues", "sued", "suing", "settlement", "settlements",
            "settle", "settles", "settled", "court", "judge", "jury", "trial", "verdict",
            "fraud", "defraud", "defrauds", "defrauded", "embezzle", "embezzlement", "indictment",
            "indicted", "charged", "charges", "fine", "fined", "fines", "penalty", "penalties",
            "class action", "defamation", "libel", "slander",
        }),
        ("REGULATORY", new[]
        {
            "regulation", "regulations", "regulator", "regulators", "regulatory", "fcc", "ftc",
            "doj", "antitrust", "senate", "congress", "parliament", "lawmaker", "lawmakers",
            "ban", "bans", "banned", "banning", "tariff", "tariffs", "compliance", "consent decree",
        }),
        ("PRODUCT", new[]
        {
            "launch", "launches", "launched", "launching", "unveil", "unveils", "unveiled",
            "platform", "price hike", "price cut", "raises prices", "raise prices", "price increase",
            "ad-supported", "ad tier", "password sharing", "new service",
            "service tier", "tiers",
        }),
    };

    // Industry/macro vocabulary for the no-mention path: enough to keep a
    // possibly-contextual article UNCERTAIN instead of IRRELEVANT, never
    // enough to admit it without semantic review.
    private static readonly string[] IndustryTerms = new[]
    {
        "streaming", "streamer", "streamers", "cord-cutting", "cord cutting", "cord cutter",
        "pay-tv", "pay tv", "video on demand", "video-on-demand", "svod", "avod",
    };

    private static readonly string[] MacroTerms = new[]
    {
        "recession", "inflation", "federal reserve", "interest rate", "interest rates",
        "rate hike", "rate cut", "gdp", "unemployment", "trade war", "stimulus",
    };

    // Name words too generic to identify a company on their own.
    private static readonly ISet<string> NameStoplicht = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "inc", "incorporated", "corp", "corporation", "company", "co", "ltd", "limited",
        "holdings", "group", "the",
    };

    public static RuleVerdict Judge(string symbol, string? companyName, string title, string description)
    {
        var text = $"{title ?? ""}\n{description ?? ""}";
        var mentioned = MentionsCompany(text, symbol, companyName);
        var material = MatchGroup(text);
        if (mentioned && material is not null)
            return new RuleVerdict(true, RelevanceDecisions.Relevant, material.Value.Category, 0.60,
                $"Company mentioned with a material {material.Value.Category.ToLowerInvariant()} signal (\"{material.Value.Term}\"). Rule fallback; AI review supersedes.");
        if (mentioned)
            return new RuleVerdict(null, RelevanceDecisions.Uncertain, "UNRELATED", 0.40,
                "Company mentioned without a material business signal; needs semantic review. Excluded until admitted.");
        if (MatchAny(text, IndustryTerms) is not null || MatchAny(text, MacroTerms) is not null || material is not null)
            return new RuleVerdict(null, RelevanceDecisions.Uncertain, "UNRELATED", 0.45,
                "No company mention; possible industry/macro connection needs semantic review. Excluded until admitted.");
        return new RuleVerdict(false, RelevanceDecisions.Irrelevant, "UNRELATED", 0.70,
            "No company mention and no material/industry signal; provider association alone is not relevance.");
    }

    private static bool MentionsCompany(string text, string symbol, string? companyName)
    {
        var normalized = (symbol ?? "").Trim();
        if (normalized.Length > 0 &&
            Regex.IsMatch(text, @"\b" + Regex.Escape(normalized) + @"\b", RegexOptions.IgnoreCase))
            return true;
        var name = (companyName ?? "").Trim();
        if (name.Length == 0)
            return false;
        if (text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        // Significant name words ("Paramount" in "Paramount Global"): at least
        // 5 letters, stoplist-filtered, whole-word matched.
        foreach (Match m in Regex.Matches(name, @"[A-Za-z][A-Za-z.'&\-]*"))
        {
            var word = m.Value.Trim('.', '\'', '&', '-');
            if (word.Length < 5 || NameStoplicht.Contains(word))
                continue;
            if (Regex.IsMatch(text, @"\b" + Regex.Escape(word) + @"\b", RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }

    private static (string Category, string Term)? MatchGroup(string text)
    {
        foreach (var (category, terms) in MaterialGroups)
        {
            var term = MatchAny(text, terms);
            if (term is not null)
                return (category, term);
        }
        return null;
    }

    private static string? MatchAny(string text, string[] terms)
    {
        foreach (var term in terms)
        {
            if (Regex.IsMatch(text, @"\b" + Regex.Escape(term) + @"\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return term;
        }
        return null;
    }
}
