namespace smartboy_dumper
{
    public interface IByteTransport : IDisposable
    {
        byte ReadByte();
        void WriteBytes(byte[] data);
    }
}