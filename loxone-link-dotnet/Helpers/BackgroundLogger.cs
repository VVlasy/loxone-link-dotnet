using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace loxonelinkdotnet.Helpers
{
    /// <summary>
    /// Lightweight logger wrapper that offloads actual calls to the inner ILogger onto a background worker.
    /// This prevents caller threads from being delayed by potentially slow logger providers (for example Console).
    ///
    /// Notes/assumptions:
    /// - BeginScope is proxied to the inner logger and therefore scopes may not be applied to queued log entries
    ///   if the scope is disposed before the background worker processes the entry.
    /// - The wrapper attempts to preserve ILogger semantics and simply queues a delegate that calls the inner logger.
    /// - On Dispose the queue is drained and worker stops.
    /// </summary>
    public class BackgroundLogger : ILogger, IDisposable
    {
        private readonly ILogger _inner;
        private readonly BlockingCollection<Action> _queue;
        private readonly Task _worker;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public BackgroundLogger(ILogger inner, int boundedCapacity = 10000)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            // Use a bounded queue to avoid unbounded memory growth; fallback to unbounded if capacity <= 0
            _queue = boundedCapacity > 0
                ? new BlockingCollection<Action>(new ConcurrentQueue<Action>(), boundedCapacity)
                : new BlockingCollection<Action>(new ConcurrentQueue<Action>());

            _worker = Task.Run(() => WorkerLoop(_cts.Token));
        }

        private void WorkerLoop(CancellationToken ct)
        {
            try
            {
                foreach (var action in _queue.GetConsumingEnumerable(ct))
                {
                    try
                    {
                        action?.Invoke();
                    }
                    catch
                    {
                        // Swallow exceptions from inner logger calls to avoid crashing worker
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled - exit
            }
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            // Proxy scopes to inner logger. Note: if the scope is disposed before queued logs are written,
            // the inner logger may not include the scope for those entries.
            return _inner.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            // Capture parameters and enqueue a delegate that calls the inner logger on the background thread.
            try
            {
                var s = state;
                var e = exception;
                var f = formatter;
                var ev = eventId;
                // Use TryAdd to avoid blocking the caller if the queue is full. If the queue is full or completed,
                // drop the log entry to ensure logging never blocks hot paths.
                _queue.TryAdd(() => _inner.Log(logLevel, ev, s, e, f));
            }
            catch (InvalidOperationException)
            {
                // Queue is marked as complete for adding - ignore new logs
            }
        }

        public void Dispose()
        {
            try
            {
                _queue.CompleteAdding();
                _cts.Cancel();
                try
                {
                    _worker.Wait(5000);
                }
                catch (AggregateException) { }
            }
            finally
            {
                _cts.Dispose();
                _queue.Dispose();
            }
        }
    }
}
