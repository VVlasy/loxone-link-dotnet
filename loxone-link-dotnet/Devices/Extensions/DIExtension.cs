using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;

namespace loxonelinkdotnet.Devices.Extensions;

/// <summary>
/// Digital Input Extension implementation
/// </summary>
public class DIExtension : LoxoneLinkNatDevice
{
    protected override string DeviceName => "DIExtension";

    // Extension-specific configuration
    private uint _frequencyBitmask = 0x00000000;
    private uint _digitalInputBitmask = 0x00000000;
    private DateTime _lastBitmaskSent = DateTime.MinValue;
    private DateTime _lastFrequencySent = DateTime.MinValue;

    public DIExtension(uint serialNumber, ICanInterface canBus, ILogger logger) 
        : base(serialNumber, DeviceTypes.DIExtension, 0x01, 10031108, canBus, logger)
    {
        // DI Extension specific initialization
    }

    /// <summary>
    /// Handle configuration updates specific to DI Extension
    /// </summary>
    protected override void OnConfigurationUpdated(ExtensionConfiguration configuration)
    {
        _logger?.LogInformation($"[{LinkDeviceAlias}] Processing DI Extension configuration...");

        // Parse extension-specific config data (frequency bitmask for DI Extension)
        if (configuration.ExtensionSpecificData.Length >= 4)
        {
            _frequencyBitmask = BitConverter.ToUInt32(configuration.ExtensionSpecificData, 0);
            _logger?.LogInformation($"[{LinkDeviceAlias}] Frequency bitmask = 0x{_frequencyBitmask:X8}");
        }
        else
        {
            _logger?.LogWarning($"[{LinkDeviceAlias}] No frequency bitmask in config data");
        }

        // Reset timers to force sending updated values
        _lastBitmaskSent = DateTime.MinValue;
        _lastFrequencySent = DateTime.MinValue;
    }

