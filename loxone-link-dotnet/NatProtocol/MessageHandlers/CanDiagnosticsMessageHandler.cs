using loxonelinkdotnet.Devices;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using System.Diagnostics;

namespace loxonelinkdotnet.NatProtocol.MessageHandlers;

/// <summary>
/// Handler for CAN Diagnostics messages (0x08)
/// </summary>
public class CanDiagnosticsMessageHandler : MessageHandlerBase
{
    public override byte Command => NatCommands.CanDiagnosticsRequest;

    public CanDiagnosticsMessageHandler(LoxoneLinkNatDevice extension, ILogger? logger)
        : base(extension, logger)
    {
    }

    public override async Task HandleRequestAsync(NatFrame frame)
    {
        var branchId = BitConverter.ToUInt16(frame.Data, 0);
        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] CAN diagnostics request for branch {branchId}");

        var responseData = new byte[7];
        BitConverter.GetBytes((ushort)branchId).CopyTo(responseData, 0);
        // Add dummy diagnostics data
        // TODO:
        var responseFrame = new NatFrame(Device.ExtensionNat, Device.DeviceNat, NatCommands.CanDiagnosticsReply, responseData);
        await SendNatFrameAsync(responseFrame);
    }
}


