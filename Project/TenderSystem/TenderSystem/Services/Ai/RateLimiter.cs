namespace TenderSystem.Services.Ai;

/// <summary>
/// "Not more than N times inside a time window, for each user." One small class used by every AI feature that needs a speed limit
/// (chat, voice typing, web mode, offer advisor, tender summary, Admin commands).
/// Services live for one request only, so keep the limiter in a <c>static readonly</c> field.
/// </summary>
public sealed class RateLimiter
{
    // key (usually the user id) -> the times of that user's latest uses
    private readonly Dictionary<string, Queue<DateTime>> _uses = new();

    /// <summary>Uses one slot and returns true, or returns false when <paramref name="max"/> uses already happened inside the window.</summary>
    public bool TryTake(string? key, int max, TimeSpan window) => TryTakeMany(key, 1, max, window);

    /// <summary>Uses <paramref name="count"/> slots at once, all or nothing.</summary>
    public bool TryTakeMany(string? key, int count, int max, TimeSpan window)
    {
        lock (_uses)
        {
            var now = DateTime.UtcNow;
            if (!_uses.TryGetValue(key ?? "", out var times))
            {
                times = new Queue<DateTime>();
                _uses[key ?? ""] = times;
            }

            while (times.Count > 0 && now - times.Peek() > window) times.Dequeue();
            if (times.Count + count > max) return false;

            for (var i = 0; i < count; i++) times.Enqueue(now);
            return true;
        }
    }
}
