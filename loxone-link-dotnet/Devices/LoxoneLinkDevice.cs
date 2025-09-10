using loxonelinkdotnet.Can.Adapters;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using Microsoft.Extensions.Logging.Abstractions;
using loxonelinkdotnet.Helpers;
using loxonelinkdotnet.NatProtocol.FragmentedMessageHandlers;
using loxonelinkdotnet.NatProtocol.MessageHandlers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static loxonelinkdotnet.Can.Adapters.SocketCan;

namespace loxonelinkdotnet.Devices
{
    public abstract partial class LoxoneLinkNatDevice : IDisposable
    {
        public const uint InitialFwVersion = 15000620;

        protected readonly ICanInterface? _canBus;
    protected readonly ILogger? _logger;
    // Background wrapper for the provided logger to offload logging work
    private BackgroundLogger? _backgroundLoggerWrapper;
        protected readonly CancellationTokenSource _cancellationTokenSource = new();
        protected readonly Random _random = new();

        // Frame processing queue with sequence-based reordering
        private readonly Queue<SocketCan.CanFrame> _frameQueue = new();
        private readonly SortedDictionary<ulong, SocketCan.CanFrame> _reorderBuffer = new();
        private readonly object _queueLock = new object();
        private readonly SemaphoreSlim _frameAvailable = new SemaphoreSlim(0);
        private Task? _frameProcessingTask;
        private ulong _nextExpectedSequence = 1;
        private const int MAX_REORDER_BUFFER_SIZE = 100;

        // Extension properties

        /// <summary>
        /// a HEX string of an STM32 Device ID, mainly for Loxone Tree Devices
        /// </summary>
        public string Stm32DeviceID { get; set; } = "a55a3928786a9719bcef012d";
        public uint SerialNumber { get; }
        public ushort DeviceType { get; }
        public byte HardwareVersion { get; }
        public uint FirmwareVersion { get; protected set; }
        public virtual byte ExtensionNat { get; internal set; }
        public virtual byte DeviceNat { get; internal set; }
        public bool IsAuthorized { get; set; }
        public string LinkDeviceAlias { get => $"{DeviceName}: {SerialNumber:X8}"; }
        protected abstract string DeviceName { get; }

        // State machine
        protected readonly DeviceStateMachine _stateMachine;

        public DeviceFirmwareUpdate? FirmwareUpdate { get; set; }

        public EventHandler<DeviceFirmwareUpdate>? FirmwareUpdateAvailable;

        // Legacy properties for compatibility
        public bool IsAssigned => _stateMachine.CurrentState != DeviceState.Offline;
        public bool IsOnline => _stateMachine.CurrentState == DeviceState.Online;
        public bool IsParked => _stateMachine.CurrentState == DeviceState.Parked;
        public DeviceState CurrentState => _stateMachine.CurrentState;

        // Legacy timing (now handled by state machine)
        private bool _extensionsOfflineReceived = false;

        // Message handlers
        private readonly Dictionary<byte, IMessageHandler> _messageHandlers = new();
        private readonly Dictionary<byte, IFragmentedMessageHandler> _fragmentedMessageHandlers = new();

        // Fragmented packet handling
        private byte _expectedFragmentCommand = 0;
        private int _expectedFragmentSize = 0;
        private uint _expectedFragmentCrc = 0;
        private int _fragmentSequence = 0;
        private readonly List<byte> _fragmentBuffer = new();

        // Configuration from Miniserver - CRC for first 12 bytes of default config
        private uint _configurationCrc = 0xF7C095CC;
        private ExtensionConfiguration? _configuration;

        /// <summary>
        /// Get the current configuration CRC for use in alive packets
        /// </summary>
        public uint ConfigurationCrc => _configurationCrc;

        /// <summary>
        /// Get the current configuration
        /// </summary>
        public ExtensionConfiguration? Configuration => _configuration;

