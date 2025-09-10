using loxonelinkdotnet.Devices;
using loxonelinkdotnet.Devices.Extensions;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;

namespace loxonelinkdotnet.NatProtocol.MessageHandlers;

/// <summary>
/// Handler for CAN Error Request messages (0x19)
/// Responds with CAN Error Reply (0x18) containing branch error information
/// </summary>
public class CanErrorRequestMessageHandler : MessageHandlerBase
{
    public override byte Command => NatCommands.CanErrorRequest;

    public CanErrorRequestMessageHandler(LoxoneLinkNatDevice device, ILogger? logger)
        : base(device, logger)
    {
    }

    public override async Task HandleRequestAsync(NatFrame frame)
    {
        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Received CAN error request");

        // Parse the request to determine which branch is being requested
        var branchId = BitConverter.ToUInt16(frame.Data, 0);
        
        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] CAN error request for branch {branchId}");

        // Send CAN error reply for the requested branch
        if (Device is TreeExtension treeExtension)
        {
            await treeExtension.SendCanErrorReplyAsync((int)branchId);
        }
        else
        {
            // For non-tree extensions, send a generic error reply
            await SendCanErrorReplyAsync((int)branchId);
        }
    }

    /// <summary>
    /// Send CAN error reply for a specific branch (fallback implementation)
    /// </summary>
    private async Task SendCanErrorReplyAsync(int branchId)
    {
        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Sending CAN error reply for branch {branchId}");

        var data = new byte[7];
        BitConverter.GetBytes((ushort)branchId).CopyTo(data, 0);
        data[2] = 0; // Reserved
        data[3] = 0; // Receive errors
        data[4] = 0; // Transmit errors

        var overallErrorBytes = BitConverter.GetBytes(0u);
        data[4] = overallErrorBytes[0];
        data[5] = overallErrorBytes[1];
        data[6] = overallErrorBytes[2];

        var frame = new NatFrame(Device.ExtensionNat, Device.DeviceNat, NatCommands.CanErrorReply, data);
        frame.Val16 = 0x8000;
        frame.Val32 = (uint)(0x00000003 | (branchId << 16)); // Include branch ID in Val32
        await SendNatFrameAsync(frame);
    }
}