using loxonelinkdotnet.Devices.Extensions;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using loxonelinkdotnet.NatProtocol.MessageHandlers;

namespace loxonelinkdotnet.Devices.TreeDevices;

/// <summary>
/// RGBW Tree device implementation
/// </summary>
public class LedSpotRgbwTreeDevice : RgbwTreeDevice
{
    protected override string DeviceName => "LED Spot RGBW Tree";

    public LedSpotRgbwTreeDevice(uint serialNumber, byte hardwareVersion, uint firmwareVersion, ILogger logger)
        : base(serialNumber, DeviceTypes.LEDSpotRgbwTree, hardwareVersion, firmwareVersion, logger)
    {
    }
}
