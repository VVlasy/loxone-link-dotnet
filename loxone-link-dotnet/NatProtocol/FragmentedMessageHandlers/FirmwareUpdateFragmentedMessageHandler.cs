using loxonelinkdotnet.Devices;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace loxonelinkdotnet.NatProtocol.FragmentedMessageHandlers
{
    public class FirmwareUpdateFragmentedMessageHandler : FragmentedMessageHandlerBase
    {
        public override byte Command => NatCommands.FirmwareUpdate;

        public FirmwareUpdateFragmentedMessageHandler(LoxoneLinkNatDevice extension, ILogger? logger) : base(extension, logger)
        {
        }

        public override async Task HandleRequestAsync(FragmentedNatFrame frame)
        {
            if (Device.FirmwareUpdate == null)
            {
                Device.FirmwareUpdate = new DeviceFirmwareUpdate(Device, Logger);
            }

            await Device.FirmwareUpdate.ProcessFrame(frame);
        }
    }
}