        /// <summary>
        /// Set the complete configuration
        /// </summary>
        public void SetConfiguration(ExtensionConfiguration configuration)
        {
            _configuration = configuration;

            // Calculate CRC on first 12 bytes of configuration data only
            var configData = new byte[16]; // Standard config size
            configData[0] = configuration.ConfigSize;
            configData[1] = configuration.ConfigVersion;
            configData[2] = configuration.LedSyncOffset;
            configData[3] = configuration.Reserved;
            BitConverter.GetBytes(configuration.OfflineTimeoutSeconds).CopyTo(configData, 4);

            // Extension-specific data (up to 4 bytes)
            if (configuration.ExtensionSpecificData.Length > 0)
            {
                var copyLength = Math.Min(configuration.ExtensionSpecificData.Length, 4);
                Array.Copy(configuration.ExtensionSpecificData, 0, configData, 8, copyLength);
            }

            // Calculate CRC on first 12 bytes only
            var crcData = configData.Take(12).ToArray();
            _configurationCrc = Stm32Crc.Compute(crcData);

            _logger?.LogInformation($"[{LinkDeviceAlias}] Configuration updated: {configuration}");
            _logger?.LogDebug($"[{LinkDeviceAlias}] Calculated CRC on first 12 bytes: 0x{_configurationCrc:X8}");

            // Update state machine timeout
            if (configuration.OfflineTimeoutSeconds > 0)
            {
                _stateMachine.SetOfflineTimeout((int)configuration.OfflineTimeoutSeconds);
            }

            // Notify derived classes about configuration update
            OnConfigurationUpdated(configuration);
        }

        /// <summary>
        /// Called when configuration is updated - override in derived classes
        /// </summary>
        protected virtual void OnConfigurationUpdated(ExtensionConfiguration configuration)
        {
            // Default implementation - can be overridden
        }

        // Configuration
        private const double OfferIntervalMs = 1000; // 1 second
        private const double KeepAliveIntervalMs = 10 * 60 * 1000; // 10 minutes

        /// <summary>
        /// Initialize message handlers
        /// </summary>
        private void InitializeMessageHandlers()
        {
            // Create handlers
            var aliveHandler = new AliveMessageHandler(this, _logger);
            var versionHandler = new VersionRequestMessageHandler(this, _logger);

            var pingHandler = new PingMessageHandler(this, _logger);
            var timeSyncHandler = new TimeSyncMessageHandler(this, _logger);
            var identifyHandler = new IdentifyMessageHandler(this, _logger);
            var extensionsOfflineHandler = new ExtensionsOfflineMessageHandler(this, _logger);
            var searchDevicesHandler = new SearchDevicesMessageHandler(this, _logger);
            var identifyUnknownHandler = new IdentifyUnknownExtensionMessageHandler(this, _logger);
            var webServicesHandler = new WebServicesTextMessageHandler(this, _logger);


            // Register handlers
            RegisterMessageHandler(pingHandler);
            RegisterMessageHandler(aliveHandler);
            RegisterMessageHandler(timeSyncHandler);
            RegisterMessageHandler(identifyHandler);
            RegisterMessageHandler(extensionsOfflineHandler);
            RegisterMessageHandler(searchDevicesHandler);
            RegisterMessageHandler(identifyUnknownHandler);
            RegisterMessageHandler(versionHandler);
            RegisterMessageHandler(webServicesHandler);


            // Create fragmented handlers
            var sendConfigHandler = new SendConfigFragmentedMessageHandler(this, _logger);
            var cryptChallengeHandler = new CryptChallengeFragmentedMessageHandler(this, _logger);
            var firmwareUpdateHandler = new FirmwareUpdateFragmentedMessageHandler(this, _logger);

            // Register fragmented handlers
            RegisterFragmentedMessageHandler(sendConfigHandler);
            RegisterFragmentedMessageHandler(cryptChallengeHandler);
            RegisterFragmentedMessageHandler(firmwareUpdateHandler);
        }

        public LoxoneLinkNatDevice(
        uint serialNumber,
        ushort deviceType,
        byte hardwareVersion,
        uint firmwareVersion,
        ICanInterface? canBus,
        ILogger logger)
        {
            _canBus = canBus;
            // Wrap the provided logger with BackgroundLogger so log calls don't block hot paths
            if (logger != null)
            {
                try
                {
                    _backgroundLoggerWrapper = new BackgroundLogger(logger);
                    _logger = _backgroundLoggerWrapper;
                }
                catch
                {
                    // If wrapper fails for any reason, fall back to the original logger
                    _logger = logger;
                }
            }
            else
            {
                _logger = null;
            }
            SerialNumber = serialNumber;
            DeviceType = deviceType;
            HardwareVersion = hardwareVersion;
            FirmwareVersion = firmwareVersion;
            ExtensionNat = 0x84; // Will be assigned by Miniserver

            // Initialize state machine (use original logger parameter to avoid nullability issues)
            _stateMachine = new DeviceStateMachine(logger ?? (ILogger)NullLogger.Instance, LinkDeviceAlias);
            _stateMachine.StateChanged += OnStateChanged;

            // Subscribe to CAN frame events
            if (_canBus != null)
            {
                _canBus.FrameReceived += OnFrameReceived;
            }

            InitializeMessageHandlers();
        }

