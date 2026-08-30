using System;

namespace SmartBoyDumperMAUI
{
    public interface IByteTransport : IDisposable
    {
        byte ReadByte();
        void WriteBytes(byte[] data);
    }
}