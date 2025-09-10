using loxonelinkdotnet.Devices;
using loxonelinkdotnet.Devices.Extensions;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace loxonelinkdotnet.NatProtocol.FragmentedMessageHandlers
{
    public class CryptChallengeFragmentedMessageHandler : FragmentedMessageHandlerBase
    {
        public override byte Command => NatCommands.CryptChallengeAuthRequest;

        public CryptChallengeFragmentedMessageHandler(LoxoneLinkNatDevice extension, ILogger? logger) : base(extension, logger)
        {
        }

        public override async Task HandleRequestAsync(FragmentedNatFrame frame)
        {
            Logger?.LogInformation($"[{Device.LinkDeviceAlias}] Received auth challenge - processing...");
            try
            {
                // Decrypt using AES-128 CBC
                var decryptedData = LoxoneCryptoCanAuthentication.DecryptInitPacket(
                    frame.Data, Device.SerialNumber);

                // Validate header (should be 0xDEADBEEF)
                var header = BitConverter.ToUInt32(decryptedData, 0);
                if (header != 0xDEADBEEF)
                {
                    Logger?.LogError(new InvalidOperationException("Auth challenge decryption failed"), $"[{Device.LinkDeviceAlias}] Failed to decrypt auth challenge");
                    Device.IsAuthorized = false;
                    return;
                }

                // Extract random value from challenge
                var challengeRandom = BitConverter.ToUInt32(decryptedData, 4);
                Logger?.LogInformation($"[{Device.LinkDeviceAlias}] Challenge decrypted, random={challengeRandom:X8}");

                // Solve the challenge to get crypto keys
                var (cryptoKey, cryptoIV) = LoxoneCryptoCanAuthentication.SolveChallenge(
                    challengeRandom, Device.SerialNumber, Convert.FromHexString(Device.Stm32DeviceID));

                // Store the keys for future encrypted communication
                //Device.SetCryptoKeys(cryptoKey, cryptoIV);

                // Create response with NEW random value (not the challenge random)
                Random rnd = new();
                uint replyRandom = (uint)rnd.Next(int.MinValue, int.MaxValue);

                byte[] cryptoReply = new byte[16];
                Array.Copy(BitConverter.GetBytes(0xDEADBEEF), 0, cryptoReply, 0, 4);
                Array.Copy(BitConverter.GetBytes(replyRandom), 0, cryptoReply, 4, 4);

                for (int i = 0; i < 8; i++)
                {
                    cryptoReply[8 + i] = 0xa5; // fillerBytes
                }

                // Encrypt the reply using the solved crypto keys
                byte[] encryptedResponse = [];

                encryptedResponse = LoxoneCryptoCanAuthentication.EncryptDataPacket(
                cryptoReply, cryptoKey, cryptoIV);

                // Send response as fragmented message
                await SendFragmentedDataAsync(NatCommands.CryptChallengeAuthReply, encryptedResponse);

                Device.IsAuthorized = true;
                Logger?.LogInformation($"[{Device.LinkDeviceAlias}] Authentication response sent!");

                Device.HandleAuthorization();

                // Announce devices after successful authentication
                //await AnnounceDevicesAsync();
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, $"[{Device.LinkDeviceAlias}] Error during authentication");
                Device.IsAuthorized = false;
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
}
