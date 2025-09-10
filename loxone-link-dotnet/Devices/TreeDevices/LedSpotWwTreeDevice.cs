using loxonelinkdotnet.Devices.Extensions;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using loxonelinkdotnet.NatProtocol.MessageHandlers;

namespace loxonelinkdotnet.Devices.TreeDevices;

/// <summary>
/// RGBW Tree device implementation
/// </summary>
public class LedSpotWwTreeDevice : RgbwTreeDevice
{
    protected override string DeviceName => "LED Spot WW Tree";

    public LedSpotWwTreeDevice(uint serialNumber, byte hardwareVersion, uint firmwareVersion, ILogger logger)
        : base(serialNumber, DeviceTypes.LEDSpotWwTree, hardwareVersion, firmwareVersion, logger)
    {
    }
}
