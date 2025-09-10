using System.Diagnostics;
using loxonelinkdotnet.NatProtocol.Core;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.Devices;
using Microsoft.Extensions.Logging;

namespace loxonelinkdotnet.NatProtocol.MessageHandlers;

/// <summary>
/// Handler for Alive messages (0x05)
/// </summary>
public class AliveMessageHandler : MessageHandlerBase
{
    public override byte Command => NatCommands.Alive;

    public AliveMessageHandler(LoxoneLinkNatDevice extension, ILogger? logger)
        : base(extension, logger)
    {
    }

    public override async Task HandleRequestAsync(NatFrame frame)
    {
        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Responding to alive request with config CRC: 0x{Device.ConfigurationCrc:X8}");

        await Device.SendHeartbeatAsync();
    }
}


