using loxonelinkdotnet.Can.Adapters;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using loxonelinkdotnet.NatProtocol.FragmentedMessageHandlers;
using loxonelinkdotnet.NatProtocol.MessageHandlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static loxonelinkdotnet.Can.Adapters.SocketCan;

namespace loxonelinkdotnet.Devices.Extensions
{
    public class TreeExtension : LoxoneExtension
    {
        protected override string DeviceName => "TreeExtension";


        /// <summary>
        /// Tree devices managed by this extension
        /// </summary>
        private readonly List<TreeDevice> _devices = new();

        /// <summary>
        /// All Tree devices
        /// </summary>
        public IReadOnlyList<TreeDevice> AllDevices => _devices;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="serialNumber">Serial number must start 13:FF:FF:FF</param>
        /// <param name="hardwareVersion"></param>
        /// <param name="firmwareVersion"></param>
        /// <param name="devices"></param>
        /// <param name="canBus"></param>
        /// <param name="logger"></param>
        public TreeExtension(uint serialNumber,
            byte hardwareVersion,
            uint firmwareVersion,
            IEnumerable<TreeDevice> devices,
            ICanInterface canBus,
            ILogger logger) : base(serialNumber, DeviceTypes.TreeBaseExtension, hardwareVersion, firmwareVersion, canBus, logger)
        {
            // Register message handlers specific to Tree Extension
            var canDiagnosticsHandler = new CanDiagnosticsMessageHandler(this, _logger);
            var canErrorRequestHandler = new CanErrorRequestMessageHandler(this, _logger);

            RegisterMessageHandler(canDiagnosticsHandler);
            RegisterMessageHandler(canErrorRequestHandler);

            // Add devices if provided
            foreach (var device in devices)
            {
                AddDevice(device, device.Branch);
            }
        }

        internal override async Task HandleIncomingNatFrameAsync(NatFrame frame)
        {
            // Then forward appropriate messages to tree devices
            // Only forward messages that are NOT for the extension itself (deviceNAT != 0)
            if (IsAssigned)
            {
                if (frame.DeviceId != 0x00 && frame.DeviceId != 0xFF)
                {
                    // Forward other targeted messages (parked devices, specific NATs, etc.)
                    await ForwardMessageToTreeDevices(frame);
                    return;
                }
                else if (frame.DeviceId == 0xFF)
                {
                    if (frame.DeviceId == 0xFF)
                    {
                        // Only forward certain broadcast messages to tree devices
                        if (ShouldForwardBroadcastToTreeDevices(frame))
                        {
                            await ForwardMessageToTreeDevices(frame);
                        }
                    }
                }
            }

            await base.HandleIncomingNatFrameAsync(frame);
        }

        public override async Task SendStartInfoAsync()
        {
            // Only send start info if we have been assigned a NAT (not 0x84 default)
            if (ExtensionNat == 0x84)
            {
                _logger?.LogDebug($"[{LinkDeviceAlias}] Tree extension not yet assigned NAT - skipping start info");
                return;
            }

            _logger?.LogDebug($"[{LinkDeviceAlias}] Tree extension assigned NAT {ExtensionNat:X2} - sending start info");
            await base.SendStartInfoAsync();

            // Tree extension starts up but waits for Miniserver commands
            // Don't send proactive CAN error replies - wait for requests
            _logger?.LogDebug($"[{LinkDeviceAlias}] Tree extension started - waiting for Miniserver commands");
        }

        /// <summary>
        /// Get devices (for message handlers)
        /// </summary>
        public IEnumerable<TreeDevice> GetDevices() => AllDevices;

        /// <summary>
        /// Add a Tree device to this extension
        /// </summary>
        public void AddDevice(TreeDevice device, TreeBranches treeBranch)
        {
            device.ParentExtension = this;
            device.Branch = treeBranch;
            _devices.Add(device);
            _logger?.LogInformation($"[Extension] Added device: Type=0x{device.DeviceType:X4} Serial={device.SerialNumber:X8}");
        }

        public void RemoveDevice(TreeDevice device)
        {
            if (_devices.Remove(device))
            {
                device.ParentExtension = null;
                _logger?.LogInformation($"[Extension] Removed device: Type=0x{device.DeviceType:X4} Serial={device.SerialNumber:X8}");
            }
            else
            {
                _logger?.LogWarning($"[Extension] Attempted to remove device not in list: Type=0x{device.DeviceType:X4} Serial={device.SerialNumber:X8}");
            }
        }