    /// <summary>
    /// Called when device goes online - send default values
    /// </summary>
    protected override void OnDeviceOnline()
    {
        base.OnDeviceOnline();
        
        // Start sending values when we come online
        _ = Task.Run(async () =>
        {
            try
            {
                await SendDefaultValuesAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"[{LinkDeviceAlias}] Error sending default values");
            }
        });
    }

    /// <summary>
    /// Called when device goes offline - stop sending values
    /// </summary>
    protected override void OnDeviceOffline()
    {
        base.OnDeviceOffline();
        
        // Reset state when going offline
        _digitalInputBitmask = 0;
        _lastBitmaskSent = DateTime.MinValue;
        _lastFrequencySent = DateTime.MinValue;
    }

    /// <summary>
    /// Simulate digital input changes and send values
    /// </summary>
    public async Task SendDigitalValuesAsync()
    {
        if (CurrentState != DeviceState.Online) return;

        var now = DateTime.UtcNow;

        // Send digital input values every second
        if ((now - _lastBitmaskSent).TotalMilliseconds >= 1000)
        {
            // Simulate changing digital inputs
            _digitalInputBitmask++;
            
            var digitalData = new byte[7];
            digitalData[0] = 0x00; // Input type
            BitConverter.GetBytes((ushort)0).CopyTo(digitalData, 1); // Input index
            BitConverter.GetBytes(_digitalInputBitmask).CopyTo(digitalData, 3); // Input bitmask

            var frame = new NatFrame(ExtensionNat, DeviceNat, NatCommands.DigitalValue, digitalData);
            await SendNatFrameAsync(frame);

            _lastBitmaskSent = now;
            _logger?.LogDebug($"[{LinkDeviceAlias}] Sent digital values: 0x{_digitalInputBitmask:X8}");
        }

        // Send frequency values every second (if enabled by frequency bitmask)
        if ((now - _lastFrequencySent).TotalMilliseconds >= 1000 && _frequencyBitmask != 0)
        {
            var random = new Random();
            var frequencyValue = (uint)random.Next(0, 100);

            var frequencyData = new byte[7];
            frequencyData[0] = 0x00; // Frequency type
            BitConverter.GetBytes((ushort)0).CopyTo(frequencyData, 1); // Frequency index
            BitConverter.GetBytes(frequencyValue).CopyTo(frequencyData, 3); // Frequency value

            var frame = new NatFrame(ExtensionNat, DeviceNat, NatCommands.FrequencyValue, frequencyData);
            await SendNatFrameAsync(frame);

            _lastFrequencySent = now;
            _logger?.LogDebug($"[{LinkDeviceAlias}] Sent frequency value: {frequencyValue} Hz");
        }
    }

    /// <summary>
    /// Send default values after coming online
    /// </summary>
    public async Task SendDefaultValuesAsync()
    {
        if (CurrentState != DeviceState.Online) return;

        _logger?.LogInformation($"[{LinkDeviceAlias}] Sending default values");

        // Send initial digital input state
        var digitalData = new byte[7];
        digitalData[0] = 0x00;
        BitConverter.GetBytes((ushort)0).CopyTo(digitalData, 1);
        BitConverter.GetBytes(_digitalInputBitmask).CopyTo(digitalData, 3);

        var frame = new NatFrame(ExtensionNat, DeviceNat, NatCommands.DigitalValue, digitalData);
        await SendNatFrameAsync(frame);

        _logger?.LogDebug($"[{LinkDeviceAlias}] Sent default digital values");
    }

    /// <summary>
    /// Set a specific digital input state
    /// </summary>
    /// <param name="inputNumber">Input number (0-31)</param>
    /// <param name="isHigh">True for high/active, false for low/inactive</param>
    public async Task SetDigitalInputAsync(int inputNumber, bool isHigh)
    {
        if (inputNumber < 0 || inputNumber > 31)
        {
            _logger?.LogError(null, $"[{LinkDeviceAlias}] Invalid input number: {inputNumber}. Must be 0-31");
            return;
        }

        if (CurrentState != DeviceState.Online)
        {
            _logger?.LogWarning($"[{LinkDeviceAlias}] Cannot set digital input - device not online");
            return;
        }

        uint mask = 1u << inputNumber;
        
        if (isHigh)
            _digitalInputBitmask |= mask;  // Set bit
        else
            _digitalInputBitmask &= ~mask; // Clear bit

        // Send the updated bitmask immediately
        var digitalData = new byte[7];
        digitalData[0] = 0x00; // Input type
        BitConverter.GetBytes((ushort)0).CopyTo(digitalData, 1); // Input index
        BitConverter.GetBytes(_digitalInputBitmask).CopyTo(digitalData, 3); // Input bitmask

        var frame = new NatFrame(ExtensionNat, DeviceNat, NatCommands.DigitalValue, digitalData);
        await SendNatFrameAsync(frame);

        _logger?.LogInformation($"[{LinkDeviceAlias}] Digital input {inputNumber} set to {(isHigh ? "HIGH" : "LOW")}. Bitmask: 0x{_digitalInputBitmask:X8}");
    }

    /// <summary>
    /// Get the current state of all digital inputs
    /// </summary>
    public uint GetDigitalInputBitmask() => _digitalInputBitmask;

    /// <summary>
    /// Get the state of a specific digital input
    /// </summary>
    /// <param name="inputNumber">Input number (0-31)</param>
    /// <returns>True if input is high/active</returns>
    public bool GetDigitalInputState(int inputNumber)
    {
        if (inputNumber < 0 || inputNumber > 31)
            return false;
        
        uint mask = 1u << inputNumber;
        return (_digitalInputBitmask & mask) != 0;
    }

    /// <summary>
    /// Background task to continuously send values
    /// </summary>
    public async Task StartValueSendingAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SendDigitalValuesAsync();
                await Task.Delay(100, cancellationToken); // 100ms interval
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"[{LinkDeviceAlias}] Error in value sending loop");
                await Task.Delay(1000, cancellationToken); // Wait 1 second before retrying
            }
        }
    }
}