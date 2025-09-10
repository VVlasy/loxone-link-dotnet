using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace loxonelinkdotnet.Can.Adapters;

/// <summary>
/// SocketCAN wrapper for .NET
/// This is a simplified wrapper that assumes SocketCAN library is available on the system
/// </summary>
public class SocketCan : ICanInterface
{
    private int _socket = -1;
    private bool _disposed = false;
    private readonly ILogger? _logger;
    private Task? _receiveTask;
    private CancellationTokenSource? _receiveCancellationTokenSource;
    private ulong _sequenceCounter = 0;

    public event EventHandler<CanFrame>? FrameReceived;
    public event EventHandler<CanFrame>? FrameSent;

    // CAN frame structure (simplified)
    [StructLayout(LayoutKind.Sequential)]
    public struct CanFrame
    {
        public uint CanId;
        public byte CanDlc;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Data;
        public ulong SequenceNumber; // Adapter-assigned sequence number for ordering

        public CanFrame()
        {
            Data = new byte[8];
            SequenceNumber = 0;
        }

        // Extended CAN frame flag (bit 31)
        public bool IsExtended => (CanId & 0x80000000) != 0;

        // Get actual CAN ID (without flags)
        public uint ActualId => CanId & 0x1FFFFFFF;
    }

    // Mock implementation - in real scenario, would use P/Invoke to actual SocketCAN
    private const int AF_CAN = 29;
    private const int PF_CAN = AF_CAN;
    private const int CAN_RAW = 1;
    private const int SOL_CAN_BASE = 100;
    private const int CAN_RAW_FILTER = 1;

    public SocketCan(string interfaceName = "can0")
        : this(interfaceName, null)
    {
    }

    public SocketCan(string interfaceName, ILogger? logger)
    {
        _logger = logger;
        // In real implementation, this would:
        // 1. Create socket with socket(PF_CAN, SOCK_RAW, CAN_RAW)
        // 2. Bind to interface
        // For now, we'll simulate this
        _socket = 1; // Mock socket descriptor
        _logger?.LogInformation($"[SocketCAN] Opened CAN interface: {interfaceName}");
    }

    public void StartReceiving()
    {
        if (_receiveTask != null && !_receiveTask.IsCompleted)
        {
            _logger?.LogDebug("[SocketCAN] Receiving already started");
            return;
        }

        _receiveCancellationTokenSource = new CancellationTokenSource();
        _receiveTask = Task.Run(ReceiveLoop, _receiveCancellationTokenSource.Token);
        _logger?.LogInformation("[SocketCAN] Started background receiving task");
    }

    public void StopReceiving()
    {
        if (_receiveCancellationTokenSource != null)
        {
            _logger?.LogDebug("[SocketCAN] Stopping background receiving task");
            _receiveCancellationTokenSource.Cancel();
            _receiveCancellationTokenSource.Dispose();
            _receiveCancellationTokenSource = null;
        }

        if (_receiveTask != null)
        {
            try
            {
                _receiveTask.Wait(1000); // Wait up to 1 second for graceful shutdown
            }
            catch (AggregateException)
            {
                // Ignore cancellation exceptions during shutdown
            }
            _receiveTask = null;
        }
        
        _logger?.LogInformation("[SocketCAN] Stopped background receiving task");
    }

    private async Task ReceiveLoop()
    {
        var cancellationToken = _receiveCancellationTokenSource?.Token ?? CancellationToken.None;
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Mock implementation - in real scenario would use recv() system call
                    await Task.Delay(100, cancellationToken); // Simulate waiting for data
                    
                    // For mock implementation, don't generate any frames
                    // In real implementation, this would receive actual frames and raise events
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[SocketCAN] Error in receive loop");
                    await Task.Delay(1000, cancellationToken); // Delay on error
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("[SocketCAN] Receive loop cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SocketCAN] Error in receive loop");
        }
    }

    /// <summary>
    /// Internal method to simulate frame reception with sequence numbering
    /// In real implementation, this would be called from the receive loop when frames arrive
    /// </summary>
    internal void SimulateFrameReceived(CanFrame frame)
    {
        // Assign sequence number to incoming frame
        frame.SequenceNumber = Interlocked.Increment(ref _sequenceCounter);
        
        _logger?.LogDebug($"[SocketCAN] RX: ID=0x{frame.CanId:X8} DLC={frame.CanDlc} Seq={frame.SequenceNumber} Data=[{string.Join(" ", frame.Data.Take(frame.CanDlc).Select(b => $"{b:X2}"))}]");
        
        // Raise FrameReceived event
        FrameReceived?.Invoke(this, frame);
    }

    public async Task SendAsync(CanFrame frame)
    {
        // Mock implementation - in real scenario would use send() system call
        await Task.Delay(1); // Simulate send delay
        _logger?.LogDebug($"[SocketCAN] TX: ID=0x{frame.CanId:X8} DLC={frame.CanDlc} Data=[{string.Join(" ", frame.Data.Take(frame.CanDlc).Select(b => $"{b:X2}"))}]");
        
        // Raise FrameSent event after successful transmission
        FrameSent?.Invoke(this, frame);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // Stop receiving task first
            StopReceiving();
            
            if (_socket != -1)
            {
                // In real implementation: close(_socket)
                _logger?.LogInformation("[SocketCAN] Closed CAN interface");
                _socket = -1;
            }
            
            _receiveCancellationTokenSource?.Dispose();
            _disposed = true;
        }
    }
}
