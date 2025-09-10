using loxonelinkdotnet.Devices;
using loxonelinkdotnet.Devices.Extensions;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using loxonelinkdotnet.NatProtocol.MessageHandlers;

namespace loxonelinkdotnet.NatProtocol.FragmentedMessageHandlers;

/// <summary>
/// Handler for Crypt Device ID Request messages (0x99)
/// Miniserver requests the 12-byte STM32 UID from Tree devices
/// </summary>
public class CryptDeviceIdRequestFragmentedMessageHandler : FragmentedMessageHandlerBase
{
    public override byte Command => NatCommands.CryptDeviceIdRequest;

    public CryptDeviceIdRequestFragmentedMessageHandler(LoxoneLinkNatDevice device, ILogger? logger)
        : base(device, logger)
    {
    }

    public override async Task HandleRequestAsync(FragmentedNatFrame frame)
    {
        Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Crypt Device ID Request received - data: {Convert.ToHexString(frame.Data)}");

        try
        {
            // Decrypt the request data using legacy encryption (skip first byte which is deviceNAT)
            var decryptedData = LoxoneCryptoCanAuthentication.DecryptInitPacketLegacy(frame.Data, Device.SerialNumber);

            // Parse the decrypted data
            var magic = BitConverter.ToUInt32(decryptedData, 0);
            var randomValue = BitConverter.ToUInt32(decryptedData, 4);
            var zero1 = BitConverter.ToUInt32(decryptedData, 8);
            var zero2 = BitConverter.ToUInt32(decryptedData, 12);

            Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Decrypted: magic=0x{magic:X8}, random=0x{randomValue:X8}");

            var random = new Random();

            byte[] cryptoReply;

            // Check if this is a valid request (magic should be 0xDEADBEEF)
            if (magic == 0xDEADBEEF)
            {
                Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Valid crypto device ID request - sending device ID");

                // Create valid reply: 0xDEADBEEF + random + 12-byte device ID + padding
                cryptoReply = new byte[32];

                // 0xDEADBEEF magic
                BitConverter.GetBytes(0xDEADBEEF).CopyTo(cryptoReply, 0);

                // Random value
                BitConverter.GetBytes((uint)random.Next()).CopyTo(cryptoReply, 4);

                // 12-byte device ID (for tree devices: serial repeated 3 times)
                byte[] deviceId = Convert.FromHexString(Device.Stm32DeviceID);

                deviceId.CopyTo(cryptoReply, 8);

                // Remaining bytes are already zero (padding to 32 bytes)
            }
            else
            {
                Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Invalid crypto device ID request - sending invalid response");

                // Create invalid reply: 0x00000000 + random + zeros
                cryptoReply = new byte[32];

                // Random value
                BitConverter.GetBytes((uint)random.Next()).CopyTo(cryptoReply, 4);

                // Rest is zeros (already done by array initialization)
            }

            // Encrypt the reply data using legacy encryption
            var encryptedReply = LoxoneCryptoCanAuthentication.EncryptInitPacketLegacy(cryptoReply, Device.SerialNumber);

            // Send as fragmented package
            await Device.SendFragmentedDataAsync(NatCommands.CryptDeviceIdReply, encryptedReply);

            Logger?.LogDebug($"[{Device.LinkDeviceAlias}] Sent crypto device ID reply");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, $"[{Device.LinkDeviceAlias}] Error processing crypto device ID request");

            // Send invalid response on error
            var cryptoReply = new byte[32];
            var random = new Random();
            BitConverter.GetBytes(0x00000000).CopyTo(cryptoReply, 0);
            BitConverter.GetBytes((uint)random.Next()).CopyTo(cryptoReply, 4);

            var encryptedReply = LoxoneCryptoCanAuthentication.EncryptInitPacketLegacy(cryptoReply, Device.SerialNumber);
            await Device.SendFragmentedDataAsync(NatCommands.CryptDeviceIdReply, encryptedReply);
        }
    }
    /// <summary>
    /// Generate 12-byte device ID for tree devices (serial repeated 3 times)
    /// </summary>
    private static byte[] GenerateDeviceIdTree(uint serialNumber)
    {
        var deviceId = new byte[12];
        var serialBytes = BitConverter.GetBytes(serialNumber);

        // For tree devices: serial repeated 3 times (3 x 4 bytes = 12 bytes)
        serialBytes.CopyTo(deviceId, 0);
        serialBytes.CopyTo(deviceId, 4);
        serialBytes.CopyTo(deviceId, 8);

        return deviceId;
    }


    /// <summary>
    /// Generate 12-byte device ID for extension devices
    /// </summary>
    private static byte[] GenerateDeviceId(uint serialNumber)
    {
        var deviceId = new byte[4];
        var serialBytes = BitConverter.GetBytes(serialNumber);

        serialBytes.CopyTo(deviceId, 0);

        return deviceId;
    }
}