namespace loxonelinkdotnet.NatProtocol.Constants;

/// <summary>
/// Reset reason constants
/// </summary>
public static class ResetReasons
{
    public const byte Undefined = 0x00;
    public const byte MiniserverStart = 0x01;
    public const byte Pairing = 0x02;
    public const byte AliveRequested = 0x03;
    public const byte Reconnect = 0x04;
    public const byte AlivePackage = 0x05;
    public const byte ReconnectBroadcast = 0x06;
    public const byte PowerOnReset = 0x20;
    public const byte StandbyReset = 0x21;
    public const byte WatchdogReset = 0x22;
    public const byte SoftwareReset = 0x23;
    public const byte PinReset = 0x24;
    public const byte WindowWatchdogReset = 0x25;
    public const byte LowPowerReset = 0x26;
}
