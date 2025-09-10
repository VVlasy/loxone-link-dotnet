using loxonelinkdotnet.Can.Adapters;

namespace loxonelinkdotnet.NatProtocol.Core;

/// <summary>
/// Represents a NAT frame used in Loxone Link protocol
/// </summary>
public class NatFrame : INatFrame
{
    public byte NatId { get; set; }
    public byte DeviceId { get; set; }
    public byte Command { get; set; }
    public byte[] Data { get; set; } = new byte[7];
    public bool IsFromServer { get; set; }
    public bool IsFragmented { get; set; }

    // Convenient data access properties following Loxone protocol conventions
    /// <summary>
    /// B0: First data byte (Data[1] in CAN frame, Data[0] in NAT data payload)
    /// </summary>
    public byte B0
    {
        get => Data[0];
        set => Data[0] = value;
    }

    /// <summary>
    /// Val16: 16-bit little-endian value from Data[1] and Data[2]
    /// </summary>
    public ushort Val16
    {
        get => (ushort)(Data[1] | (Data[2] << 8));
        set
        {
            Data[1] = (byte)(value & 0xFF);
            Data[2] = (byte)((value >> 8) & 0xFF);
        }
    }

    /// <summary>
    /// Val32: 32-bit little-endian value from Data[3] through Data[6]
    /// </summary>
    public uint Val32
    {
        get => (uint)(Data[3] | (Data[4] << 8) | (Data[5] << 16) | (Data[6] << 24));
        set
        {
            Data[3] = (byte)(value & 0xFF);
            Data[4] = (byte)((value >> 8) & 0xFF);
            Data[5] = (byte)((value >> 16) & 0xFF);
            Data[6] = (byte)((value >> 24) & 0xFF);
        }
    }

    public NatFrame() { }

    public NatFrame(byte natId, byte deviceId, byte command, byte[] data, bool isFromServer = false)
    {
        NatId = natId;
        DeviceId = deviceId;
        Command = command;
        IsFromServer = isFromServer;
        
        if (data.Length > 7)
            throw new ArgumentException("Data payload cannot exceed 7 bytes for NAT frames");
        
        Array.Copy(data, Data, Math.Min(data.Length, 7));
    }

    /// <summary>
    /// Encode NAT frame to 29-bit CAN frame
    /// CAN ID format: 10000.0ddlnnnn.nnnn0000.cccccccc
    /// dd = direction: 00=from extension, 10=from extension(shortcut), 11=from miniserver
    /// l = package type: 0=regular, 1=fragmented  
    /// nnnnnnnn = NAT ID, cccccccc = command
    /// </summary>
    public SocketCan.CanFrame ToCanFrame()
    {
        uint canId = 0x10000000; // Base NAT identifier (bits 28-24)
        
        // Set direction bits (bits 22-21) 
        // 00 = from extension (default), 11 = from server
        if (IsFromServer)
            canId |= 0x00600000; // Set bits 22-21 to 11
        // else leave as 00 for extension messages
        
        // Set fragmented bit (bit 20)
        if (IsFragmented)
            canId |= 0x00100000;
        
        // Set NAT ID (bits 19-12)
        canId |= (uint)(NatId << 12);
        
        // Set command (bits 7-0)
        canId |= Command;
        
        var frame = new SocketCan.CanFrame
        {
            CanId = canId,
            CanDlc = 8
        };

        // First byte is device ID for NAT protocol
        frame.Data[0] = DeviceId;
        Array.Copy(Data, 0, frame.Data, 1, 7);

        return frame;
    }

    /// <summary>
    /// Decode 29-bit CAN frame to NAT frame
    /// </summary>
    public static NatFrame? FromCanFrame(SocketCan.CanFrame frame)
    {
        // Check if this is a NAT frame (top 5 bits should be 0x10)
        if ((frame.CanId & 0xF8000000) != 0x10000000)
            return null;

        var natFrame = new NatFrame
        {
            Command = (byte)(frame.CanId & 0xFF),
            NatId = (byte)((frame.CanId >> 12) & 0xFF),
            IsFragmented = (frame.CanId & 0x00100000) != 0, // Updated to bit 20
            IsFromServer = (frame.CanId & 0x00600000) == 0x00600000, // Check bits 22-21 = 11
            DeviceId = frame.Data[0]
        };

        Array.Copy(frame.Data, 1, natFrame.Data, 0, 7);
        return natFrame;
    }

    public override string ToString()
    {
        return $"NAT[{NatId:X2}:{DeviceId:X2}] Cmd=0x{Command:X2} " +
               $"Data=[{string.Join(" ", Data.Select(b => $"{b:X2}"))}] " +
               $"{(IsFromServer ? "S→E" : "E→S")} {(IsFragmented ? "FRAG" : "")}";
    }
}
