using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;

namespace loxonelinkdotnet.Devices
{
    public class DeviceFirmwareUpdate
    {
        public enum FwUpdateState
        {
            Idle,
            Receiving,
            ReceivingCrc,
            Verifying,
            Completed,
            Failed
        }

        public enum FwUpdateCommands
        {
            // Server -> Device
            FirmwareData = 0x01,
            FirmwareCrc = 0x02,
            VerifyUpdate = 0x03,
            VerifyAndRestart = 0x04,
            //
            VerificationSuccess = 0x80,
            VerificationFailed = 0x81,
        }

        public struct FirmwarePage
        {
            public ushort PageNumber { get; init; }
            public uint Crc32 { get; init; }
            public int PageStartIndex { get; init; }
            public int PageLength { get; init; }
        }

        private ILogger? _logger;

        public LoxoneLinkNatDevice Device { get; init; }

        public FwUpdateState State { get; private set; } = FwUpdateState.Idle;

        private byte[] _firmwareData = [];
        public byte[] FirmwareData { get => _firmwareData; set => _firmwareData = value; }

        public Dictionary<ushort, FirmwarePage> Pages { get; } = new Dictionary<ushort, FirmwarePage>();
        public uint NewFirmwareVersion { get; internal set; }
        public ushort FirmwareDeviceType { get; private set; }

        public DeviceFirmwareUpdate(LoxoneLinkNatDevice device, ILogger? logger)
        {
            Device = device;
            _logger = logger;
        }

