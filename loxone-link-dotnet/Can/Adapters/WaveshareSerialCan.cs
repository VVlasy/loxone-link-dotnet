using Microsoft.Extensions.Logging;
using System.IO.Ports;
using static loxonelinkdotnet.Can.Adapters.SocketCan;

namespace loxonelinkdotnet.Can.Adapters;

/// <summary>
/// Waveshare Serial CAN adapter implementation
/// Based on USB-CAN adapter that uses a virtual COM port
/// </summary>
public class WaveshareSerialCan : ICanInterface
{
    private readonly SerialPort _serialPort;
    private readonly SemaphoreSlim _sendSemaphore;
    private readonly ILogger? _logger; // Make logger nullable
    private readonly object _lockObject = new object();
    private readonly List<byte> _receiveBuffer = new List<byte>();
    private bool _disposed = false;
    private Task? _receiveTask;
    private CancellationTokenSource? _receiveCancellationTokenSource;
    private ulong _sequenceCounter = 0;

    public event EventHandler<CanFrame>? FrameReceived;
    public event EventHandler<CanFrame>? FrameSent;

    // Binary frame structure constants
    private const int MIN_FRAME_SIZE = 5; // Minimum CAN frame size in binary
    private const byte FRAME_START_MARKER = 0xAA; // Adjust based on your adapter

    public WaveshareSerialCan(string portName, int baudRate, int canBaudRate, ILogger? logger = null)
    {
        _logger = logger; // Allow null logger
        _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 100, // Shorter timeout for better responsiveness
            WriteTimeout = 1000,
            NewLine = "\r\n"
        };

