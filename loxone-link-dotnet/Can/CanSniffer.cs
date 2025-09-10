using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using loxonelinkdotnet.NatProtocol.Core;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.Can.Adapters;
using Microsoft.Extensions.Logging;

namespace loxonelinkdotnet.Tools
{
    /// <summary>
    /// CAN bus sniffer for analyzing Loxone device startup sequences
    /// </summary>
    public class CanSniffer : IDisposable
    {
        private readonly ICanInterface _canBus;
        private readonly ILogger _logger;
        private readonly List<CanSnifferEntry> _capturedFrames = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private bool _isRunning = false;
        private DateTime _startTime = DateTime.UtcNow;

        // Frame processing queue
        private readonly Queue<CanSnifferEntry> _frameQueue = new();
        private readonly object _queueLock = new object();
        private readonly SemaphoreSlim _frameAvailable = new SemaphoreSlim(0);
        private Task? _frameProcessingTask;

        public CanSniffer(ICanInterface canBus, ILogger logger)
        {
            _canBus = canBus;
            _logger = logger;
            _canBus.FrameReceived += OnFrameReceived;
            _canBus.FrameSent += OnFrameSent;
        }

        /// <summary>
        /// Start sniffing CAN frames
        /// </summary>
        public async Task StartAsync()
        {
            if (_isRunning)
            {
                _logger?.LogWarning("[Sniffer] Already running!");
                return;
            }

            _logger?.LogInformation("[Sniffer] Starting CAN bus sniffer...");
            _logger?.LogInformation("[Sniffer] Press 's' to save capture, 'c' to clear, 'a' to analyze, 'q' to quit");
            
            _isRunning = true;
            _startTime = DateTime.UtcNow;
            _capturedFrames.Clear();

            // Start receiving frames via event
            _canBus.StartReceiving();

            // Start frame processing task
            _frameProcessingTask = Task.Run(FrameProcessingLoopAsync, _cancellationTokenSource.Token);

            // Start console input handler
            _ = Task.Run(HandleConsoleInputAsync, _cancellationTokenSource.Token);

            // Return immediately - tasks run in background
            await Task.CompletedTask;
        }

        /// <summary>
        /// Stop sniffing
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;

            _logger?.LogInformation("[Sniffer] Stopping CAN bus sniffer...");
            _isRunning = false;
            _cancellationTokenSource.Cancel();
        }

        /// <summary>
        /// Handle received CAN frames - queue them for processing
        /// </summary>
        private void OnFrameReceived(object? sender, SocketCan.CanFrame frame)
        {
            QueueFrame(frame, FrameDirection.Received);
        }

        /// <summary>
        /// Handle sent CAN frames - queue them for processing
        /// </summary>
        private void OnFrameSent(object? sender, SocketCan.CanFrame frame)
        {
            QueueFrame(frame, FrameDirection.Sent);
        }

        /// <summary>
        /// Queue a frame for processing with direction information
        /// </summary>
        private void QueueFrame(SocketCan.CanFrame frame, FrameDirection direction)
        {
            if (!_isRunning) return;

            try
            {
                // Create entry with frame and direction
                var entry = new CanSnifferEntry
                {
                    Timestamp = DateTime.UtcNow,
                    RelativeTimeMs = (DateTime.UtcNow - _startTime).TotalMilliseconds,
                    CanFrame = frame,
                    Direction = direction
                };

                lock (_queueLock)
                {
                    _frameQueue.Enqueue(entry);
                }
                
                // Signal that a frame is available for processing
                _frameAvailable.Release();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[Sniffer] Error queuing frame");
            }
        }

        /// <summary>
        /// Background task to process queued frames
        /// </summary>
        private async Task FrameProcessingLoopAsync()
        {
            _logger?.LogDebug("[Sniffer] Frame processing loop started");

            while (!_cancellationTokenSource.Token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    // Wait for a frame to be available
                    await _frameAvailable.WaitAsync(_cancellationTokenSource.Token);

                    CanSnifferEntry? entryToProcess = null;

                    // Dequeue the next entry
                    lock (_queueLock)
                    {
                        if (_frameQueue.Count > 0)
                        {
                            entryToProcess = _frameQueue.Dequeue();
                        }
                    }

                    // Process the entry if we got one
                    if (entryToProcess != null)
                    {
                        ProcessSnifferEntry(entryToProcess);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[Sniffer] Error in frame processing loop");
                    // Continue processing other frames even if one fails
                }
            }

            _logger?.LogDebug("[Sniffer] Frame processing loop stopped");
        }

