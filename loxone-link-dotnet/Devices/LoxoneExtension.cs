using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using loxonelinkdotnet.NatProtocol.MessageHandlers;
using loxonelinkdotnet.NatProtocol.Core;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.Devices;
using loxonelinkdotnet.Can.Adapters;
using Microsoft.Extensions.Logging;

namespace loxonelinkdotnet;

/// <summary>
/// Loxone Extension that handles the lifecycle with the Miniserver
/// </summary>
public abstract partial class LoxoneExtension : LoxoneLinkNatDevice
{
    public override byte DeviceNat { get => 0x00; internal set => throw new InvalidOperationException("DeviceNat is read-only for Extensions"); }

    public LoxoneExtension(
        uint serialNumber,
        ushort deviceType,
        byte hardwareVersion,
        uint firmwareVersion,
        ICanInterface canBus,
        ILogger logger) : base(serialNumber, deviceType, hardwareVersion, firmwareVersion, canBus, logger)
    {

    }
}
