using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.IntegrationTests.Fixtures;

public sealed class ConcurrentRefreshInterceptor : SaveChangesInterceptor
{
    private TaskCompletionSource? _barrier;
    private int _arrivals;

    public void Arm()
    {
        _arrivals = 0;
        _barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Disarm()
    {
        _barrier = null;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource? barrier = _barrier;
        if (barrier is not null && eventData.Context!.ChangeTracker.Entries<RefreshSession>()
            .Any(entry => entry.State == EntityState.Modified))
        {
            // Force both requests to read the same token before either commits.
            if (Interlocked.Increment(ref _arrivals) == 2)
            {
                barrier.TrySetResult();
            }

            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }

        return result;
    }
}
