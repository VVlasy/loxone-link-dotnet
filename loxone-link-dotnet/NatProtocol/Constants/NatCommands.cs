namespace loxonelinkdotnet.NatProtocol.Constants;

/// <summary>
/// NAT protocol command constants
/// </summary>
public static class NatCommands
{
    // Common commands
    public const byte VersionRequest = 0x01;
    public const byte StartInfo = 0x02;
    public const byte VersionInfo = 0x03;
    public const byte ConfigEqual = 0x04;
    public const byte Ping = 0x05;
    public const byte Pong = 0x06;
    public const byte SetOffline = 0x07;
    public const byte Alive = 0x08;
    public const byte ExtensionsOffline = 0x0A; // Extensions offline command
    public const byte TimeSync = 0x0C;
    public const byte Identify = 0x10;
    public const byte SendConfig = 0x11;
    public const byte WebServiceRequest = 0x12;
    public const byte Logging = 0x13;
    public const byte CanDiagnosticsReply = 0x16;
    public const byte CanDiagnosticsRequest = 0x17;
    public const byte CanErrorReply = 0x18;
    public const byte CanErrorRequest = 0x19;
    public const byte TreeShortcut = 0x1A;
    public const byte TreeShortcutTest = 0x1B;

    // Value commands
    public const byte DigitalValue = 0x80;
    public const byte AnalogValue = 0x81;
    public const byte RgbwValue = 0x84;
    public const byte FrequencyValue = 0x85;
    public const byte CompositeRgbwValue = 0x88;

    // Encryption commands
    public const byte CryptDeviceIdReply = 0x98;
    public const byte CryptDeviceIdRequest = 0x99;
    public const byte CryptChallengeAuthRequest = 0x9C;
    public const byte CryptChallengeAuthReply = 0x9D;

    // NAT management commands
    public const byte FragmentStart = 0xF0;
    public const byte FragmentData = 0xF1;
    public const byte SearchDevicesRequest = 0xFB;
    public const byte SearchDevicesResponse = 0xFC;
    public const byte NatOfferConfirm = 0xFD;
    public const byte NatOfferRequest = 0xFE;
    public const byte FirmwareUpdate = 0xEF;
}
