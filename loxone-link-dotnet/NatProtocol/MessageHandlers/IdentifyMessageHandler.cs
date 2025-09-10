using loxonelinkdotnet.Devices;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using System.Diagnostics;

namespace loxonelinkdotnet.NatProtocol.MessageHandlers;

/// <summary>
/// Handler for Identify messages (0x10)
/// </summary>
public class IdentifyMessageHandler : MessageHandlerBase
{
    public override byte Command => NatCommands.Identify;

    public IdentifyMessageHandler(LoxoneLinkNatDevice extension, ILogger? logger)
        : base(extension, logger)
    {
    }

    public override async Task HandleRequestAsync(NatFrame frame)
    {
        var targetSerial = BitConverter.ToUInt32(frame.Data, 3);
        if (targetSerial == Device.SerialNumber)
        {
            Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Identify request received - flashing LED (simulated)");
            StartIdentify();
        }
        else if (targetSerial == 0x00000000)
        {
            Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Identify request received for serial 0x0000000 - stop flashing LED");
            StopIdentify();
        }
        await Task.CompletedTask;
    }

    public virtual void StartIdentify()
    {

    }

    public virtual void StopIdentify()
    {

    }
}


