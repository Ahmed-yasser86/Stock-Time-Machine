namespace StockTimeMachine;

// Map-reduce for the 32k-token Flash input ceiling. Today's per-thread caps
// (≈15k chars worst case) fit one pass, so the reducer is dormant but real:
// if caps or threads grow past MaxPromptChars, articles split into batches,
// each batch is summarized separately, and the batch summaries are synthesized
// by one final call. Article numbering stays GLOBAL across batches so [n]
// citations survive the reduce step. Pure and unit-tested.
public static class BriefBatcher
{
    // ~30k tokens at ~4 chars/token, headroom below the 32k ceiling.
    public const int MaxPromptChars = 120000;

    public static List<List<(string Title, string Body)>> Batch(
        IReadOnlyList<(string Title, string Body)> inputs, int maxBatchChars = MaxPromptChars)
    {
        var batches = new List<List<(string Title, string Body)>> { new() };
        int current = 0;
        foreach (var item in inputs)
        {
            int size = item.Title.Length + item.Body.Length;
            if (current > 0 && current + size > maxBatchChars)
            {
                batches.Add(new List<(string Title, string Body)>());
                current = 0;
            }
            batches[^1].Add(item);
            current += size;
        }
        return batches;
    }
}