        _sendSemaphore = new SemaphoreSlim(1, 1);
        _logger?.LogInformation($"[WaveshareSerialCAN] Initialized on port {portName}, baud: {baudRate}, CAN bitrate: {canBaudRate}");
    }

    // Helper methods for safe logging
    private void LogDebug(string message) => _logger?.LogDebug(message);
    private void LogInformation(string message) => _logger?.LogInformation(message);
    private void LogWarning(string message) => _logger?.LogWarning(message);
    private void LogError(Exception ex, string message) => _logger?.LogError(ex, message);

    public void Open()
    {
        if (!_serialPort.IsOpen)
        {
            try
            {
                _serialPort.Open();
                LogInformation($"[WaveshareSerialCAN] Opened serial port {_serialPort.PortName}");
                InitializeAdapter();
            }
            catch (Exception ex)
            {
                LogError(ex, "[WaveshareSerialCAN] Failed to open serial port");
                throw;
            }
        }
    }

    private void InitializeAdapter()
    {
        try
        {
            LogDebug("[WaveshareSerialCAN] Initializing Waveshare CAN adapter with exact protocol...");
            
            // Clear any existing data
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();
            
            // Use the exact initialization command from the Python script
            // Set CAN baudrate using variable length protocol
            List<byte> setBaudrate = new List<byte>
            {
                0xAA,   // Packet header
                0x55,   // Packet header  
                0x12,   // Type: use variable protocol (0x02 for fixed 20 byte, 0x12 for variable)
                0x07,   // CAN Baud Rate:  500kbps  ##  0x01(1Mbps),  0x02(800kbps),  0x03(500kbps),  0x04(400kbps),  0x05(250kbps),  0x06(200kbps),  0x07(125kbps),  0x08(100kbps),  0x09(50kbps),  0x0a(20kbps),  0x0b(10kbps),   0x0c(5kbps)##
                0x02,   // Frame Type: Extended Frame (0x01=standard, 0x02=extended)
                0x00,   // Filter ID1
                0x00,   // Filter ID2  
                0x00,   // Filter ID3
                0x00,   // Filter ID4
                0x00,   // Mask ID1
                0x00,   // Mask ID2
                0x00,   // Mask ID3
                0x00,   // Mask ID4
                0x00,   // CAN mode: normal mode (0x00=normal, 0x01=silent, 0x02=loopback, 0x03=loopback silent)
                0x00,   // Automatic resend: automatic retransmission
                0x00,   // Spare
                0x00,   // Spare
                0x00,   // Spare
                0x00,   // Spare
            };

            // Calculate checksum (sum of bytes from index 2 onwards)
            int checksum = 0;
            for (int i = 2; i < setBaudrate.Count; i++)
            {
                checksum += setBaudrate[i];
            }
            setBaudrate.Add((byte)(checksum & 0xFF));

            // Send initialization command
            byte[] initCommand = setBaudrate.ToArray();
            _serialPort.Write(initCommand, 0, initCommand.Length);
            
            LogInformation($"[WaveshareSerialCAN] Sent CAN baudrate setting command: {initCommand.Length} bytes");
            LogDebug($"[WaveshareSerialCAN] Init command: {string.Join(" ", initCommand.Select(b => $"{b:X2}"))}");

            // Wait for adapter to process command
            Thread.Sleep(500);

            // Clear receive buffer after initialization
            _serialPort.DiscardInBuffer();
            lock (_lockObject)
            {
                _receiveBuffer.Clear();
            }
            
            LogInformation("[WaveshareSerialCAN] Waveshare CAN adapter initialized successfully with variable protocol");
        }
        catch (Exception ex)
        {
            LogError(ex, "[WaveshareSerialCAN] Failed to initialize Waveshare CAN adapter");
            throw new InvalidOperationException("Failed to initialize Waveshare CAN adapter", ex);
        }
    }

    public void StartReceiving()
    {
        ThrowIfDisposed();

        if (_receiveTask != null && !_receiveTask.IsCompleted)
        {
            LogDebug("[WaveshareSerialCAN] Receiving already started");
            return;
        }

        if (!_serialPort.IsOpen)
        {
            LogDebug("[WaveshareSerialCAN] Opening serial port for receive");
            Open();
        }

        _receiveCancellationTokenSource = new CancellationTokenSource();
        _receiveTask = Task.Run(ReceiveLoop, _receiveCancellationTokenSource.Token);
        LogInformation("[WaveshareSerialCAN] Started background receiving task");
    }

    public void StopReceiving()
    {
        if (_receiveCancellationTokenSource != null)
        {
            LogDebug("[WaveshareSerialCAN] Stopping background receiving task");
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
        
        LogInformation("[WaveshareSerialCAN] Stopped background receiving task");
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
                    bool newDataReceived = false;
                    
                    // Read available bytes from serial port
                    if (_serialPort.BytesToRead > 0)
                    {
                        byte[] buffer = new byte[_serialPort.BytesToRead];
                        int bytesRead = _serialPort.Read(buffer, 0, buffer.Length);

                        if (bytesRead > 0)
                        {
                            newDataReceived = true;
                            lock (_lockObject)
                            {
                                _receiveBuffer.AddRange(buffer.Take(bytesRead));
                                
                                // Debug: Log raw received data
                                string hexData = string.Join(" ", buffer.Take(bytesRead).Select(b => $"{b:X2}"));
                                //_logger?.LogDebug($"[WaveshareSerialCAN] Raw RX: {bytesRead} bytes: {hexData}");
                            }
                        }
                    }

                    // Always try to parse frames from buffer (not just when new data arrives)
                    lock (_lockObject)
                    {
                        CanFrame? frame;
                        // Keep parsing while there are complete frames in the buffer
                        while ((frame = TryParseFrame()).HasValue)
                        {
                            var frameValue = frame.Value; // Create local copy to avoid closure issues
                            
                            // Assign sequence number to incoming frame
                            frameValue.SequenceNumber = Interlocked.Increment(ref _sequenceCounter);
                            
                            LogDebug($"[WaveshareSerialCAN] RX: ID=0x{frameValue.CanId:X8} DLC={frameValue.CanDlc} Seq={frameValue.SequenceNumber} Data=[{string.Join(" ", frameValue.Data.Take(frameValue.CanDlc).Select(b => $"{b:X2}"))}]");
                            
                            // Raise the event on a background thread to avoid blocking
                            _ = Task.Run(() => FrameReceived?.Invoke(this, frameValue));
                        }
                    }

                    // Only delay if no new data was received to prevent busy waiting
                    if (!newDataReceived)
                    {
                        await Task.Delay(1, cancellationToken);
                    }
                }
                catch (TimeoutException)
                {
                    continue;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogError(ex, "[WaveshareSerialCAN] Error during receive operation");
                    await Task.Delay(100, cancellationToken); // Longer delay on error
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogDebug("[WaveshareSerialCAN] Receive loop cancelled");
        }
        catch (Exception ex)
        {
            LogError(ex, "[WaveshareSerialCAN] Error in receive loop");
        }
    }

    private CanFrame? TryParseFrame()
    {
        // Parse frames using the exact Waveshare protocol from the Python script
        // Frame format: [AA][Control][ID bytes][Data bytes][55]
        // Control byte: bits 7-6=11, bit 5=frame type, bit 4=frame format, bits 3-0=DLC

        if (_receiveBuffer.Count < 3) // Minimum: AA + Control + 55
            return null;

        try
        {
            // Look for frame start: AA followed by control byte with bits 7-6 = 11 (0xC0)
            int frameStart = FindWaveshareProtocolFrame();
            if (frameStart == -1)
            {
                // Clean old data if buffer gets too large
                if (_receiveBuffer.Count > 256)
                {
                    LogWarning("[WaveshareSerialCAN] Receive buffer getting large, clearing old data");
                    _receiveBuffer.RemoveRange(0, Math.Min(64, _receiveBuffer.Count / 2));
                }
                return null;
            }

            // Parse the Waveshare protocol frame
            var frame = ParseWaveshareProtocolFrame(frameStart);
            if (frame.HasValue)
            {
                // Remove parsed data from buffer
                int frameLength = GetWaveshareProtocolFrameLength(frameStart);
                _receiveBuffer.RemoveRange(0, frameStart + frameLength);
                return frame;
            }

            // If parsing failed, remove one byte and try again
            _receiveBuffer.RemoveRange(0, 1);
            return null;
        }
        catch (Exception ex)
        {
            LogError(ex, "[WaveshareSerialCAN] Error parsing Waveshare protocol frame");
            
            // Don't clear entire buffer, just remove some data and continue
            if (_receiveBuffer.Count > 32)
            {
                _receiveBuffer.RemoveRange(0, 16);
            }
            else
            {
                _receiveBuffer.Clear();
            }
            return null;
        }
    }

    private int FindWaveshareProtocolFrame()
    {
        // Look for Waveshare protocol frame: AA followed by control byte (bits 7-6 = 11)
        for (int i = 0; i < _receiveBuffer.Count - 1; i++)
        {
            if (_receiveBuffer[i] == 0xAA && i + 1 < _receiveBuffer.Count)
            {
                byte controlByte = _receiveBuffer[i + 1];
                if ((controlByte & 0xC0) == 0xC0) // Check if bits 7-6 are both 1
                {
                    //_logger?.LogDebug($"[WaveshareSerialCAN] Found Waveshare protocol frame at offset {i}, control=0x{controlByte:X2}");
                    return i;
                }
            }
        }
        return -1;
    }

    private CanFrame? ParseWaveshareProtocolFrame(int startIndex)
    {
        try
        {
            if (startIndex + 1 >= _receiveBuffer.Count)
                return null;

            // Parse control byte
            byte controlByte = _receiveBuffer[startIndex + 1];
            
            // Extract frame information from control byte
            int dlc = controlByte & 0x0F;        // Bits 3-0: data length
            bool isRemoteFrame = (controlByte & 0x10) != 0; // Bit 4: frame format
            bool isExtendedFrame = (controlByte & 0x20) != 0; // Bit 5: frame type
            
            //_logger?.LogDebug($"[WaveshareSerialCAN] Parsing frame: DLC={dlc}, Extended={isExtendedFrame}, Remote={isRemoteFrame}");

            // Calculate expected frame length
            int expectedIdBytes = isExtendedFrame ? 4 : 2;
            int expectedFrameLength = 2 + expectedIdBytes + dlc + 1; // AA + Control + ID + Data + 55
            
            if (startIndex + expectedFrameLength > _receiveBuffer.Count)
            {
                LogDebug($"[WaveshareSerialCAN] Not enough data for complete frame. Need {expectedFrameLength}, have {_receiveBuffer.Count - startIndex}");
                return null; // Not enough data yet
            }

            // Check end marker
            if (_receiveBuffer[startIndex + expectedFrameLength - 1] != 0x55)
            {
                LogWarning($"[WaveshareSerialCAN] Invalid end marker: expected 0x55, got 0x{_receiveBuffer[startIndex + expectedFrameLength - 1]:X2}");
                return null; // Invalid frame
            }

            var frame = new CanFrame
            {
                CanDlc = (byte)dlc,
                Data = new byte[8]
            };

            // Parse CAN ID based on frame type
            if (isExtendedFrame)
            {
                // Extended frame: 4 bytes, little-endian
                uint id = (uint)(_receiveBuffer[startIndex + 5] << 24 |
                               _receiveBuffer[startIndex + 4] << 16 |
                               _receiveBuffer[startIndex + 3] << 8 |
                               _receiveBuffer[startIndex + 2]);
                frame.CanId = id;
                
                // Data starts after 2 header bytes + 4 ID bytes
                int dataStart = startIndex + 6;
                for (int i = 0; i < dlc && i < 8; i++)
                {
                    frame.Data[i] = _receiveBuffer[dataStart + i];
                }
            }
            else
            {
                // Standard frame: 2 bytes, little-endian  
                uint id = (uint)(_receiveBuffer[startIndex + 3] << 8 |
                               _receiveBuffer[startIndex + 2]);
                frame.CanId = id;
                
                // Data starts after 2 header bytes + 2 ID bytes
                int dataStart = startIndex + 4;
                for (int i = 0; i < dlc && i < 8; i++)
                {
                    frame.Data[i] = _receiveBuffer[dataStart + i];
                }
            }

            LogDebug($"[WaveshareSerialCAN] Parsed Waveshare frame: ID=0x{frame.CanId:X8}, DLC={frame.CanDlc}, Extended={isExtendedFrame}, Remote={isRemoteFrame}");
            return frame;
        }
        catch (Exception ex)
        {
            LogError(ex, $"[WaveshareSerialCAN] Error parsing Waveshare protocol frame at offset {startIndex}");
            return null;
        }
    }

    private int GetWaveshareProtocolFrameLength(int startIndex)
    {
        try
        {
            if (startIndex + 1 >= _receiveBuffer.Count)
                return 3; // Minimum frame size

            byte controlByte = _receiveBuffer[startIndex + 1];
            
            int dlc = controlByte & 0x0F;
            bool isExtendedFrame = (controlByte & 0x20) != 0;
            
            int idBytes = isExtendedFrame ? 4 : 2;
            int frameLength = 2 + idBytes + dlc + 1; // AA + Control + ID + Data + 55
            
            //_logger?.LogDebug($"[WaveshareSerialCAN] Waveshare protocol frame length: {frameLength}");
            return frameLength;
        }
        catch
        {
            return 3; // Fallback to minimum
        }
    }

    public async Task SendAsync(CanFrame frame)
    {
        ThrowIfDisposed();

        if (!_serialPort.IsOpen)
        {
            LogDebug("[WaveshareSerialCAN] Opening serial port for send");
            Open();
        }

        await _sendSemaphore.WaitAsync();

        try
        {
            byte[] frameBytes = FormatBinaryFrame(frame);
            LogInformation($"[WaveshareSerialCAN] TX: ID=0x{frame.CanId:X8} DLC={frame.CanDlc} Data=[{string.Join(" ", frame.Data.Take(frame.CanDlc).Select(b => $"{b:X2}"))}]");
            //_logger?.LogDebug($"[WaveshareSerialCAN] Sending {frameBytes.Length} bytes to serial port");

            await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    _serialPort.Write(frameBytes, 0, frameBytes.Length);
                }
            });

            // Raise FrameSent event after successful transmission
            _ = Task.Run(() => FrameSent?.Invoke(this, frame));
        }
        catch (Exception ex)
        {
            LogError(ex, "[WaveshareSerialCAN] Error sending frame");
            throw;
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }

    private byte[] FormatBinaryFrame(CanFrame frame)
    {
        try
        {
            // Format frame using exact Waveshare protocol from Python script
            // Format: [AA][Control][ID bytes][Data bytes][55]
            
            List<byte> frameBytes = new List<byte>();

            // Add frame header
            frameBytes.Add(0xAA);

            // Create control byte
            // Bits 7-6: 11 (frame header indicator)
            // Bit 5: Frame type (0=Standard, 1=Extended)  
            // Bit 4: Frame format (0=Data, 1=Remote)
            // Bits 3-0: Data length (DLC)
            
            bool isExtended = frame.CanId > 0x7FF; // Extended if ID > 11 bits
            byte controlByte = 0xC0; // Set bits 7-6 to 11
            
            if (isExtended)
                controlByte |= 0x20; // Set bit 5 for extended frame
            
            // Bit 4 is 0 for data frame (we don't support remote frames for sending)
            
            // Set DLC in bits 3-0
            controlByte |= (byte)(frame.CanDlc & 0x0F);
            
            frameBytes.Add(controlByte);

            // Add CAN ID bytes
            if (isExtended)
            {
                // Extended frame: 4 bytes, little-endian (LSB first)
                byte id0 = (byte)(frame.CanId & 0xFF);         // LSB
                byte id1 = (byte)(frame.CanId >> 8 & 0xFF);
                byte id2 = (byte)(frame.CanId >> 16 & 0xFF);
                byte id3 = (byte)(frame.CanId >> 24 & 0xFF);  // MSB
                
                _logger?.LogDebug($"CAN ID 0x{frame.CanId:X8} -> bytes: {id0:X2} {id1:X2} {id2:X2} {id3:X2} (little-endian)");
                
                frameBytes.Add(id0);  // LSB first
                frameBytes.Add(id1);
                frameBytes.Add(id2);
                frameBytes.Add(id3);  // MSB last
            }
            else
            {
                // Standard frame: 2 bytes, little-endian (LSB first)
                byte id0 = (byte)(frame.CanId & 0xFF);         // LSB
                byte id1 = (byte)(frame.CanId >> 8 & 0xFF);  // MSB

                _logger?.LogDebug($"CAN ID 0x{frame.CanId:X8} -> bytes: {id0:X2} {id1:X2} (little-endian)");
                
                frameBytes.Add(id0);  // LSB first
                frameBytes.Add(id1);  // MSB last
            }

            // Add data bytes
            for (int i = 0; i < frame.CanDlc && i < 8; i++)
            {
                frameBytes.Add(frame.Data[i]);
            }

            // Add end marker
            frameBytes.Add(0x55);

            LogDebug($"[WaveshareSerialCAN] Formatted Waveshare protocol frame: {frameBytes.Count} bytes, Extended={isExtended}, Control=0x{controlByte:X2}");
            
            return frameBytes.ToArray();
        }
        catch (Exception ex)
        {
            LogError(ex, "[WaveshareSerialCAN] Error formatting Waveshare protocol frame");
            throw;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WaveshareSerialCan));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            try
            {
                LogInformation("[WaveshareSerialCAN] Disposing serial CAN interface");
                
                // Stop receiving task first
                StopReceiving();
                
                // Send close command in binary format if needed
                if (_serialPort?.IsOpen == true)
                {
                    byte[] closeCommand = { 0xC1, 0x00 }; // Example close command
                    _serialPort.Write(closeCommand, 0, closeCommand.Length);
                    Thread.Sleep(100);
                    LogDebug("[WaveshareSerialCAN] Sent close command to adapter");
                }
            }
            catch (Exception ex)
            {
                // Ignore errors during disposal but log them
                _logger?.LogWarning($"[WaveshareSerialCAN] Error during disposal: {ex.Message}");
            }

            _serialPort?.Dispose();
            _sendSemaphore?.Dispose();
            _receiveCancellationTokenSource?.Dispose();
            _disposed = true;
            LogInformation("[WaveshareSerialCAN] Serial CAN interface disposed");
        }
    }

    ~WaveshareSerialCan()
    {
        Dispose(false);
    }
}
