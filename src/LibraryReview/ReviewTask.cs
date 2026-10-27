using MediaBrowser.Model.Tasks;

namespace LibraryReview;

public sealed class ReviewTask(ReviewService service) : IScheduledTask
{
    public string Name => "Scan Library Review";
    public string Key => "LibraryReviewScan";
    public string Description => "Read-only checks of movie metadata; no automatic replacement or deletion.";
    public string Category => "Library Review";
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
        [new() { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromMinutes(15).Ticks }];
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);
        await service.Scan(cancellationToken);
        progress.Report(100);
    }
}
