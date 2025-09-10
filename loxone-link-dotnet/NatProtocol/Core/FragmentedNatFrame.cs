using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace loxonelinkdotnet.NatProtocol.Core
{
    public class FragmentedNatFrame : INatFrame
    {
        public byte NatId { get; set; }
        public byte DeviceId { get; set; }
        public byte Command { get; set; }
        public byte[] Data { get; set; } = new byte[0];
        public bool IsFromServer { get; set; }
        public bool IsFragmented { get; set; }
        public byte B0 { get; set; }
        public ushort Val16 { get; set; }
        public uint Val32 { get; set; }
        public uint Crc32 { get; set; }
    }
}
