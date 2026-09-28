namespace ObsLiveBot.Infrastructure.Obs;

public interface IReconnectDelay
{
    TimeSpan GetDelay(int attempt);

    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class ProgressiveReconnectDelay : IReconnectDelay
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    public TimeSpan GetDelay(int attempt)
    {
        var index = Math.Clamp(attempt - 1, 0, Delays.Length - 1);
        return Delays[index];
    }

    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
