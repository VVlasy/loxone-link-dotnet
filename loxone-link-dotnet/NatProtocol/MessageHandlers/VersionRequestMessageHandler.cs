using System.Diagnostics;
using loxonelinkdotnet.NatProtocol.Core;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.Devices;
using Microsoft.Extensions.Logging;

namespace loxonelinkdotnet.NatProtocol.MessageHandlers;

/// <summary>
/// Handler for Version Request messages (0x01)
/// </summary>
public class VersionRequestMessageHandler : MessageHandlerBase
{
    public override byte Command => NatCommands.VersionRequest;

    public VersionRequestMessageHandler(LoxoneLinkNatDevice extension, ILogger? logger)
        : base(extension, logger)
    {
    }

    public override async Task HandleRequestAsync(NatFrame frame)
    {
        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Received version request - DeviceId={frame.DeviceId:X2} Data={string.Join(" ", frame.Data.Select(b => $"{b:X2}"))}");

        if (frame.Data.Length >= 7)
        {
            var requestedSerial = BitConverter.ToUInt32(frame.Data, 3);

            if (requestedSerial == Device.SerialNumber)
            {
                Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Version request for serial: 0x{requestedSerial:X8}");
                Logger?.LogDebug("[VersionRequestHandler] Sending extension version info");
                await SendVersionInfoAsync();
            }
        }
    }

    /// <summary>
    /// Send Version Info response
    /// </summary>
    public async Task SendVersionInfoAsync()
    {
        Logger?.LogInformation("[VersionRequestHandler] Sending version info");

        // Version info structure: 20 bytes total
        // 0-3: Firmware version (uint32)
        // 4-7: Unknown/reserved (uint32) 
        // 8-11: Configuration CRC (uint32)
        // 12-15: Serial number (uint32)
        // 16: Start reason (byte)
        // 17-18: Hardware type (uint16)
        // 19: Hardware version (byte)
        var versionData = new byte[20];
        BitConverter.GetBytes(Device.FirmwareVersion).CopyTo(versionData, 0);
        BitConverter.GetBytes((uint)0).CopyTo(versionData, 4);  // Unknown/reserved
        BitConverter.GetBytes(Device.ConfigurationCrc).CopyTo(versionData, 8);  // Configuration CRC
        BitConverter.GetBytes(Device.SerialNumber).CopyTo(versionData, 12);
        versionData[16] = ResetReasons.Pairing;
        BitConverter.GetBytes(Device.DeviceType).CopyTo(versionData, 17);
        versionData[19] = Device.HardwareVersion;

        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Version Info Data: {string.Join(" ", versionData.Select(b => $"{b:X2}"))}");
        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Version Info - Config CRC: 0x{Device.ConfigurationCrc:X8}");

        await SendFragmentedDataAsync(NatCommands.VersionInfo, versionData);
    }

    /// <summary>
    /// Send Version Info response for a specific Tree device
    /// </summary>
    public async Task SendTreeDeviceVersionInfoAsync(TreeDevice device)
    {
        Logger?.LogInformation($"[{Device.LinkDeviceAlias}] Sending Tree device version info for device 0x{device.DeviceType:X4} serial 0x{device.SerialNumber:X8}");

        // Version info structure: 20 bytes total
        // 0-3: Firmware version (uint32)
        // 4-7: Unknown/reserved (uint32) 
        // 8-11: Configuration CRC (uint32)
        // 12-15: Serial number (uint32)
        // 16: Start reason (byte)
        // 17-18: Hardware type (uint16)
        // 19: Hardware version (byte)
        var versionData = new byte[20];
        BitConverter.GetBytes(Device.FirmwareVersion).CopyTo(versionData, 0);
        BitConverter.GetBytes((uint)0).CopyTo(versionData, 4);  // Unknown/reserved
        BitConverter.GetBytes(Device.ConfigurationCrc).CopyTo(versionData, 8);  // Configuration CRC
        BitConverter.GetBytes(device.SerialNumber).CopyTo(versionData, 12);
        versionData[16] = ResetReasons.Pairing;
        BitConverter.GetBytes(device.DeviceType).CopyTo(versionData, 17);
        versionData[19] = Device.HardwareVersion;

        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Tree Device Version Info Data: {string.Join(" ", versionData.Select(b => $"{b:X2}"))}");

        await SendFragmentedDataAsync(NatCommands.VersionInfo, versionData);
    }
}


