using loxonelinkdotnet.Can.Adapters;
using System.Runtime.InteropServices;
using System.Text;

namespace loxonelinkdotnet;

/// <summary>
/// Interface for CAN adapters
/// </summary>
public interface ICanInterface : IDisposable
{
    event EventHandler<SocketCan.CanFrame>? FrameReceived;
    event EventHandler<SocketCan.CanFrame>? FrameSent;
    Task SendAsync(SocketCan.CanFrame frame);
    void StartReceiving();
    void StopReceiving();
}