        /// <summary>
        /// Process a single CAN frame entry for sniffing
        /// </summary>
        private void ProcessSnifferEntry(CanSnifferEntry entry)
        {
            try
            {
                // Try to parse as NAT frame
                var natFrame = NatFrame.FromCanFrame(entry.CanFrame);
                if (natFrame != null)
                {
                    entry.NatFrame = natFrame;
                    entry.IsNatFrame = true;
                    
                    // Add analysis
                    entry.Analysis = AnalyzeNatFrame(natFrame);
                }

                lock (_capturedFrames)
                {
                    _capturedFrames.Add(entry);
                }

                // Real-time display (limited to interesting frames)
                if (ShouldDisplayFrame(entry) || true)
                {
                    DisplayFrame(entry);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[Sniffer] Error processing entry");
            }
        }

        /// <summary>
        /// Handle console input for sniffer commands
        /// </summary>
        private async Task HandleConsoleInputAsync()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        
                        switch (char.ToLower(key.KeyChar))
                        {
                            case 's':
                                await SaveCaptureAsync();
                                break;
                                
                            case 'c':
                                ClearCapture();
                                break;
                                
                            case 'a':
                                AnalyzeCapture();
                                break;
                                
                            case 'q':
                                Stop();
                                break;
                                
                            case 'f':
                                FilterAndDisplayFrames();
                                break;
                                
                            case 'h':
                                ShowHelp();
                                break;
                        }
                    }
                    
                    await Task.Delay(100);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Determine if a frame should be displayed in real-time
        /// </summary>
        private bool ShouldDisplayFrame(CanSnifferEntry entry)
        {
            if (!entry.IsNatFrame)
                return false;

            var frame = entry.NatFrame!;
            
            // Show important NAT commands
            return frame.Command switch
            {
                NatCommands.NatOfferRequest => true,
                NatCommands.NatOfferConfirm => true,
                NatCommands.StartInfo => true,
                NatCommands.VersionInfo => true,
                NatCommands.VersionRequest => true,
                NatCommands.CryptChallengeAuthRequest => true,
                NatCommands.CryptChallengeAuthReply => true,
                NatCommands.SendConfig => true,
                NatCommands.Alive => true,
                NatCommands.FragmentStart => true,
                _ => false
            };
        }

        /// <summary>
        /// Display a captured frame
        /// </summary>
        private void DisplayFrame(CanSnifferEntry entry)
        {
            var timeStr = $"{entry.RelativeTimeMs:F0}ms".PadLeft(8);
            var directionStr = entry.Direction == FrameDirection.Sent ? "TX" : "RX";
            
            if (entry.IsNatFrame)
            {
                var nat = entry.NatFrame!;
                var dataStr = string.Join(" ", nat.Data.Select(b => $"{b:X2}"));
                _logger?.LogInformation($"[{timeStr}] {directionStr} NAT: Ext={nat.NatId:X2} Dev={nat.DeviceId:X2} Cmd=0x{nat.Command:X2} Data=[{dataStr}] B0=0x{nat.B0:X2} Val16=0x{nat.Val16:X4} Val32=0x{nat.Val32:X8} {entry.Analysis}");
            }
            else
            {
                var can = entry.CanFrame;
                var dataStr = string.Join(" ", can.Data.Take(can.CanDlc).Select(b => $"{b:X2}"));
                _logger?.LogInformation($"[{timeStr}] {directionStr} CAN: ID=0x{can.CanId:X3} DLC={can.CanDlc} Data=[{dataStr}]");
            }
        }

