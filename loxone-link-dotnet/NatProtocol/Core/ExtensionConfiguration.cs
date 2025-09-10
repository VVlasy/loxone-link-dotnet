namespace loxonelinkdotnet.NatProtocol.Core;

/// <summary>
/// Represents the configuration data sent by the Miniserver to extensions
/// </summary>
public class ExtensionConfiguration
{
    /// <summary>
    /// Size of the configuration header (typically 9 bytes)
    /// </summary>
    public byte ConfigSize { get; set; }

    /// <summary>
    /// Configuration version
    /// </summary>
    public byte ConfigVersion { get; set; }

    /// <summary>
    /// LED synchronization offset for Tree devices
    /// </summary>
    public byte LedSyncOffset { get; set; }

    /// <summary>
    /// Unknown/reserved byte
    /// </summary>
    public byte Reserved { get; set; }

    /// <summary>
    /// Timeout in seconds for offline detection
    /// </summary>
    public uint OfflineTimeoutSeconds { get; set; }

    /// <summary>
    /// Extension-specific configuration data
    /// </summary>
    public byte[] ExtensionSpecificData { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// CRC32 of the complete configuration data
    /// </summary>
    public uint ConfigurationCrc { get; set; }

    /// <summary>
    /// Parse configuration data from received bytes
    /// </summary>
    /// <param name="configData">Raw configuration data</param>
    /// <param name="crc">CRC32 of the configuration data</param>
    /// <returns>Parsed configuration or null if invalid</returns>
    public static ExtensionConfiguration? Parse(byte[] configData, uint crc)
    {
        if (configData.Length < 9) // Minimum size for header
            return null;

        var config = new ExtensionConfiguration
        {
            ConfigurationCrc = crc,
            ConfigSize = configData[0],
            ConfigVersion = configData[1],
            LedSyncOffset = configData[2],
            Reserved = configData[3],
            OfflineTimeoutSeconds = BitConverter.ToUInt32(configData, 4)
        };

        // Extract extension-specific data (everything after the 9-byte header, minus 4-byte CRC at end)
        if (configData.Length > 9)
        {
            int extensionDataLength = configData.Length - 9;
            
            // Check if there's a CRC at the end (last 4 bytes)
            if (extensionDataLength >= 4)
            {
                extensionDataLength -= 4; // Exclude CRC from extension data
            }
            
            if (extensionDataLength > 0)
            {
                config.ExtensionSpecificData = new byte[extensionDataLength];
                Array.Copy(configData, 9, config.ExtensionSpecificData, 0, extensionDataLength);
            }
        }

        return config;
    }

    /// <summary>
    /// Get timeout in minutes for display purposes
    /// </summary>
    public double OfflineTimeoutMinutes => OfflineTimeoutSeconds / 60.0;

    public override string ToString()
    {
        return $"Config[Size:{ConfigSize}, Ver:{ConfigVersion}, LED:{LedSyncOffset}, " +
               $"Timeout:{OfflineTimeoutSeconds}s ({OfflineTimeoutMinutes:F1}min), " +
               $"ExtData:{ExtensionSpecificData.Length}bytes, CRC:0x{ConfigurationCrc:X8}]";
    }
}