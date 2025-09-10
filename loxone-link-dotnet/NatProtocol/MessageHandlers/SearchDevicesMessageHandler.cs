using loxonelinkdotnet.Devices;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using System.Diagnostics;

namespace loxonelinkdotnet.NatProtocol.MessageHandlers;

/// <summary>
/// Handler for Search Devices messages (0xFB)
/// </summary>
public class SearchDevicesMessageHandler : MessageHandlerBase
{
    public override byte Command => NatCommands.SearchDevicesRequest;

    public SearchDevicesMessageHandler(LoxoneLinkNatDevice extension, ILogger? logger)
        : base(extension, logger)
    {
    }

    public override async Task HandleRequestAsync(NatFrame frame)
    {
        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Received search devices request - DeviceId={frame.DeviceId:X2}");

        if (frame.DeviceId == 0x00 || frame.DeviceId == 0xFF)
        {
            Logger?.LogDebug("[SearchDevicesHandler] Extension search request - responding with extension info");
            await RespondForExtensionAsync();
        }
    }

    private async Task RespondForExtensionAsync()
    {
        if (!Device.IsAssigned)
        {
            Logger?.LogDebug("[SearchDevicesHandler] Not responding to search request - not assigned yet");
            return;
        }

        var responseData = new byte[7];
        responseData[0] = 0x00;
        BitConverter.GetBytes(Device.DeviceType).CopyTo(responseData, 1);
        BitConverter.GetBytes(Device.SerialNumber).CopyTo(responseData, 3);

        if (Device is TreeDevice treeDevice)
        {
            responseData[0] = (byte)(treeDevice.Branch == TreeBranches.Right ? 0x01 : 0x80);
        }

        var responseFrame = new NatFrame(Device.ExtensionNat, Device.DeviceNat, NatCommands.SearchDevicesResponse, responseData);
        await SendNatFrameAsync(responseFrame);

        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Sent extension search response: Type=0x{Device.DeviceType:X4} Serial={Device.SerialNumber:X8}");
    }
}