        /// <summary>
        /// Analyze a NAT frame and provide description
        /// </summary>
        private string AnalyzeNatFrame(NatFrame frame)
        {
            var sb = new StringBuilder();
            
            switch (frame.Command)
            {
                case NatCommands.NatOfferRequest:
                    if (frame.Data.Length >= 7)
                    {
                        // For tree devices: B0 is at byte 0, device type is at bytes 1-2
                        var deviceType = BitConverter.ToUInt16(frame.Data, 1);
                        var serial = BitConverter.ToUInt32(frame.Data, 3);
                        sb.Append($"OFFER Type=0x{deviceType:X4} Serial=0x{serial:X8}");
                    }
                    break;
                    
                case NatCommands.NatOfferConfirm:
                    if (frame.Data.Length >= 7)
                    {
                        var assignedNat = frame.Data[0];
                        var isParked = frame.Data[1] != 0;
                        var serial = BitConverter.ToUInt32(frame.Data, 3);
                        sb.Append($"ASSIGN NAT={assignedNat:X2} Parked={isParked} Serial=0x{serial:X8}");
                    }
                    break;
                    
                case NatCommands.StartInfo:
                    sb.Append("START_INFO (fragmented)");
                    break;
                    
                case NatCommands.VersionInfo:
                    sb.Append("VERSION_INFO (fragmented)");
                    break;
                    
                case NatCommands.VersionRequest:
                    if (frame.Data.Length >= 7)
                    {
                        var serial = BitConverter.ToUInt32(frame.Data, 3);
                        sb.Append($"VERSION_REQ Serial=0x{serial:X8}");
                    }
                    break;
                    
                case NatCommands.Alive:
                    if (frame.Data.Length >= 7)
                    {
                        var reason = frame.Data[0];
                        var configCrc = BitConverter.ToUInt32(frame.Data, 3);
                        sb.Append($"ALIVE Reason={reason} ConfigCRC=0x{configCrc:X8}");
                    }
                    break;
                    
                case NatCommands.FragmentStart:
                    if (frame.Data.Length >= 7)
                    {
                        var command = frame.Data[0];
                        var size = BitConverter.ToUInt16(frame.Data, 1);
                        var crc = BitConverter.ToUInt32(frame.Data, 3);
                        sb.Append($"FRAG_START Cmd=0x{command:X2} Size={size} CRC=0x{crc:X8}");
                    }
                    break;
                    
                case NatCommands.FragmentData:
                    sb.Append("FRAG_DATA");
                    break;
                    
                default:
                    sb.Append($"CMD_0x{frame.Command:X2}");
                    break;
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// Save captured frames to file
        /// </summary>
        private async Task SaveCaptureAsync()
        {
            var filename = $"can_capture_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            
            try
            {
                var json = JsonSerializer.Serialize(_capturedFrames, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Converters = { new CanFrameJsonConverter() }
                });
                
                await File.WriteAllTextAsync(filename, json);
                _logger?.LogInformation($"[Sniffer] Saved {_capturedFrames.Count} frames to {filename}");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"[Sniffer] Failed to save capture to {filename}");
            }
        }

        /// <summary>
        /// Clear captured frames
        /// </summary>
        private void ClearCapture()
        {
            _capturedFrames.Clear();
            _startTime = DateTime.UtcNow;
            _logger?.LogInformation("[Sniffer] Capture cleared");
        }

