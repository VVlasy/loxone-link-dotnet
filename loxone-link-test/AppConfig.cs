using loxonelinkdotnet.Devices;
using loxonelinkdotnet.NatProtocol.Constants;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace loxonelinktest;

/// <summary>
/// Custom JSON converter for serial numbers that supports both string (MAC format) and uint formats
/// </summary>
public class SerialNumberConverter : JsonConverter<uint>
{
    public override uint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (string.IsNullOrEmpty(value))
                return 0;

            // Handle MAC address format like "0C:DD:22:01"
            if (value.Contains(':'))
            {
                var bytes = value.Split(':').Select(hex => Convert.ToByte(hex, 16)).ToArray();
                if (bytes.Length == 4)
                {
                    return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
                }
            }

            // Handle hex format like "0x12345678" or "12345678"
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToUInt32(value, 16);
            }

            // Try parsing as decimal
            if (uint.TryParse(value, out var result))
            {
                return result;
            }

            throw new JsonException($"Unable to parse serial number: {value}");
        }
        else if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetUInt32();
        }

        throw new JsonException($"Unexpected token type: {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, uint value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

/// <summary>
/// Configuration loaded from JSON file
/// </summary>
public class AppConfig
{
    public CanInterfaceConfig CanInterface { get; set; } = new();
    public TreeExtensionConfig TreeExtension { get; set; } = new();
    public string LogLevel { get; set; } = "Information";

    public class CanInterfaceConfig
    {
        public string Type { get; set; } = "Serial"; // "Serial" or "SocketCAN"
        public string ComPort { get; set; } = "COM3";
        public int BaudRate { get; set; } = 2000000;
        public int CanBitRate { get; set; } = 125000;
    }

    public class TreeExtensionConfig
    {
        [JsonConverter(typeof(SerialNumberConverter))]
        public uint SerialNumber { get; set; } = 319941070;
        public ushort HardwareType { get; set; } = 19;
        public byte HardwareVersion { get; set; } = 2;
        public uint FirmwareVersion { get; set; } = 13030124;
        public List<RgbwDimmerDeviceConfig> RgwDimmerDevices { get; set; } = new List<RgbwDimmerDeviceConfig>
        {
            new RgbwDimmerDeviceConfig { Serial = 316482209, HardwareVersion = 1, FirmwareVersion = 13030124, Branch = TreeBranches.Right }
        };
    }

    public class RgbwDimmerDeviceConfig
    {
        [JsonConverter(typeof(SerialNumberConverter))]
        public uint Serial { get; set; }
        public byte HardwareVersion { get; set; } = 1;
        public uint FirmwareVersion { get; set; } = 13030124;
        public TreeBranches Branch { get; set; }
    }

    /// <summary>
    /// Load configuration from JSON file
    /// </summary>
    public static AppConfig Load(string filePath = "config.json")
    {
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                return config;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading config: {ex.Message}");
        }

        // Return default config if file not found or error
        return new AppConfig();
    }

    /// <summary>
    /// Save configuration to JSON file
    /// </summary>
    public void Save(string filePath = "config.json")
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving config: {ex.Message}");
        }
    }

    /// <summary>
    /// Create a default configuration
    /// </summary>
    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            CanInterface = new CanInterfaceConfig
            {
                Type = "Serial",
                ComPort = "COM3",
                BaudRate = 2000000,
                CanBitRate = 125000
            },
            TreeExtension = new TreeExtensionConfig
            {
                SerialNumber = 319941070,
                HardwareType = 19,
                HardwareVersion = 2,
                FirmwareVersion = 13030124,
                RgwDimmerDevices = new List<RgbwDimmerDeviceConfig>
                {
                    new RgbwDimmerDeviceConfig
                    {
                        Serial = 316482209,
                        HardwareVersion = 1,
                        FirmwareVersion = 13030124,
                        Branch = TreeBranches.Right
                    }
                }
            },
            LogLevel = "Information"
        };
    }
}