        public async Task ProcessFrame(FragmentedNatFrame frame)
        {
            _logger?.LogDebug($"[{Device.LinkDeviceAlias}] Received firmware update packet: {frame.Data.Length} bytes");

            var dataSize = frame.Data[0];
            var command = (FwUpdateCommands)frame.Data[1];
            var deviceType = BitConverter.ToUInt16(frame.Data, 2);
            var newFirmwareVersion = BitConverter.ToUInt32(frame.Data, 4);
            var pageNumber = BitConverter.ToUInt16(frame.Data, 8);
            var index = BitConverter.ToUInt16(frame.Data, 10);
            var data = frame.Data[12..(dataSize - 1)];

            if (deviceType != Device.DeviceType)
            {
                _logger?.LogWarning($"[{Device.LinkDeviceAlias}] Firmware update device type mismatch: expected {Device.DeviceType:X4}, got {deviceType:X4}");
                return;
            }

            switch (command)
            {
                case FwUpdateCommands.FirmwareData:
                    // Receiving firmware data
                    if (State == FwUpdateState.Idle)
                    {
                        State = FwUpdateState.Receiving;
                        _logger?.LogInformation($"[{Device.LinkDeviceAlias}] Starting firmware update to version {newFirmwareVersion:X8}");
                        _firmwareData = [];
                        Pages.Clear();
                    }
                    else if (State != FwUpdateState.Receiving)
                    {
                        _logger?.LogWarning($"[{Device.LinkDeviceAlias}] Received firmware data while not in receiving state");
                        return;
                    }

                    // Store the data chunk
                    Array.Resize(ref _firmwareData, _firmwareData.Length + data.Length);
                    Array.Copy(data, 0, _firmwareData, _firmwareData.Length - data.Length, data.Length);

                    // Create or update the page entry
                    if (!Pages.ContainsKey(pageNumber))
                    {
                        // New page
                        var page = new FirmwarePage
                        {
                            PageNumber = pageNumber,
                            Crc32 = 0, // Will be set later
                            PageStartIndex = _firmwareData.Length - data.Length,
                            PageLength = data.Length
                        };
                        Pages[pageNumber] = page;
                    }
                    else
                    {
                        var page = Pages[pageNumber];
                        // Update existing page length
                        page = new FirmwarePage
                        {
                            PageNumber = page.PageNumber,
                            Crc32 = page.Crc32,
                            PageStartIndex = page.PageStartIndex,
                            PageLength = page.PageLength + data.Length
                        };
                        Pages[pageNumber] = page;
                    }
                    break;
                case FwUpdateCommands.FirmwareCrc:
                    if (State != FwUpdateState.Receiving)
                    {
                        _logger?.LogWarning($"[{Device.LinkDeviceAlias}] Received firmware CRC while not in receiving state");
                        return;
                    }
                    State = FwUpdateState.ReceivingCrc;

                    // Recieve the CRC32 for each page

                    if (Pages.ContainsKey(pageNumber))
                    {
                        var crcPage = Pages[pageNumber];
                        crcPage = new FirmwarePage
                        {
                            PageNumber = crcPage.PageNumber,
                            Crc32 = BitConverter.ToUInt32(data, 0),
                            PageStartIndex = crcPage.PageStartIndex,
                            PageLength = crcPage.PageLength
                        };
                        Pages[pageNumber] = crcPage;
                    }
                    else
                    {
                        _logger?.LogWarning($"[{Device.LinkDeviceAlias}] Received CRC for unknown page {pageNumber}");
                    }
                    break;
                case FwUpdateCommands.VerifyUpdate:
                case FwUpdateCommands.VerifyAndRestart:
                    // Verify the received firmware
                    if (State != FwUpdateState.ReceivingCrc)
                    {
                        _logger?.LogWarning($"[{Device.LinkDeviceAlias}] Received verify command while not in receiving CRC state");
                        return;
                    }
                    State = FwUpdateState.Verifying;

                    NewFirmwareVersion = newFirmwareVersion;
                    FirmwareDeviceType = deviceType;

                    // All data received, verify CRCs
                    bool allCrcsValid = true;
                    ushort failedPage = 0;
                    foreach (var p in Pages.Values)
                    {
                        var pageData = _firmwareData[p.PageStartIndex..(p.PageStartIndex + p.PageLength)];
                        uint computedCrc = Stm32Crc.Compute(pageData);
                        if (computedCrc != p.Crc32)
                        {
                            _logger?.LogError(new InvalidDataException($"CRC mismatch on page {p.PageNumber}: expected {p.Crc32:X8}, got {computedCrc:X8}"), $"[{Device.LinkDeviceAlias}] CRC mismatch on page {p.PageNumber}: expected {p.Crc32:X8}, got {computedCrc:X8}");
                            allCrcsValid = false;
                            failedPage = p.PageNumber;
                            break;
                        }
                    }

                    var firmwareCrc = Stm32Crc.Compute(_firmwareData);
                    byte[] responseData = new byte[] {
                            0x00,
                            (byte)(allCrcsValid ? FwUpdateCommands.VerificationSuccess : FwUpdateCommands.VerificationFailed),

                            (byte)(Device.DeviceType & 0xFF),
                            (byte)((Device.DeviceType >> 8) & 0xFF),

                            (byte)(newFirmwareVersion & 0xFF),
                            (byte)((newFirmwareVersion >> 8) & 0xFF),
                            (byte)((newFirmwareVersion >> 16) & 0xFF),
                            (byte)((newFirmwareVersion >> 24) & 0xFF),

                            (byte)(failedPage & 0xFF),
                            (byte)((failedPage >> 8) & 0xFF),

                            0x00,0x00, // index

                            // Data crc32
                            (byte)(firmwareCrc & 0xFF),
                            (byte)((firmwareCrc >> 8) & 0xFF),
                            (byte)((firmwareCrc >> 16) & 0xFF),
                            (byte)((firmwareCrc >> 24) & 0xFF),
                        };

                    // set data size
                    responseData[0] = (byte)(responseData.Length);
                    if (command == FwUpdateCommands.VerifyUpdate)
                    {
                        // verify and restarts does not want a response from device
                        await Device.SendFragmentedDataAsync(NatCommands.FirmwareUpdate, responseData);
                    }

                    if (allCrcsValid)
                    {
                        State = FwUpdateState.Completed;
                        _logger?.LogInformation($"[{Device.LinkDeviceAlias}] Firmware update verified successfully, ready to apply");
                        _ = Task.Factory.StartNew(() => Device.OnDeviceFirmwareUpdate(this));
                        Device.FirmwareUpdate = null; // clear reference to allow new updates
                    }
                    else
                    {
                        State = FwUpdateState.Failed;
                    }


                    break;
                case FwUpdateCommands.VerificationSuccess:
                case FwUpdateCommands.VerificationFailed:
                    // these are responses from the device, should not be received here
                    break;
                default:
                    _logger?.LogWarning($"[{Device.LinkDeviceAlias}] Unknown firmware update command: {command}");
                    break;
            }


            // Optionally send an acknowledgment back to the sender
            await Device.SendHeartbeatAsync();
        }
    }
}