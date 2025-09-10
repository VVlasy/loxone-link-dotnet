using loxonelinkdotnet.Devices.Extensions;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using loxonelinkdotnet.NatProtocol.FragmentedMessageHandlers;

namespace loxonelinkdotnet.Devices;

/// <summary>
/// Base class for all Tree devices
/// </summary>
public abstract class TreeDevice : LoxoneLinkNatDevice
{    
    /// <summary>
    /// Reference to the parent extension that owns this tree device
    /// </summary>
    public TreeExtension? ParentExtension { get; set; }
    public TreeBranches Branch { get; internal set; }

    public override byte ExtensionNat { get => ParentExtension?.ExtensionNat ?? 0x00; internal set => base.ExtensionNat = value; }

    protected TreeDevice(uint serialNumber, ushort deviceType, byte hardwareVersion, uint firmwareVersion, ILogger logger) : 
        base(serialNumber, deviceType, hardwareVersion, firmwareVersion, null, logger)
    {
        // DeviceNat will be assigned during NAT assignment
        DeviceNat = 0x00; // Start with no NAT assigned
        Stm32DeviceID = "a55a3928786aaabbccddeeff";

        InitializeMessageHandlers();
    }

    private void InitializeMessageHandlers()
    {
        // Register fragmented handlers specific to Tree Extension
        var cryptDeviceIdFragmentedHandler = new CryptDeviceIdRequestFragmentedMessageHandler(this, _logger);
        RegisterFragmentedMessageHandler(cryptDeviceIdFragmentedHandler);
    }

