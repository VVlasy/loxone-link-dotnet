namespace loxonelinkdotnet.NatProtocol.Constants;

/// <summary>
/// Device type constants
/// </summary>
public static class DeviceTypes
{
    /// <summary>
    /// Serial number starts with 14:XX:XX:XX
    /// </summary>
    public const ushort DIExtension = 0x0014;

    /// <summary>
    /// Serial number starts with 13:XX:XX:XX
    /// </summary>
    public const ushort TreeBaseExtension = 0x0013;

    /// <summary>
    /// Serial number starts with 13:XX:XX:XX
    /// </summary>
    public const ushort Rgbw24VDimmerTree = 0x800C;

    public const ushort LEDSpotRgbwTree = 0x8016;
    public const ushort LEDSpotWwTree = 0x8017;

    public const ushort TouchTree = 0x8003;
    public const ushort MotionSensorTree = 0x8002;
}
