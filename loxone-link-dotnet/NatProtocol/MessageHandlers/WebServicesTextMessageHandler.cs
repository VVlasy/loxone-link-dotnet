using loxonelinkdotnet.Devices;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Constants;
using loxonelinkdotnet.NatProtocol.Core;
using System.Diagnostics;
using System.Text;

namespace loxonelinkdotnet.NatProtocol.MessageHandlers;

/// <summary>
/// Handles WebServices text messages (command 0x12)
/// </summary>
public class WebServicesTextMessageHandler : IMessageHandler
{
    private readonly loxonelinkdotnet.Devices.LoxoneLinkNatDevice _device;
    private readonly ILogger? _logger;

    public byte Command => NatCommands.WebServiceRequest;

    public WebServicesTextMessageHandler(loxonelinkdotnet.Devices.LoxoneLinkNatDevice device, ILogger? logger)
    {
        _device = device;
        _logger = logger;
    }

    public async Task HandleRequestAsync(NatFrame frame)
    {
        try
        {
            // Extract web service request string from frame data
            // Format: [DeviceID][0][StringLength][String...]
            if (frame.Data.Length < 2)
            {
                _logger?.LogWarning($"[{_device.LinkDeviceAlias}] Invalid WebServices request - insufficient data");
                return;
            }

            byte stringLength = frame.Data[1];
            if (stringLength <= 1 || stringLength > 6) // Max 6 bytes for string in 7-byte data payload
            {
                _logger?.LogDebug($"[{_device.LinkDeviceAlias}] WebServices request with length {stringLength} - likely empty or fragmented");
                return;
            }

            // Extract the command string (skip first 2 bytes: DeviceID and StringLength)
            string webServiceRequest = Encoding.UTF8.GetString(frame.Data, 2, Math.Min(stringLength - 1, 5));
            
            _logger?.LogInformation($"[{_device.LinkDeviceAlias}] WebServices request: '{webServiceRequest}'");

            // Handle the web service request
            await HandleWebServiceRequestAsync(webServiceRequest);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"[{_device.LinkDeviceAlias}] Error handling WebServices request");
        }
    }

    private async Task HandleWebServiceRequestAsync(string request)
    {
        switch (request?.ToLowerInvariant())
        {
            case "version":
                await SendWebServiceResponseAsync($"App Version: {_device.FirmwareVersion}, HW Version: {_device.HardwareVersion}\r\n");
                break;

            case "statistics":
                await SendWebServiceResponseAsync(GetStatisticsString());
                break;

            case "techreport":
                await SendWebServiceResponseAsync(GetTechReportString());
                break;

            case "reboot":
                _logger?.LogInformation($"[{_device.LinkDeviceAlias}] WebServices: Reboot requested");
                await SendWebServiceResponseAsync("Reboot initiated\r\n");
                // Could trigger a device restart here if needed
                break;

            case "forceupdate":
                _logger?.LogInformation($"[{_device.LinkDeviceAlias}] WebServices: Force update requested");
                await SendWebServiceResponseAsync("Force update initiated\r\n");
                // Could trigger a firmware update check here if needed
                break;

            default:
                _logger?.LogInformation($"[{_device.LinkDeviceAlias}] WebServices: Unknown request '{request}'");
                await SendWebServiceResponseAsync($"Unknown command: {request}\r\n");
                break;
        }
    }

    private async Task SendWebServiceResponseAsync(string response)
    {
        if (string.IsNullOrEmpty(response))
            return;

        await _device.SendWebServicesResponseAsync(response);
    }

    private string GetStatisticsString()
    {
        // Create a basic statistics report
        // Format similar to what Loxone devices return
        return $"Sent:0;Rcv:0;Err:0;REC:0;TEC:0;HWE:0;TQ:0;mTQ:8;QOvf:0;RQ:1;mRQ:1;State:{(_device.IsOnline ? "Online" : _device.IsParked ? "Parked" : "Offline")};";
    }

    private string GetTechReportString()
    {
        // Create a technical report
        var uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;
        return $"UpTime:{(int)uptime.TotalSeconds}s;Serial:{_device.SerialNumber:X8};NatIdx:{_device.ExtensionNat:X2};ConfigCRC:{_device.ConfigurationCrc:X8};";
    }
}