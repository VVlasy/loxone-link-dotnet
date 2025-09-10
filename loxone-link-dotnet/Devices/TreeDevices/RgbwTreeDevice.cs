using loxonelinkdotnet.Devices.Extensions;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using loxonelinkdotnet.NatProtocol.MessageHandlers;

namespace loxonelinkdotnet.Devices.TreeDevices;

/// <summary>
/// RGBW Tree device implementation
/// </summary>
public abstract class RgbwTreeDevice : TreeDevice
{
    public struct RgbwValue
    {
        public byte Red { get; set; }
        public byte Green { get; set; }
        public byte Blue { get; set; }
        public byte White { get; set; }

        public RgbwValue(byte red, byte green, byte blue, byte white)
        {
            Red = red;
            Green = green;
            Blue = blue;
            White = white;
        }

        public override string ToString()
        {
            return $"RGBW({Red}%, {Green}%, {Blue}%, {White}%)";
        }
    }

    private RgbwValue _currentValue;
    protected DateTime _lastValueUpdate = DateTime.UtcNow;

    public RgbwValue CurrentValue 
    { 
        get => _currentValue;
        private set
        {
            _currentValue = value;
            _lastValueUpdate = DateTime.UtcNow;
        }
    }

    public event EventHandler<RgbwValue>? ValueChanged;
    
    public RgbwTreeDevice(uint serialNumber, ushort deviceType, byte hardwareVersion, uint firmwareVersion, ILogger logger) 
        : base(serialNumber, deviceType, hardwareVersion, firmwareVersion, logger)
    {
        _currentValue = new RgbwValue(0, 0, 0, 0);

        var rgbwValueHandler = new RgbwValueMessageHandler(this, logger);
        var compositeRgbwValueHandler = new CompositeRgbwValueMessageHandler(this, logger);

        // Register handlers
        RegisterMessageHandler(rgbwValueHandler);
        RegisterMessageHandler(compositeRgbwValueHandler);
    }

    /// <summary>
    /// Apply RGBW value to the device (implement your hardware control here)
    /// </summary>
    private async Task ApplyRgbwValueAsync(RgbwValue value, double fadeTime = 0, bool isJump = false)
    {
        CurrentValue = value;
        ValueChanged?.Invoke(this, value);

        // TODO: Implement actual hardware control here
        // For example, control Philips Hue lights, PWM outputs, etc.
        _logger?.LogInformation($"[RGBW Device {SerialNumber:X8}] Applied: {value} " +
                              $"{(fadeTime > 0 ? $"with fade {fadeTime:F1}s" : "")} {(isJump ? "[JUMP]" : "")}");

        await Task.CompletedTask;
    }

    public async Task SendStateUpdateAsync()
    {
        // Send current RGBW state back to Miniserver
        var data = new byte[7];
        data[0] = 0; // Channel number
        BitConverter.GetBytes((ushort)0).CopyTo(data, 1); // Flags (unused for state update)
        data[3] = CurrentValue.Red;
        data[4] = CurrentValue.Green;
        data[5] = CurrentValue.Blue;
        data[6] = CurrentValue.White;

        await SendNatFrameAsync(new NatFrame(ExtensionNat, DeviceNat, NatCommands.RgbwValue, data));
        _logger?.LogDebug($"[RGBW Device {SerialNumber:X8}] Sent state update: {CurrentValue}");
    }

    /// <summary>
    /// Manually set RGBW value (e.g., for testing or local control)
    /// </summary>
    public async Task SetRgbwAsync(byte red, byte green, byte blue, byte white)
    {
        var newValue = new RgbwValue(red, green, blue, white);
        await ApplyRgbwValueAsync(newValue);
        await SendStateUpdateAsync();
    }

    public class RgbwValueMessageHandler : MessageHandlerBase
    {
        public override byte Command => NatCommands.RgbwValue;

        private readonly RgbwTreeDevice _device;

        public RgbwValueMessageHandler(RgbwTreeDevice extension, ILogger? logger)
            : base(extension, logger)
        {
            _device = extension;
        }

        public override async Task HandleRequestAsync(NatFrame frame)
        {
            // Standard RGBW command: R/G/B/W values in data[1..4]
            var red = frame.Data[3];
            var green = frame.Data[4];
            var blue = frame.Data[5];
            var white = frame.Data[6];

            var newValue = new RgbwValue(red, green, blue, white);
            Logger?.LogInformation($"[RGBW Device {_device.SerialNumber:X8}] Set RGBW: {newValue}");

            await _device.ApplyRgbwValueAsync(newValue);

            // Send acknowledgment back to Miniserver
            await _device.SendNatFrameAsync(new NatFrame(_device.ExtensionNat, _device.DeviceNat, NatCommands.RgbwValue, frame.Data));
        }
    }


    public class CompositeRgbwValueMessageHandler : MessageHandlerBase
    {
        public override byte Command => NatCommands.CompositeRgbwValue;

        private readonly RgbwTreeDevice _device;

        public CompositeRgbwValueMessageHandler(RgbwTreeDevice extension, ILogger? logger)
            : base(extension, logger)
        {
            _device = extension;
        }

        public override async Task HandleRequestAsync(NatFrame frame)
        {
            // Composite RGBW command: includes fade time in data[0..1]
            var fadeTimeRaw = BitConverter.ToUInt16(frame.Data, 1);
            var red = frame.Data[3];
            var green = frame.Data[4];
            var blue = frame.Data[5];
            var white = frame.Data[6];

            // Decode fade time
            double fadeTime;
            if ((fadeTimeRaw & 0x4000) != 0)
                fadeTime = (fadeTimeRaw & 0x3FFF) * 1.0; // Seconds
            else
                fadeTime = (fadeTimeRaw & 0x3FFF) * 0.1; // Deciseconds

            var isJump = (fadeTimeRaw & 0x8000) != 0;
            var newValue = new RgbwValue(red, green, blue, white);

            Logger?.LogInformation($"[RGBW Device {_device.SerialNumber:X8}] Set Composite RGBW: {newValue} " +
                                  $"Fade Time: {fadeTime:F1}s {(isJump ? "[JUMP]" : "")}");

            await _device.ApplyRgbwValueAsync(newValue, fadeTime, isJump);

            // Send acknowledgment back to Miniserver
            await SendNatFrameAsync(new NatFrame(_device.ExtensionNat, _device.DeviceNat, NatCommands.CompositeRgbwValue, frame.Data));
        }
    }
}
