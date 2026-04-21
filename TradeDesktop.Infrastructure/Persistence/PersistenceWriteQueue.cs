using System.Threading.Channels;

namespace TradeDesktop.Infrastructure.Persistence;

internal sealed class PersistenceWriteQueue
{
    private readonly Channel<IPersistQueueItem> _channel = Channel.CreateUnbounded<IPersistQueueItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public void Enqueue(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _channel.Writer.TryWrite(new ActionItem(action));
    }

    public Task FlushAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Writer.TryWrite(new FlushItem(tcs));
        if (!ct.CanBeCanceled)
        {
            return tcs.Task;
        }

        return tcs.Task.WaitAsync(ct);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(ct))
        {
            switch (item)
            {
                case ActionItem action:
                    await action.Action(ct);
                    break;
                case FlushItem flush:
                    flush.Tcs.TrySetResult(true);
                    break;
            }
        }
    }

    private interface IPersistQueueItem;

    private sealed record ActionItem(Func<CancellationToken, Task> Action) : IPersistQueueItem;

    private sealed record FlushItem(TaskCompletionSource<bool> Tcs) : IPersistQueueItem;
}
