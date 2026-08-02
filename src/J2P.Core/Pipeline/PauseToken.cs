namespace J2P.Core.Pipeline;

/// <summary>一時停止/再開を通知するトークンのソース。</summary>
public sealed class PauseTokenSource
{
    private volatile TaskCompletionSource<bool>? _paused;

    public bool IsPaused => _paused is not null;

    public PauseToken Token => new(this);

    public void Pause()
    {
        if (_paused is null)
            Interlocked.CompareExchange(ref _paused, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously), null);
    }

    public void Resume()
    {
        var tcs = Interlocked.Exchange(ref _paused, null);
        tcs?.TrySetResult(true);
    }

    internal async Task WaitWhilePausedAsync(CancellationToken ct)
    {
        while (true)
        {
            var tcs = _paused;
            if (tcs is null) return;
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            await tcs.Task.ConfigureAwait(false);
        }
    }
}

/// <summary>一時停止トークン。</summary>
public readonly struct PauseToken
{
    private readonly PauseTokenSource? _source;

    internal PauseToken(PauseTokenSource source) => _source = source;

    public bool IsPaused => _source?.IsPaused ?? false;

    public Task WaitWhilePausedAsync(CancellationToken ct) =>
        _source?.WaitWhilePausedAsync(ct) ?? Task.CompletedTask;
}