    /// <summary>
    /// Tree devices don't start their own lifecycle - they are managed by their parent extension
    /// </summary>
    public override async Task StartAsync()
    {
        _logger?.LogDebug($"[{LinkDeviceAlias}] Tree device initialized - lifecycle managed by parent extension");
        
        // Tree devices don't start CAN receiving or background tasks
        // They only process messages forwarded by their parent extension
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tree devices send frames through their parent extension's CAN interface
    /// </summary>
    public override async Task SendNatFrameAsync(NatFrame frame)
    {
        if (ParentExtension != null)
        {
            await ParentExtension.SendNatFrameAsync(frame);
        }
        else
        {
            _logger?.LogWarning($"[TreeDevice {SerialNumber:X8}] Cannot send frame - no parent extension");
        }
    }

    public override bool IsFrameForUs(INatFrame frame)
    {
        // If we don't have a parent extension, we can't filter properly
        if (ParentExtension == null)
        {
            _logger?.LogWarning($"[TreeDevice {SerialNumber:X8}] No parent extension - cannot filter messages");
            return false;
        }

        var extensionNat = ParentExtension.ExtensionNat;
        var deviceNat = frame.DeviceId;

        if (frame.NatId != extensionNat && frame.NatId != 0xFF)
        {
            _logger?.LogDebug($"[TreeDevice {SerialNumber:X8}] Ignoring message - Extension NAT mismatch: FrameExtensionNat={frame.NatId:X2}, MyExtensionNat={extensionNat:X2}");
            return false;
        }

        // Messages with deviceNAT == 0x00 are for the extension itself, NOT for tree devices
        if (deviceNat == 0x00)
        {
            _logger?.LogDebug($"[TreeDevice {SerialNumber:X8}] Message is for extension (DeviceNAT=00), not for tree devices");
            return false;
        }

        // Check if this is a global broadcast message (NAT 0xFF) - handle this first before parked device check
        if (deviceNat == 0xFF)
        {
            // For search devices and some other broadcast commands, only respond if we're assigned
            if (frame.Command == NatCommands.SearchDevicesRequest)
            {
                if (ExtensionNat == 0)
                {
                    _logger?.LogDebug($"[TreeDevice {SerialNumber:X8}] Ignoring search request - not assigned yet");
                    return false;
                }
            }
            _logger?.LogDebug($"[TreeDevice {SerialNumber:X8}] Handling global broadcast message: DeviceNAT={deviceNat:X2}");
            return true;
        }

        // Check if this is for parked devices (bit 7 = 0x80, but not 0xFF which we handled above)
        if ((deviceNat & 0x80) == 0x80 && frame.Command != NatCommands.NatOfferConfirm)
        {
            // Messages to parked devices should be handled by all devices
            _logger?.LogDebug($"[TreeDevice {SerialNumber:X8}] Handling parked device message: DeviceNAT={deviceNat:X2}");
            return true;
        }

        // Check if the message is specifically addressed to this device's Device NAT
        if (deviceNat == DeviceNat && DeviceNat != 0x00)
        {
            _logger?.LogDebug($"[TreeDevice {SerialNumber:X8}] Message for this device: DeviceNAT={deviceNat:X2}");
            return true;
        }

        // Check if this is a global broadcast to all devices (DeviceId 0xFF)
        if (deviceNat == 0xFF)
        {
            _logger?.LogDebug($"[TreeDevice {SerialNumber:X8}] Broadcast message to all devices: DeviceNAT={deviceNat:X2}");
            return true;
        }

        // For NAT offer confirmations, check the assigned Device NAT in data[0]
        if (frame.Command == NatCommands.NatOfferConfirm && frame.Data.Length > 0)
        {
            var assignedDeviceNat = frame.Data[0];
            var assignedSerial = BitConverter.ToUInt32(frame.Data, 3);
            
            if (assignedSerial == SerialNumber)
            {
                _logger?.LogDebug($"[TreeDevice {SerialNumber:X8}] Device NAT assignment for this device: DeviceNAT={assignedDeviceNat:X2}");
                return true;
            }
        }

        // Check for parked device messages (high bit set, except 0xFF which is broadcast)
        if ((deviceNat & 0x80) == 0x80 && deviceNat != 0xFF)
        {
            // Parked devices receive messages sent to their parked NAT range
            if (DeviceNat != 0x00 && ((DeviceNat & 0x80) == 0x80))
            {
                _logger?.LogDebug($"[TreeDevice {SerialNumber:X8}] Parked device message: DeviceNAT={deviceNat:X2}");
                return true;
            }
        }

        // Message is not for this device
        _logger?.LogDebug($"[TreeDevice {SerialNumber:X8}] Ignoring message not for this device: DeviceNAT={deviceNat:X2}, MyNAT={ExtensionNat:X2}");
        return false;
    }

    /// <summary>
    /// Send NAT offer request for tree device (different format than extensions)
    /// </summary>
    public override async Task SendOfferAsync()
    {
        if (ParentExtension == null)
        {
            _logger?.LogWarning($"[TreeDevice {SerialNumber:X8}] Cannot send offer - no parent extension");
            return;
        }

        _logger?.LogDebug($"[TreeDevice {SerialNumber:X8}] Sending tree device offer request via extension NAT {ParentExtension.ExtensionNat:X2}");

        var data = new byte[7];
        
        // Tree device offer format (matching real device: [40 0C 80 12 86 16 C0])
        // First byte: Device type upper byte
        data[0] = (byte)((DeviceType >> 8) & 0xFF);
        
        // Bytes 1-2: Device type (little-endian)
        var deviceTypeBytes = BitConverter.GetBytes(DeviceType);
        data[1] = deviceTypeBytes[0];
        data[2] = deviceTypeBytes[1];
        
        // Bytes 3-6: Serial number (little-endian)
        var serialBytes = BitConverter.GetBytes(SerialNumber);
        data[3] = serialBytes[0];
        data[4] = serialBytes[1]; 
        data[5] = serialBytes[2];
        data[6] = serialBytes[3];

        // Tree devices send offers using parent extension's NAT with DeviceId=0x00
        // This is key: Tree devices don't have their own NAT, they use parent's NAT + DeviceId=0x00 for offers
        var frame = new NatFrame(ParentExtension.ExtensionNat, 0x00, NatCommands.NatOfferRequest, data);
        await ParentExtension.SendNatFrameAsync(frame);

        _logger?.LogDebug($"[TreeDevice {SerialNumber:X8}] Sent tree device offer: Type=0x{DeviceType:X4} Serial={SerialNumber:X8}");
    }

}
