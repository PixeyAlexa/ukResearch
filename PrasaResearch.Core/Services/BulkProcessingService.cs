using PrasaResarch.Data;
using PrasaResarch.Models;

namespace PrasaResarch.Services;
public class BulkProcessingService(ICompanyProcessor processor, JobPersistenceService persistence)
{
    private readonly SemaphoreSlim runGate = new(1);
    private int active;
    public int ActiveCount => Volatile.Read(ref active);
    public event Action<int>? ActiveCountChanged;
    private volatile TaskCompletionSource resume = Completed();
    public bool IsPaused { get; private set; }
    private static TaskCompletionSource Completed() { var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); t.SetResult(); return t; }
    public void Pause() { if (IsPaused) return; resume = new(TaskCreationOptions.RunContinuationsAsynchronously); IsPaused = true; }
    public void Resume() { IsPaused = false; resume.TrySetResult(); }
    public async Task RunAsync(IEnumerable<string> numbers, int concurrency, IProgress<CompanyResult> progress, CancellationToken ct)
    {
        if (!await runGate.WaitAsync(0, ct)) throw new InvalidOperationException("A processing job is already running.");
        var source = processor as IProcessingProgressSource;
        void ReportPartial(CompanyResult result)
        {
            if (result.Stage is "Finding latest accounts filing" or "Checking available document formats") persistence.Save(result);
            progress.Report(result);
        }
        if (source != null) source.ResultUpdated += ReportPartial;
        try
        {
            Resume();
            await Parallel.ForEachAsync(numbers.Distinct(), new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = ct }, async (number, token) =>
            {
                await resume.Task.WaitAsync(token);
                ActiveCountChanged?.Invoke(Interlocked.Increment(ref active));
                try
                {
                    var result = await processor.ProcessAsync(number, token);
                    // Commit before reporting so a crash cannot lose a completed item.
                    persistence.Save(result); progress.Report(result);
                }
                finally { ActiveCountChanged?.Invoke(Interlocked.Decrement(ref active)); }
            });
        }
        finally { if (source != null) source.ResultUpdated -= ReportPartial; runGate.Release(); }
    }
}
