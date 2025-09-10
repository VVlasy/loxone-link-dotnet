using loxonelinkdotnet.Devices;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using System.Diagnostics;

namespace loxonelinkdotnet.NatProtocol.MessageHandlers;

/// <summary>
/// Handler for Extensions Offline messages (0x0A)
/// </summary>
public class ExtensionsOfflineMessageHandler : MessageHandlerBase
{
    public override byte Command => NatCommands.ExtensionsOffline;

    public ExtensionsOfflineMessageHandler(LoxoneLinkNatDevice extension, ILogger? logger)
        : base(extension, logger)
    {
    }

    public override async Task HandleRequestAsync(NatFrame frame)
    {
        Logger?.LogWarning($"[{Device.LinkDeviceAlias}] Extensions Offline command received - entering startup phase");

        Device.SetDeviceOffline(true);
        Device.IsAuthorized = false;

        Logger?.LogInformation($"[{Device.LinkDeviceAlias}] Extension is now OFFLINE - will wait for Miniserver to complete startup");

        await Task.CompletedTask;
    }
}


