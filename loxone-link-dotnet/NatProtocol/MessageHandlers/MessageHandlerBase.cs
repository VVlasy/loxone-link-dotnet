using loxonelinkdotnet.Devices;
using Microsoft.Extensions.Logging;
using loxonelinkdotnet.NatProtocol.Core;
using System.Diagnostics;

namespace loxonelinkdotnet.NatProtocol.MessageHandlers;

/// <summary>
/// Interface for handling NAT protocol messages
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    /// The NAT command this handler processes
    /// </summary>
    byte Command { get; }

    /// <summary>
    /// Handle an incoming request for this message type
    /// </summary>
    Task HandleRequestAsync(NatFrame frame);
}

/// <summary>
/// Base class for NAT message handlers
/// </summary>
public abstract class MessageHandlerBase : IMessageHandler
{
    protected readonly LoxoneLinkNatDevice Device;
    protected readonly ILogger? Logger;

    protected MessageHandlerBase(LoxoneLinkNatDevice device, ILogger? logger)
    {
        Device = device;
        Logger = logger;
    }

    public abstract byte Command { get; }

    public abstract Task HandleRequestAsync(NatFrame frame);

    /// <summary>
    /// Send a NAT frame using the extension's CAN bus
    /// </summary>
    protected async Task SendNatFrameAsync(NatFrame frame)
    {
        await Device.SendNatFrameAsync(frame);
    }

    /// <summary>
    /// Send fragmented data using the extension's fragmentation logic
    /// </summary>
    protected async Task SendFragmentedDataAsync(byte command, byte[] data)
    {
        await Device.SendFragmentedDataAsync(command, data);
    }
}

