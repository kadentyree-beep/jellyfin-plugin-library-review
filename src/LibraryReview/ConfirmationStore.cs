namespace LibraryReview;

public record PendingReplacement(ReplacementPreview Preview, Fingerprint File, RadarrMatch Match, string ConfigStamp);

/// <summary>Short-lived, process-local tickets. Restart invalidates outstanding previews.</summary>
public sealed class ConfirmationStore
{
    private readonly Dictionary<string, PendingReplacement> _tickets = [];
    private readonly object _lock = new();
    public void Add(PendingReplacement pending)
    {
        lock (_lock)
        {
            foreach (var key in _tickets.Where(x => x.Value.Preview.Expires < DateTimeOffset.UtcNow).Select(x => x.Key).ToList()) _tickets.Remove(key);
            _tickets[pending.Preview.Token] = pending;
        }
    }
    public PendingReplacement Consume(Guid id, ConfirmRequest request, string configStamp, bool enabled)
    {
        lock (_lock)
        {
            if (!enabled || !_tickets.Remove(request.Token, out var pending)
                || pending.Preview.ItemId != id || pending.Preview.Expires < DateTimeOffset.UtcNow
                || pending.Preview.Title != request.ConfirmTitle || pending.ConfigStamp != configStamp)
                throw new InvalidOperationException("Confirmation expired, already used, or does not match. Prepare a new preview.");
            return pending;
        }
    }
}
