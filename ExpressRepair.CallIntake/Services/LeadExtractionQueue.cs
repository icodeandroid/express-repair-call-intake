using System.Threading.Channels;

namespace ExpressRepair.CallIntake.Services;

/// <summary>
/// The hand-off between the webhook and the worker. The webhook writes a call id
/// and returns; the worker reads ids and does the slow work.
/// </summary>
public interface ILeadExtractionQueue
{
    ValueTask EnqueueAsync(int callId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<int> DequeueAllAsync(CancellationToken cancellationToken);
}

/// <summary>
/// An in-memory queue built on System.Threading.Channels.
///
/// Deliberately the simplest thing that satisfies the brief: no broker to install,
/// nothing for a reviewer to set up. The trade-off is that anything still queued
/// is lost if the process restarts, which is why the worker also sweeps the
/// database for stranded Processing rows on startup.
/// </summary>
public sealed class LeadExtractionQueue : ILeadExtractionQueue
{
    private readonly Channel<int> _channel;

    public LeadExtractionQueue()
    {
        // Unbounded is safe at this volume; a bounded channel with
        // BoundedChannelFullMode.Wait would apply backpressure if it were not.
        _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(int callId, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(callId, cancellationToken);

    public IAsyncEnumerable<int> DequeueAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
