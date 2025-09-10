
using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace loxonelinkdotnet.NatProtocol
{
    public class LoxoneCryptoCanAuthentication
    {
        internal static byte[]? CryptoMasterDeviceIDBytes { get; private set; }
        public static string? CryptoMasterDeviceID
        {
            get => Encoding.UTF8.GetString(CryptoMasterDeviceIDBytes ?? []);
            set => CryptoMasterDeviceIDBytes = Convert.FromHexString(value ?? string.Empty);
        }

        public static string? LoxoneCryptoEncryptedAESKey { get; set; }
        public static string? LoxoneCryptoEncryptedAESIV { get; set; }

        // Legacy crypto constants (used for tree devices and older authentication)
        public static uint[] LoxoneCryptoCanAlgoLegacyKey { get; set; }
        public static uint[] LoxoneCryptoCanAlgoLegacyIV { get; set; }

        // Hash function implementations
        public static uint RSHash(byte[] key)
        {
            uint a = 63689;
            uint hash = 0;

            foreach (byte b in key)
            {
                hash = hash * a + b;
                hash = hash & 0xFFFFFFFF;
                a = a * 378551;
                a = a & 0xFFFFFFFF;
            }

            return hash;
        }

        public static uint JSHash(byte[] key)
        {
            uint hash = 1315423911;

            foreach (byte b in key)
            {
                hash ^= (hash >> 2) + b + (hash << 5);
                hash = hash & 0xFFFFFFFF;
            }

            return hash;
        }

        public static uint DJBHash(byte[] key)
        {
            uint hash = 5381;

            foreach (byte b in key)
            {
                hash = ((hash << 5) + hash) + b;
                hash = hash & 0xFFFFFFFF;
            }

            return hash;
        }

        public static uint DEKHash(byte[] key)
        {
            uint hash = (uint)key.Length;

            foreach (byte b in key)
            {
                hash = ((hash << 5) ^ (hash >> 27)) ^ b;
                hash = hash & 0xFFFFFFFF;
            }

            return hash;
        }

        // AES encryption/decryption methods
        public static byte[] DecryptInitPacket(byte[] data, uint serial)
        {
            uint[] cryptoCanAlgoKey = InitializeCryptoCanAlgoKey(LoxoneCryptoEncryptedAESKey ?? string.Empty);
            uint[] cryptoCanAlgoIV = InitializeCryptoCanAlgoIV(LoxoneCryptoEncryptedAESIV ?? string.Empty);

            // Pre-calculate the AES key/iv based on constant data and the serial number
            uint[] aesKey = new uint[4];
            uint[] aesIV = new uint[4];

            for (int i = 0; i < 4; i++)
            {
                aesKey[i] = (~serial ^ cryptoCanAlgoKey[i]) & 0xFFFFFFFF;
                aesIV[i] = (serial ^ cryptoCanAlgoIV[i]) & 0xFFFFFFFF;
            }

            byte[] key = new byte[16];
            byte[] iv = new byte[16];

            Buffer.BlockCopy(aesKey, 0, key, 0, 16);
            Buffer.BlockCopy(aesIV, 0, iv, 0, 16);

            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                aes.IV = iv;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(data, 0, data.Length);
                }
            }
        }

        public static byte[] EncryptDataPacket(byte[] data, uint[] key, uint iv)
        {
            uint[] aesKey = new uint[4];
            for (int i = 0; i < 4; i++)
            {
                aesKey[i] = iv ^ key[i];
            }

            uint[] aesIV = { iv, iv, iv, iv };

            byte[] keyBytes = new byte[16];
            byte[] ivBytes = new byte[16];

            Buffer.BlockCopy(aesKey, 0, keyBytes, 0, 16);
            Buffer.BlockCopy(aesIV, 0, ivBytes, 0, 16);

            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = keyBytes;
                aes.IV = ivBytes;

                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    return encryptor.TransformFinalBlock(data, 0, data.Length);
                }
            }
        }

        // Legacy AES encryption/decryption methods (used for tree devices)
        public static byte[] DecryptInitPacketLegacy(byte[] data, uint serial)
        {
            // Pre-calculate the AES key/iv based on legacy constants and the serial number
            uint[] aesKey = new uint[4];
            uint[] aesIV = new uint[4];

            for (int i = 0; i < 4; i++)
            {
                aesKey[i] = (~(serial ^ LoxoneCryptoCanAlgoLegacyKey[i])) & 0xFFFFFFFF;
                aesIV[i] = (serial ^ LoxoneCryptoCanAlgoLegacyIV[i]) & 0xFFFFFFFF;
            }

            byte[] key = new byte[16];
            byte[] iv = new byte[16];

            Buffer.BlockCopy(aesKey, 0, key, 0, 16);
            Buffer.BlockCopy(aesIV, 0, iv, 0, 16);

            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                aes.IV = iv;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(data, 0, data.Length);
                }
            }
        }

        public static byte[] EncryptInitPacketLegacy(byte[] data, uint serial)
        {
            // Pre-calculate the AES key/iv based on legacy constants and the serial number
            uint[] aesKey = new uint[4];
            uint[] aesIV = new uint[4];

            for (int i = 0; i < 4; i++)
            {
                aesKey[i] = (~(serial ^ LoxoneCryptoCanAlgoLegacyKey[i])) & 0xFFFFFFFF;
                aesIV[i] = (serial ^ LoxoneCryptoCanAlgoLegacyIV[i]) & 0xFFFFFFFF;
            }

            byte[] key = new byte[16];
            byte[] iv = new byte[16];

            Buffer.BlockCopy(aesKey, 0, key, 0, 16);
            Buffer.BlockCopy(aesIV, 0, iv, 0, 16);

            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                aes.IV = iv;

                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    return encryptor.TransformFinalBlock(data, 0, data.Length);
                }
            }
        }

        // Challenge solving methods
        public static (uint[] key, uint iv) SolveChallenge(uint random, uint serial, byte[] deviceID)
        {
            byte[] buffer = new byte[deviceID.Length + 8];
            Array.Copy(deviceID, 0, buffer, 0, deviceID.Length);

            byte[] randomBytes = BitConverter.GetBytes(random);
            byte[] serialBytes = BitConverter.GetBytes(serial);

            Array.Copy(randomBytes, 0, buffer, deviceID.Length, 4);
            Array.Copy(serialBytes, 0, buffer, deviceID.Length + 4, 4);

            byte[] xorBuffer = new byte[buffer.Length];
            for (int i = 0; i < buffer.Length; i++)
            {
                xorBuffer[i] = (byte)(buffer[i] ^ 0xA5);
            }

            uint[] keyResult = {
                RSHash(buffer),
                JSHash(buffer),
                DJBHash(buffer),
                DEKHash(buffer)
            };

            uint ivResult = RSHash(xorBuffer);

            return (keyResult, ivResult);
        }

        // Crypto algorithm initialization
        public static uint[] InitializeCryptoCanAlgoKey(string encryptedAESKey)
        {
            byte[] encryptedBytes = Convert.FromHexString(encryptedAESKey);

            return new uint[] {
                DEKHash(encryptedBytes),
                JSHash(encryptedBytes),
                DJBHash(encryptedBytes),
                RSHash(encryptedBytes)
            };
        }

        public static uint[] InitializeCryptoCanAlgoIV(string encryptedAESIV)
        {
            byte[] encryptedBytes = Convert.FromHexString(encryptedAESIV);

            return new uint[] {
                DEKHash(encryptedBytes),
                JSHash(encryptedBytes),
                DJBHash(encryptedBytes),
                RSHash(encryptedBytes)
            };
        }
    }
}