        /// <summary>
        /// Analyze captured startup sequence
        /// </summary>
        private void AnalyzeCapture()
        {
            _logger?.LogInformation($"[Sniffer] Analyzing {_capturedFrames.Count} captured frames...");
            
            // Group by device serial numbers
            var deviceSequences = new Dictionary<uint, List<CanSnifferEntry>>();
            
            foreach (var entry in _capturedFrames.Where(e => e.IsNatFrame))
            {
                var frame = entry.NatFrame!;
                
                // Extract serial number from various frame types
                uint? serial = frame.Command switch
                {
                    NatCommands.NatOfferRequest when frame.Data.Length >= 7 
                        => BitConverter.ToUInt32(frame.Data, 3),
                    NatCommands.NatOfferConfirm when frame.Data.Length >= 7 
                        => BitConverter.ToUInt32(frame.Data, 3),
                    NatCommands.VersionRequest when frame.Data.Length >= 7 
                        => BitConverter.ToUInt32(frame.Data, 3),
                    _ => null
                };
                
                if (serial.HasValue)
                {
                    if (!deviceSequences.ContainsKey(serial.Value))
                        deviceSequences[serial.Value] = new List<CanSnifferEntry>();
                    
                    deviceSequences[serial.Value].Add(entry);
                }
            }
            
            // Analyze each device sequence
            foreach (var kvp in deviceSequences)
            {
                AnalyzeDeviceSequence(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Analyze startup sequence for a specific device
        /// </summary>
        private void AnalyzeDeviceSequence(uint serial, List<CanSnifferEntry> entries)
        {
            _logger?.LogInformation($"[Analysis] Device 0x{serial:X8} - {entries.Count} frames:");
            
            foreach (var entry in entries.OrderBy(e => e.Timestamp))
            {
                var timeStr = $"{entry.RelativeTimeMs:F0}ms".PadLeft(8);
                _logger?.LogInformation($"  [{timeStr}] {entry.Analysis}");
            }
        }

        /// <summary>
        /// Filter and display frames by type
        /// </summary>
        private void FilterAndDisplayFrames()
        {
            Console.WriteLine("Filter options:");
            Console.WriteLine("1. Start Info frames");
            Console.WriteLine("2. Version Info frames");  
            Console.WriteLine("3. Offer/Assignment frames");
            Console.WriteLine("4. Fragment frames");
            Console.WriteLine("5. All NAT frames");
            Console.Write("Choose filter (1-5): ");
            
            var choice = Console.ReadLine();
            
            var filteredFrames = choice switch
            {
                "1" => _capturedFrames.Where(e => e.IsNatFrame && e.NatFrame!.Command == NatCommands.StartInfo),
                "2" => _capturedFrames.Where(e => e.IsNatFrame && e.NatFrame!.Command == NatCommands.VersionInfo),
                "3" => _capturedFrames.Where(e => e.IsNatFrame && 
                    (e.NatFrame!.Command == NatCommands.NatOfferRequest || e.NatFrame!.Command == NatCommands.NatOfferConfirm)),
                "4" => _capturedFrames.Where(e => e.IsNatFrame && 
                    (e.NatFrame!.Command == NatCommands.FragmentStart || e.NatFrame!.Command == NatCommands.FragmentData)),
                "5" => _capturedFrames.Where(e => e.IsNatFrame),
                _ => _capturedFrames
            };
            
            foreach (var entry in filteredFrames)
            {
                DisplayFrame(entry);
            }
        }

        /// <summary>
        /// Show help
        /// </summary>
        private void ShowHelp()
        {
            _logger?.LogInformation("[Sniffer] Commands:");
            _logger?.LogInformation("  s - Save capture to JSON file");
            _logger?.LogInformation("  c - Clear captured frames");
            _logger?.LogInformation("  a - Analyze captured startup sequences");
            _logger?.LogInformation("  f - Filter and display frames by type");
            _logger?.LogInformation("  h - Show this help");
            _logger?.LogInformation("  q - Quit sniffer");
        }

        public void Dispose()
        {
            Stop();
            
            // Wait for frame processing task to complete
            try
            {
                _frameProcessingTask?.Wait(1000);
            }
            catch (AggregateException)
            {
                // Ignore cancellation exceptions during disposal
            }
            
            _canBus.FrameReceived -= OnFrameReceived;
            _canBus.FrameSent -= OnFrameSent;
            _frameAvailable.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }

    /// <summary>
    /// Frame direction for sniffing
    /// </summary>
    public enum FrameDirection
    {
        Received,   // Incoming frame from bus
        Sent        // Outgoing frame to bus
    }

    /// <summary>
    /// Captured CAN frame with metadata
    /// </summary>
    public class CanSnifferEntry
    {
        public DateTime Timestamp { get; set; }
        public double RelativeTimeMs { get; set; }
        public SocketCan.CanFrame CanFrame { get; set; }
        public NatFrame? NatFrame { get; set; }
        public bool IsNatFrame { get; set; }
        public string Analysis { get; set; } = "";
        public FrameDirection Direction { get; set; } = FrameDirection.Received;
    }

    /// <summary>
    /// JSON converter for SocketCan.CanFrame
    /// </summary>
    public class CanFrameJsonConverter : System.Text.Json.Serialization.JsonConverter<SocketCan.CanFrame>
    {
        public override SocketCan.CanFrame Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Implementation for reading (if needed)
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, SocketCan.CanFrame value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("CanId", value.CanId);
            writer.WriteNumber("CanDlc", value.CanDlc);
            writer.WriteStartArray("Data");
            for (int i = 0; i < value.CanDlc; i++)
            {
                writer.WriteNumberValue(value.Data[i]);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
