using loxonelinkdotnet.Devices;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using System.Diagnostics;

namespace loxonelinkdotnet.NatProtocol.MessageHandlers;

/// <summary>
/// Handler for Time Sync messages (0x06)
/// </summary>
public class TimeSyncMessageHandler : MessageHandlerBase
{
    public override byte Command => NatCommands.TimeSync;

    public TimeSyncMessageHandler(LoxoneLinkNatDevice extension, ILogger? logger)
        : base(extension, logger)
    {
    }

    public override async Task HandleRequestAsync(NatFrame frame)
    {
        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Received time sync");
        // Time sync is just informational, no response needed
        // TODO:
        await Task.CompletedTask;
    }
}


