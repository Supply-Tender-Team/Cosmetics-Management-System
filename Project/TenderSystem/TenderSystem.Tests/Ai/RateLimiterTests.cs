using TenderSystem.Services.Ai;

namespace TenderSystem.Tests.Ai;

public class RateLimiterTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public void Allows_up_to_the_maximum_then_refuses()
    {
        var limiter = new RateLimiter();
        Assert.True(limiter.TryTake("u1", 2, Minute));
        Assert.True(limiter.TryTake("u1", 2, Minute));
        Assert.False(limiter.TryTake("u1", 2, Minute));
    }

    [Fact]
    public void Each_user_has_their_own_count()
    {
        var limiter = new RateLimiter();
        Assert.True(limiter.TryTake("u1", 1, Minute));
        Assert.False(limiter.TryTake("u1", 1, Minute));
        Assert.True(limiter.TryTake("u2", 1, Minute));
    }

    [Fact]
    public void Old_uses_leave_the_window()
    {
        var limiter = new RateLimiter();
        Assert.True(limiter.TryTake("u1", 1, TimeSpan.FromMilliseconds(30)));
        Thread.Sleep(80);
        Assert.True(limiter.TryTake("u1", 1, TimeSpan.FromMilliseconds(30)));
    }

    [Fact]
    public void Taking_many_is_all_or_nothing()
    {
        var limiter = new RateLimiter();
        Assert.True(limiter.TryTakeMany("u1", 3, 5, Minute));
        Assert.False(limiter.TryTakeMany("u1", 3, 5, Minute));    // would be 6 of 5: nothing is taken
        Assert.True(limiter.TryTakeMany("u1", 2, 5, Minute));     // the 3 already used leave room for exactly 2
        Assert.False(limiter.TryTake("u1", 5, Minute));
    }

    [Fact]
    public void A_missing_key_counts_as_one_shared_user()
    {
        var limiter = new RateLimiter();
        Assert.True(limiter.TryTake(null, 1, Minute));
        Assert.False(limiter.TryTake("", 1, Minute));
    }
}