        /// <summary>
        /// Trigger tree devices to send NAT offer requests (called when miniserver sends 0xF4)
        /// </summary>
        public async Task TriggerDeviceOffersAsync()
        {
            if (!IsOnline)
            {
                _logger?.LogWarning("[Extension] Cannot trigger device offers - extension not online");
                return;
            }

            _logger?.LogInformation($"[Extension] Triggering {AllDevices.Count} tree devices to send offers");

            foreach (var device in AllDevices)
            {
                _logger?.LogDebug($"[Extension] Triggering device offer: Type=0x{device.DeviceType:X4} Serial={device.SerialNumber:X8}");
                await device.SendOfferAsync();
                await Task.Delay(50); // Small delay between offers
            }
        }


        /// <summary>
        /// Send CAN error reply for a specific branch
        /// </summary>
        public async Task SendCanErrorReplyAsync(int branchId)
        {
            _logger?.LogDebug($"[{LinkDeviceAlias}] Sending CAN error reply for branch {branchId}");

            var data = new byte[7];
            BitConverter.GetBytes((ushort)branchId).CopyTo(data, 0);
            data[2] = 0x80; // Status flag (0x80 for Tree branch)
            data[3] = 0; // Receive errors
            data[4] = 0; // Transmit errors

            var overallErrorBytes = BitConverter.GetBytes(0u);
            data[4] = overallErrorBytes[0];
            data[5] = overallErrorBytes[1];
            data[6] = overallErrorBytes[2];

            var frame = new NatFrame(ExtensionNat, DeviceNat, NatCommands.CanErrorReply, data);
            frame.Val16 = 0x8000;
            // Match real device: branchId in high bits, base value in low bits
            frame.Val32 = (uint)(branchId == 2 ? 0x00000003 : 0x00000006);
            await SendNatFrameAsync(frame);
        }

        /// <summary>
        /// Handle NAT assignment - forward tree device assignments to child devices
        /// </summary>
        public override async Task HandleAssignAsync(NatFrame frame)
        {
            var assignedNat = frame.Data[0];
            var isParked = frame.Data[1] != 0;
            var assignedSerial = BitConverter.ToUInt32(frame.Data, 3);

            _logger?.LogDebug($"[{LinkDeviceAlias}] Assignment: NAT/DeviceNAT={assignedNat:X2} Parked={isParked} Serial={assignedSerial:X8}");

            // First check if this is for the extension itself
            if (assignedSerial == SerialNumber)
            {
                await base.HandleAssignAsync(frame);
                return;
            }

            // Check if this assignment is for one of our tree devices
            var targetDevice = AllDevices.FirstOrDefault(d => d.SerialNumber == assignedSerial);
            if (targetDevice != null)
            {

                _logger?.LogInformation($"[{LinkDeviceAlias}] Forwarding assignment to tree device: Serial={assignedSerial:X8} NAT={assignedNat:X2}");
                await targetDevice.HandleAssignAsync(frame);

            }
            else
            {
                _logger?.LogDebug($"[{LinkDeviceAlias}] Ignoring assignment for unknown device: Serial={assignedSerial:X8}");
            }
        }

        /// <summary>
        /// Forward message to appropriate Tree devices based on C++ implementation logic
        /// </summary>
        private async Task ForwardMessageToTreeDevices(NatFrame frame)
        {
            _logger?.LogDebug($"[Extension] Forwarding message to Tree devices: DeviceNAT={frame.DeviceId:X2} Command=0x{frame.Command:X2}");

            // Let each device decide if it should handle the message
            var handledByDevices = new List<TreeDevice>();

            foreach (var device in AllDevices)
            {
                if (device.IsFrameForUs(frame))
                {
                    await device.HandleIncomingNatFrameAsync(frame);
                    handledByDevices.Add(device);
                }
            }

            if (handledByDevices.Count == 0)
            {
                _logger?.LogDebug($"[Extension] No Tree devices handled message: DeviceNAT={frame.DeviceId:X2}");
            }
            else
            {
                _logger?.LogDebug($"[Extension] Message handled by {handledByDevices.Count} device(s)");
            }
        }

        /// <summary>
        /// Determine if a broadcast message should be forwarded to tree devices
        /// </summary>
        private bool ShouldForwardBroadcastToTreeDevices(INatFrame frame)
        {
            switch (frame.Command)
            {
                case NatCommands.SearchDevicesRequest:
                case NatCommands.Ping:
                case NatCommands.TimeSync:
                    return true;

                default:
                    // By default, forward broadcast messages
                    _logger?.LogDebug($"[Extension] Forwarding broadcast command 0x{frame.Command:X2} to tree devices");
                    return true;
            }
        }
    }
}
