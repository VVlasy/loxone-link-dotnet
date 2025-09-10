using loxonelinkdotnet.Devices;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using System.Diagnostics;

namespace loxonelinkdotnet.NatProtocol.MessageHandlers;

/// <summary>
/// Handler for Identify Unknown Extension messages (0xF4)
/// </summary>
public class IdentifyUnknownExtensionMessageHandler : MessageHandlerBase
{
    public override byte Command => 0xF4;

    public IdentifyUnknownExtensionMessageHandler(LoxoneLinkNatDevice extension, ILogger? logger)
        : base(extension, logger)
    {
    }

    public override async Task HandleRequestAsync(NatFrame frame)
    {
        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Received identify unknown extension command");

        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Extension discovery request");
        Device.SetDeviceOffline(false);

        if (!Device.IsAssigned)
        {
            Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Not assigned yet - will resume sending NAT offer requests");
            Device.ResetOfferTime();
        }
        else
        {
            // For Tree Extensions, trigger tree devices to send their offers
            if (Device is Devices.Extensions.TreeExtension treeExtension)
            {
                Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Triggering tree devices to announce themselves");
                await treeExtension.TriggerDeviceOffersAsync();
            }
        }

        await Task.CompletedTask;
    }
}


