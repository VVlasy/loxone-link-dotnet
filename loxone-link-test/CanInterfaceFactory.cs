using loxonelinkdotnet;
using loxonelinkdotnet.Can.Adapters;
using Microsoft.Extensions.Logging;

namespace loxonelinktest;

/// <summary>
/// Factory for creating CAN interfaces based on configuration
/// </summary>
public static class CanInterfaceFactory
{
    public static ICanInterface CreateCanInterface(AppConfig.CanInterfaceConfig config, ILogger? logger = null)
    {
        logger?.LogInformation($"[CanInterfaceFactory] Creating CAN interface: Type={config.Type}");

        switch (config.Type.ToLower())
        {
            case "serial":
                logger?.LogInformation($"[CanInterfaceFactory] Creating WaveshareSerialCan on {config.ComPort}");
                return new WaveshareSerialCan(config.ComPort, config.BaudRate, config.CanBitRate, logger);

            case "socketcan":
                logger?.LogInformation("[CanInterfaceFactory] Creating SocketCAN interface");
                return new SocketCan("can0", logger);

            default:
                logger?.LogWarning($"[CanInterfaceFactory] Unknown interface type '{config.Type}', defaulting to SocketCAN");
                return new SocketCan("can0", logger);
        }
    }
}
