using loxonelinkdotnet.Devices;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using System.Diagnostics;

namespace loxonelinkdotnet.NatProtocol.MessageHandlers;

/// <summary>
/// Handler for Ping messages (0x02)
/// </summary>
public class PingMessageHandler : MessageHandlerBase
{
    public override byte Command => NatCommands.Ping;

    public PingMessageHandler(LoxoneLinkNatDevice extension, ILogger? logger)
        : base(extension, logger)
    {
    }

    public override async Task HandleRequestAsync(NatFrame frame)
    {
        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Responding to ping");
        var responseFrame = new NatFrame(Device.ExtensionNat, Device.DeviceNat, NatCommands.Pong, new byte[7]);
        await SendNatFrameAsync(responseFrame);
    }
}