        /// <summary>
        /// Handle state machine state changes
        /// </summary>
        protected virtual void OnStateChanged(object? sender, StateChangeEventArgs e)
        {
            _logger?.LogInformation($"[{LinkDeviceAlias}] Device state changed: {e.PreviousState} → {e.NewState} (Reason: {e.Reason})");

            // Handle state-specific actions
            switch (e.NewState)
            {
                case DeviceState.Online:
                    OnDeviceOnline();
                    break;
                case DeviceState.Parked:
                    OnDeviceParked();
                    break;
                case DeviceState.Offline:
                    OnDeviceOffline();
                    break;
            }
        }

        /// <summary>
        /// Called when device transitions to Online state - override in derived classes
        /// </summary>
        protected virtual void OnDeviceOnline()
        {
            _logger?.LogInformation($"[{LinkDeviceAlias}] Device is now ONLINE");
        }

        /// <summary>
        /// Called when device transitions to Parked state - override in derived classes
        /// </summary>
        protected virtual void OnDeviceParked()
        {
            _logger?.LogInformation($"[{LinkDeviceAlias}] Device is now PARKED");
        }

        /// <summary>
        /// Called when device transitions to Offline state - override in derived classes
        /// </summary>
        protected virtual void OnDeviceOffline()
        {
            _logger?.LogWarning($"[{LinkDeviceAlias}] Device is now OFFLINE");
        }

        /// <summary>
        /// this should set a new FW version and reboot the state machine, ideally also store the new firmware version in a persistent way
        /// </summary>
        /// <param name="firmwareUpdate"></param>
        internal virtual void OnDeviceFirmwareUpdate(DeviceFirmwareUpdate firmwareUpdate)
        {
            FirmwareVersion = firmwareUpdate.NewFirmwareVersion;
            _stateMachine.Reset();

            FirmwareUpdateAvailable?.Invoke(this, firmwareUpdate);
        }

        internal void HandleAuthorization()
        {
            _stateMachine.HandleAuthorization();
        }

        protected virtual void OnCanFrameRecieved(SocketCan.CanFrame canFrame)
        {

        }

        protected void RegisterMessageHandler(IMessageHandler handler, bool overwrite = false)
        {
            if (!_messageHandlers.ContainsKey(handler.Command))
            {
                _messageHandlers[handler.Command] = handler;
                _logger?.LogDebug($"[{LinkDeviceAlias}] Registered message handler for command 0x{handler.Command:X2}");
            }
            else
            {
                if (overwrite)
                {
                    _messageHandlers[handler.Command] = handler;
                    _logger?.LogDebug($"[{LinkDeviceAlias}] Overriding handler for command 0x{handler.Command:X2}");
                }
                else
                {
                    throw new InvalidOperationException($"[{LinkDeviceAlias}] Handler is already registered for command 0x{handler.Command:X2}");
                }
            }
        }

        protected void RegisterFragmentedMessageHandler(IFragmentedMessageHandler handler, bool overwrite = false)
        {
            if (!_fragmentedMessageHandlers.ContainsKey(handler.Command))
            {
                _fragmentedMessageHandlers[handler.Command] = handler;
                _logger?.LogDebug($"[{LinkDeviceAlias}] Registered fragmented message handler for command 0x{handler.Command:X2}");
            }
            else
            {
                if (overwrite)
                {
                    _fragmentedMessageHandlers[handler.Command] = handler;
                    _logger?.LogDebug($"[{LinkDeviceAlias}] Overriding fragmented handler for command 0x{handler.Command:X2}");
                }
                else
                {
                    throw new InvalidOperationException($"[{LinkDeviceAlias}] Handler is already registered for command 0x{handler.Command:X2}");
                }
            }
        }

        public virtual bool IsFrameForUs(INatFrame frame)
        {
            return frame.NatId == ExtensionNat || frame.NatId == 0xFF;
        }

        /// <summary>
        /// Reset offer time (for message handlers)
        /// </summary>
        public void ResetOfferTime()
        {
            // Now handled by state machine
            _stateMachine.HandleDeviceOffline();
        }

        /// <summary>
        /// Set extensions offline flag (for message handlers)
        /// </summary>
        public void SetDeviceOffline(bool offline)
        {
            _extensionsOfflineReceived = offline;
            if (offline)
            {
                _stateMachine.HandleDeviceOffline();
            }
        }

        /// <summary>
        /// Start the extension lifecycle
        /// </summary>
        public virtual async Task StartAsync()
        {
            _logger?.LogInformation($"[{LinkDeviceAlias}] Starting Loxone Link Device - Serial: {SerialNumber:X8}");

            // Start CAN receiving
            _canBus?.StartReceiving();

            // Start background tasks
            _ = Task.Run(LifecycleLoopAsync, _cancellationTokenSource.Token);
            _frameProcessingTask = Task.Run(FrameProcessingLoopAsync, _cancellationTokenSource.Token);

            _logger?.LogInformation($"[{LinkDeviceAlias}] Device started, entering offer phase");
            await Task.CompletedTask; // Remove warning
        }

