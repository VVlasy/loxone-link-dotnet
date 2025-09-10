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
    public class SendConfigFragmentedMessageHandler : FragmentedMessageHandlerBase
    {
        public override byte Command => NatCommands.SendConfig;

        public SendConfigFragmentedMessageHandler(LoxoneLinkNatDevice extension, ILogger? logger) : base(extension, logger)
        {
        }

        public override async Task HandleRequestAsync(FragmentedNatFrame frame)
        {
            Logger?.LogInformation($"[{Device.LinkDeviceAlias}] Received configuration: {frame.Data.Length} bytes");
            Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Config data: {string.Join(" ", frame.Data.Select(b => $"{b:X2}"))}");

            // Parse the configuration - CRC will be calculated correctly in SetConfiguration
            var configuration = ExtensionConfiguration.Parse(frame.Data, frame.Crc32);
            if (configuration != null)
            {
                // SetConfiguration will recalculate CRC on first 12 bytes only
                Device.SetConfiguration(configuration);

                // Log detailed configuration info
                Logger?.LogInformation($"[{Device.LinkDeviceAlias}] {configuration}");

                if (configuration.ExtensionSpecificData.Length > 0)
                {
                    Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Extension-specific data: {string.Join(" ", configuration.ExtensionSpecificData.Select(b => $"{b:X2}"))}");
                }
            }
            else
            {
                Logger?.LogWarning($"[{Device.LinkDeviceAlias}] Failed to parse configuration data (length: {frame.Data.Length})");
                // Fallback: Calculate CRC on first 12 bytes and set directly
            }

            // Send Config Equal response to acknowledge
            await SendConfigEqual();

            Logger?.LogInformation($"[{Device.LinkDeviceAlias}] Configuration received and processed");
        }

        private async Task SendConfigEqual()
        {
            Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Sending heartbeat with config CRC: 0x{Device.ConfigurationCrc:X8}");


            var frame = new NatFrame(Device.ExtensionNat, Device.DeviceNat, NatCommands.ConfigEqual, []);

            await SendNatFrameAsync(frame);
        }
    }
}
