using loxonelinkdotnet.Devices.Extensions;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using loxonelinkdotnet.NatProtocol.MessageHandlers;

namespace loxonelinkdotnet.Devices.TreeDevices;

/// <summary>
/// RGBW Tree device implementation
/// </summary>
public class RgbwDimmerTreeDevice : RgbwTreeDevice
{
    protected override string DeviceName => "RGBW Dimmer 24V Tree";

    public RgbwDimmerTreeDevice(uint serialNumber, byte hardwareVersion, uint firmwareVersion, ILogger logger)
        : base(serialNumber, DeviceTypes.Rgbw24VDimmerTree, hardwareVersion, firmwareVersion, logger)
    {
    }
}