        /// <summary>
        /// Stop the extension
        /// </summary>
        public async Task StopAsync()
        {
            _logger?.LogInformation($"[{LinkDeviceAlias}] Stopping extension");

            _cancellationTokenSource.Cancel();

            // Send offline message
            if (IsOnline)
            {
                await SendNatFrameAsync(new NatFrame(ExtensionNat, DeviceNat, NatCommands.SetOffline, new byte[7]));
            }
        }

        /// <summary>
        /// Main lifecycle loop
        /// </summary>
        private async Task LifecycleLoopAsync()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    // Update state machine
                    _stateMachine.Update();

                    // Handle state-driven behavior
                    await HandleStateDrivenBehaviorAsync();

                    await Task.Delay(1000, _cancellationTokenSource.Token); // 1 second loop interval
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"[{LinkDeviceAlias}] Error in lifecycle loop");
                }
            }
        }

        /// <summary>
        /// Handle behavior based on current device state
        /// </summary>
        private async Task HandleStateDrivenBehaviorAsync()
        {
            switch (_stateMachine.CurrentState)
            {
                case DeviceState.Offline:
                    // Send offer requests if not in extensions offline mode
                    if (!_extensionsOfflineReceived && _stateMachine.ShouldSendOffer(out var nextOfferDelay))
                    {
                        await SendOfferAsync();
                        _logger?.LogDebug($"[{LinkDeviceAlias}] NAT offer sent, next attempt in {nextOfferDelay.TotalMilliseconds:F0}ms");
                    }
                    break;

                case DeviceState.Parked:
                case DeviceState.Online:
                    // Send keep-alive messages
                    if (_stateMachine.ShouldSendKeepAlive(TimeSpan.FromMinutes(10)))
                    {
                        await SendHeartbeatAsync();
                        _logger?.LogDebug($"[{LinkDeviceAlias}] Keep-alive sent");
                    }
                    break;
            }
        }

        /// <summary>
        /// Handle received CAN frames - reorder by sequence number then queue for processing
        /// </summary>
        private void OnFrameReceived(object? sender, SocketCan.CanFrame canFrame)
        {
            if (_cancellationTokenSource.Token.IsCancellationRequested)
                return;

            try
            {
                lock (_queueLock)
                {
                    // Add frame to reorder buffer
                    _reorderBuffer[canFrame.SequenceNumber] = canFrame;

                    // Process frames in sequence order
                    while (_reorderBuffer.ContainsKey(_nextExpectedSequence))
                    {
                        var orderedFrame = _reorderBuffer[_nextExpectedSequence];
                        _reorderBuffer.Remove(_nextExpectedSequence);
                        _frameQueue.Enqueue(orderedFrame);
                        _nextExpectedSequence++;

                        // Signal that a frame is available for processing
                        _frameAvailable.Release();
                    }

                    // Prevent buffer from growing too large - process oldest frames if needed
                    while (_reorderBuffer.Count > MAX_REORDER_BUFFER_SIZE)
                    {
                        var oldestEntry = _reorderBuffer.First();
                        _logger?.LogWarning($"[{LinkDeviceAlias}] Dropping out-of-order frame seq={oldestEntry.Key}, expected={_nextExpectedSequence}");
                        _reorderBuffer.Remove(oldestEntry.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"[{LinkDeviceAlias}] Error processing received frame sequence");
            }
        }

        /// <summary>
        /// Background task to process queued frames
        /// </summary>
        private async Task FrameProcessingLoopAsync()
        {
            _logger?.LogDebug($"[{LinkDeviceAlias}] Frame processing loop started");

            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    // Wait for a frame to be available
                    await _frameAvailable.WaitAsync(_cancellationTokenSource.Token);

                    // Process ALL available frames in strict FIFO order
                    while (true)
                    {
                        SocketCan.CanFrame? frameToProcess = null;

                        // Dequeue the next frame
                        lock (_queueLock)
                        {
                            if (_frameQueue.Count > 0)
                            {
                                frameToProcess = _frameQueue.Dequeue();
                            }
                        }

                        // If no more frames, break and wait for next semaphore signal
                        if (!frameToProcess.HasValue)
                            break;

                        // Process the frame synchronously to maintain strict order
                        await ProcessCanFrameAsync(frameToProcess.Value);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"[{LinkDeviceAlias}] Error in frame processing loop");
                    // Continue processing other frames even if one fails
                }
            }

            _logger?.LogDebug($"[{LinkDeviceAlias}] Frame processing loop stopped");
        }

        /// <summary>
        /// Process a single CAN frame
        /// </summary>
        private async Task ProcessCanFrameAsync(SocketCan.CanFrame canFrame)
        {
            try
            {
                OnCanFrameRecieved(canFrame);

                var natFrame = NatFrame.FromCanFrame(canFrame);
                if (natFrame != null)
                {
                    await HandleIncomingNatFrameAsync(natFrame);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"[{LinkDeviceAlias}] Error processing CAN frame");
            }
        }

        /// <summary>
        /// Send NAT offer request
        /// </summary>
        public virtual async Task SendOfferAsync()
        {
            _logger?.LogDebug($"[{LinkDeviceAlias}] Sending NAT offer request");

            var data = new byte[7];
            data[0] = 0x00;
            BitConverter.GetBytes(DeviceType).CopyTo(data, 1); // Hardware type (2 bytes)
            BitConverter.GetBytes(SerialNumber).CopyTo(data, 3); // Serial number (4 bytes) at offset 3

            if (this is TreeDevice treeDevice)
            {
                data[0] = (byte)(treeDevice.Branch == TreeBranches.Right ? 0x01 : 0x80);
            }

            var frame = new NatFrame(ExtensionNat, DeviceNat, NatCommands.NatOfferRequest, data);
            var canFrame = frame.ToCanFrame();
            await SendNatFrameAsync(frame);

            _logger?.LogDebug($"[{LinkDeviceAlias}] Sent NAT offer: Type=0x{DeviceType:X4} Serial={SerialNumber:X8}");
        }

        /// <summary>
        /// Send heartbeat response
        /// </summary>
        public async Task SendHeartbeatAsync()
        {
            _logger?.LogDebug($"[{LinkDeviceAlias}] Sending heartbeat with config CRC: 0x{ConfigurationCrc:X8}");

            var data = new byte[7];
            data[0] = ResetReasons.AlivePackage;
            // Bytes 1-2: Configuration version (little-endian)
            var configVersion = (ushort)(_configuration?.ConfigVersion ?? 0);
            var versionBytes = BitConverter.GetBytes(configVersion);
            data[1] = versionBytes[0];
            data[2] = versionBytes[1];
            // Bytes 3-6: Configuration CRC (little-endian)
            var crcBytes = BitConverter.GetBytes(ConfigurationCrc);
            Array.Copy(crcBytes, 0, data, 3, 4);

            var frame = new NatFrame(ExtensionNat, DeviceNat, NatCommands.Alive, data);
            await SendNatFrameAsync(frame);
        }

        /// <summary>
        /// Handle NAT assignment from Miniserver
        /// </summary>
        public virtual async Task HandleAssignAsync(NatFrame frame)
        {
            var assignedNat = frame.Data[0]; // New NAT ID or Device NAT
            var isParked = frame.Data[1] != 0;

            // Serial number is at offset 3
            var assignedSerial = BitConverter.ToUInt32(frame.Data, 3);

            _logger?.LogDebug($"[{LinkDeviceAlias}] Assignment: NAT/DeviceNAT={assignedNat:X2} Parked={isParked} Serial={assignedSerial:X8}");

            // Check if this is for this device
            if (assignedSerial == SerialNumber)
            {
                // For extensions, this sets ExtensionNat, for tree devices, this sets DeviceNat
                if (this is TreeDevice)
                {
                    DeviceNat = assignedNat;
                    _logger?.LogInformation($"[{LinkDeviceAlias}] Tree Device assigned Device NAT: DeviceNAT={DeviceNat:X2} " +
                                          $"Parked={isParked} Serial={assignedSerial:X8} (Extension NAT={ExtensionNat:X2})");
                }
                else
                {
                    ExtensionNat = assignedNat;
                    _logger?.LogInformation($"[{LinkDeviceAlias}] Extension assigned NAT: ExtensionNAT={ExtensionNat:X2} " +
                                          $"Parked={isParked} Serial={assignedSerial:X8}");
                }

                // Update state machine
                _stateMachine.HandleNatAssignment(isParked);

                // Send start info
                await SendStartInfoAsync();
            }
            else
            {
                _logger?.LogDebug($"[{LinkDeviceAlias}] Ignoring assignment for different device: Serial={assignedSerial:X8}");
            }
        }

        /// <summary>
        /// Send start info after assignment
        /// </summary>
        public virtual async Task SendStartInfoAsync()
        {
            _logger?.LogInformation($"[{LinkDeviceAlias}] Sending start info");

            // Create Start Info response (20 bytes fragmented)
            var startInfoData = new byte[20];

            // Firmware version (4 bytes)
            BitConverter.GetBytes(FirmwareVersion).CopyTo(startInfoData, 0);

            // Unknown (4 bytes, typically 0)
            Array.Clear(startInfoData, 4, 4);

            // Configuration CRC (4 bytes) - use the CRC received from Miniserver
            BitConverter.GetBytes(_configurationCrc).CopyTo(startInfoData, 8);

            // Serial number (4 bytes)
            BitConverter.GetBytes(SerialNumber).CopyTo(startInfoData, 12);

            // Reason (1 byte) - use 0x02 to match real device  
            startInfoData[16] = IsParked ? ResetReasons.Pairing : ResetReasons.PowerOnReset; // Reset reason used by real device

            // Hardware type and additional data (4 bytes total for the last fragment)
            BitConverter.GetBytes(DeviceType).CopyTo(startInfoData, 17);
            startInfoData[19] = 0x02;  // Hardware version

            _logger?.LogDebug($"[{LinkDeviceAlias}] Bytes 17-19: {startInfoData[17]:X2} {startInfoData[18]:X2} {startInfoData[19]:X2}");

            // Debug logging
            _logger?.LogDebug($"[{LinkDeviceAlias}] Start Info - FW Version: {FirmwareVersion} (0x{FirmwareVersion:X8})");
            _logger?.LogDebug($"[{LinkDeviceAlias}] Start Info - HW Version: {HardwareVersion}");
            _logger?.LogDebug($"[{LinkDeviceAlias}] Start Info - Device Type: 0x{DeviceType:X4}");
            _logger?.LogDebug($"[{LinkDeviceAlias}] Start Info - Serial: 0x{SerialNumber:X8}");
            _logger?.LogDebug($"[{LinkDeviceAlias}] Start Info - Config CRC: 0x{_configurationCrc:X8}");
            _logger?.LogDebug($"[{LinkDeviceAlias}] Start Info Data: {string.Join(" ", startInfoData.Select(b => $"{b:X2}"))}");

            // Send as fragmented package
            await SendFragmentedDataAsync(NatCommands.StartInfo, startInfoData);
        }

        /// <summary>
        /// Handle incoming NAT frame
        /// </summary>
        internal virtual async Task HandleIncomingNatFrameAsync(NatFrame frame)
        {
            _logger?.LogDebug($"[{LinkDeviceAlias}] Received: {frame}");

            // Check if this frame is for us (extension) or for Tree devices
            if (IsFrameForUs(frame) == false) // 0xFF = broadcast, 0x00 = unassigned/general
            {
                _logger?.LogDebug($"[{LinkDeviceAlias}] Frame not for us: NatId={frame.NatId:X2}, ExtensionNat={ExtensionNat:X2}");
                return;
            }

            // Reset communication timeout for assigned devices
            if (IsAssigned)
            {
                _stateMachine.ResetTimeout();
            }

            // Handle fragmented packets first
            if (frame.Command == NatCommands.FragmentStart)
            {
                await HandleFragmentStartAsync(frame);
                return;
            }
            else if (frame.Command == NatCommands.FragmentData)
            {
                await HandleFragmentDataAsync(frame);
                return;
            }

            // Handle extension-level commands (DeviceId == 0x00 or 0xFF)
            await HandleLinkDeviceCommandAsync(frame);
        }

        /// <summary>
        /// Handle extension-level commands
        /// </summary>
        public async Task<bool> HandleLinkDeviceCommandAsync(NatFrame frame)
        {
            _logger?.LogDebug($"[{LinkDeviceAlias}] Handling LinkDevice command: 0x{frame.Command:X2}");

            // Handle special cases that aren't in message handlers
            switch (frame.Command)
            {
                case NatCommands.NatOfferConfirm:
                    await HandleAssignAsync(frame);
                    return true;
                case NatCommands.SendConfig:
                    await HandleSendConfigAsync(frame);
                    return true;
            }

            // Delegate to message handlers
            if (_messageHandlers.TryGetValue(frame.Command, out var handler))
            {
                await handler.HandleRequestAsync(frame);
                return true;
            }
            else
            {
                _logger?.LogWarning($"[{LinkDeviceAlias}] Unhandled command: 0x{frame.Command:X2}");
            }

            return false;
        }

        /// <summary>
        /// Send fragmented data (for messages > 7 bytes)
        /// </summary>
        public async Task SendFragmentedDataAsync(byte command, byte[] data)
        {
            // Send fragment header
            var headerData = new byte[7];
            headerData[0] = command; // Original command

            // Report size
            ushort reportedSize = (ushort)data.Length;

            BitConverter.GetBytes(reportedSize).CopyTo(headerData, 1); // Size
            BitConverter.GetBytes(CalculateCrc32(data)).CopyTo(headerData, 3); // CRC32

            var headerFrame = new NatFrame(ExtensionNat, DeviceNat, NatCommands.FragmentStart, headerData);
            headerFrame.IsFragmented = true; // Set fragmented bit for fragment start
            await SendNatFrameAsync(headerFrame);

            // Send data in 7-byte chunks
            for (int offset = 0; offset < data.Length; offset += 7)
            {
                var chunkSize = Math.Min(7, data.Length - offset);
                var chunkData = new byte[7];
                Array.Copy(data, offset, chunkData, 0, chunkSize);

                // For fragmented data frames, always use device ID 0x00 - payload goes entirely in Data field
                var dataFrame = new NatFrame(ExtensionNat, DeviceNat, NatCommands.FragmentData, chunkData);
                dataFrame.IsFragmented = true; // Set fragmented bit for fragment data
                await SendNatFrameAsync(dataFrame);

                // Small delay between fragments to match real device timing
                await Task.Delay(100);
            }
        }

        /// <summary>
        /// Send NAT frame to CAN bus
        /// </summary>
        public virtual async Task SendNatFrameAsync(NatFrame frame)
        {
            var canFrame = frame.ToCanFrame();
            if (_canBus == null)
            {
                _logger?.LogError(new InvalidOperationException("No CAN device was added during creation"), $"[{LinkDeviceAlias}] Cannot send NAT frame, CAN bus not initialized");
                return;
            }
            await _canBus.SendAsync(canFrame);
            //TODO: add log sendframe
            //_logger?.LogDebug($"[{LinkDeviceAlias}] TX NAT: CAN_ID=0x{canFrame.CanId:X8} Ext={frame.NatId:X2} Dev={frame.DeviceId:X2} Cmd=0x{frame.Command:X2} Data=[{string.Join(" ", frame.Data.Select(b => $"{b:X2}"))}] B0=0x{frame.B0:X2} Val16=0x{frame.Val16:X4} Val32=0x{frame.Val32:X8}");
        }

        /// <summary>
        /// Send a webservices text response 
        /// </summary>
        public async Task SendWebServicesResponseAsync(string response)
        {
            if (string.IsNullOrEmpty(response))
                return;

            _logger?.LogDebug($"[{LinkDeviceAlias}] Sending WebServices response: {response.Replace("\r\n", "\\r\\n")}");

            // Format: [DeviceID][StringLength][String...][0]
            var responseBytes = Encoding.UTF8.GetBytes(response);
            var data = new byte[responseBytes.Length + 3];
            data[0] = 0x00; // Device ID
            data[1] = (byte)(responseBytes.Length + 1); // String length including null terminator
            Array.Copy(responseBytes, 0, data, 2, responseBytes.Length);
            data[data.Length - 1] = 0x00; // Null terminator

            await SendFragmentedDataAsync(NatCommands.WebServiceRequest, data);
        }

        /// <summary>
        /// Calculate CRC32 using STM32 hardware CRC compatible algorithm
        /// Cut down array to multiple of 4 bytes instead of padding
        /// </summary>
        private static uint CalculateCrc32(byte[] data)
        {
            // Cut down to multiple of 4 for STM32 CRC (round down instead of up)
            var trimmedLength = data.Length & ~3; // Round down to nearest multiple of 4
            if (trimmedLength == 0) trimmedLength = 4; // Ensure at least 4 bytes

            var crcData = new byte[trimmedLength];
            Array.Copy(data, crcData, Math.Min(data.Length, trimmedLength));

            return Stm32Crc.Compute(crcData);
        }

        /// <summary>
        /// Handle fragmented packet start (0xF0)
        /// </summary>
        private async Task HandleFragmentStartAsync(NatFrame frame)
        {
            _expectedFragmentCommand = frame.Data[0];
            _expectedFragmentSize = BitConverter.ToUInt16(frame.Data, 1);
            _expectedFragmentCrc = BitConverter.ToUInt32(frame.Data, 3);

            _fragmentBuffer.Clear();
            _fragmentSequence = 0;

            _logger?.LogDebug($"[{LinkDeviceAlias}] Fragment start: Cmd=0x{_expectedFragmentCommand:X2} Size={_expectedFragmentSize} CRC=0x{_expectedFragmentCrc:X8}");

            await Task.CompletedTask;
        }

        /// <summary>
        /// Handle fragmented packet data (0xF1)
        /// </summary>
        private async Task HandleFragmentDataAsync(NatFrame frame)
        {
            _fragmentSequence++;
            var fragmentData = frame.Data.Take(Math.Min(_expectedFragmentSize - _fragmentBuffer.Count, frame.Data.Length)).ToArray();

            _logger?.LogDebug($"[{LinkDeviceAlias}] Fragment data #{_fragmentSequence}: [{string.Join(" ", fragmentData.Select(b => $"{b:X2}"))}]");

            // Calculate how many bytes we still need
            var bytesNeeded = _expectedFragmentSize - _fragmentBuffer.Count;

            // Only add the bytes we need, don't exceed the expected size
            var bytesToAdd = Math.Min(bytesNeeded, frame.Data.Length);
            _fragmentBuffer.AddRange(frame.Data.Take(bytesToAdd));
            _logger?.LogDebug($"[{LinkDeviceAlias}] Fragment data: {_fragmentBuffer.Count}/{_expectedFragmentSize} bytes received (size field: {_expectedFragmentSize})");

            // Check if we have received all expected data
            if (_fragmentBuffer.Count >= _expectedFragmentSize)
            {
                var completeData = _fragmentBuffer.Take(_expectedFragmentSize).ToArray();
                _logger?.LogDebug($"[{LinkDeviceAlias}] Fragment complete: {completeData.Length} bytes");

                // Verify CRC32 of the complete data
                var calculatedCrc = CalculateCrc32(completeData);
                if (calculatedCrc != _expectedFragmentCrc)
                {
                    _logger?.LogDebug($"[{LinkDeviceAlias}] Complete fragment data ({completeData.Length} bytes): {string.Join(" ", completeData.Select(b => $"{b:X2}"))}");

                    _logger?.LogError(new InvalidDataException("Fragment CRC mismatch"), $"[Extension] Fragment CRC mismatch! Expected: 0x{_expectedFragmentCrc:X8}, Calculated: 0x{calculatedCrc:X8}");

                    // Reset fragment state and return without processing
                    _fragmentBuffer.Clear();
                    _expectedFragmentCommand = 0;
                    _expectedFragmentSize = 0;
                    _expectedFragmentCrc = 0;
                    _fragmentSequence = 0;
                    return;
                }

                _logger?.LogDebug($"[{LinkDeviceAlias}] Fragment CRC verification passed: 0x{calculatedCrc:X8}");

                // Process the complete fragmented command
                var fragmentedNatFrame = new FragmentedNatFrame()
                {
                    NatId = frame.NatId,
                    DeviceId = frame.DeviceId,
                    Command = _expectedFragmentCommand,
                    Data = completeData,
                    Crc32 = calculatedCrc
                };

                await HandleCompleteFragmentAsync(fragmentedNatFrame);

                // Reset fragment state
                _fragmentBuffer.Clear();
                _expectedFragmentCommand = 0;
                _expectedFragmentSize = 0;
                _expectedFragmentCrc = 0;
                _fragmentSequence = 0;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Handle complete fragmented packet
        /// </summary>
        internal virtual async Task<bool> HandleCompleteFragmentAsync(FragmentedNatFrame frame)
        {
            switch (frame.Command)
            {
                case NatCommands.StartInfo:
                    // We don't expect to receive start info, we send it
                    _logger?.LogWarning($"[{LinkDeviceAlias}] Received unexpected Start Info fragment");
                    return true;
            }

            // Delegate to message handlers
            if (_fragmentedMessageHandlers.TryGetValue(frame.Command, out var handler))
            {
                await handler.HandleRequestAsync(frame);
                return true;
            }
            else
            {
                _logger?.LogWarning($"[{LinkDeviceAlias}] Unhandled fragment command: 0x{frame.Command:X2}");
            }

            return false;
        }

        /// <summary>
        /// Handle Send Config command (can be fragmented or not)
        /// </summary>
        private async Task HandleSendConfigAsync(NatFrame frame)
        {
            // This should not be called for fragmented packets, but just in case
            _logger?.LogDebug($"[{LinkDeviceAlias}] Received Send Config command (non-fragmented)");
            await Task.CompletedTask;
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();

            // Wait for frame processing task to complete
            try
            {
                _frameProcessingTask?.Wait(1000);
            }
            catch (AggregateException)
            {
                // Ignore cancellation exceptions during disposal
            }

            // Unsubscribe from events
            if (_stateMachine != null)
                _stateMachine.StateChanged -= OnStateChanged;

            if (_canBus != null)
            {
                _canBus.FrameReceived -= OnFrameReceived;
                _canBus.Dispose();
            }

            // Dispose semaphore
            _frameAvailable.Dispose();
            // Dispose background logger wrapper if created
            _backgroundLoggerWrapper?.Dispose();
        }
    }
}
