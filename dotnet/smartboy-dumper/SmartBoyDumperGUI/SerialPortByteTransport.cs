using System.IO.Ports;
using System.Text;
using smartboy_dumper;

public class SerialPortByteTransport : IByteTransport
{
    private readonly SerialPort _port;

    public SerialPortByteTransport(string portName, int baudRate = 115200)
    {
        _port = new SerialPort(portName, baudRate)
        {
            ReadTimeout = 1000,
            WriteTimeout = 1000,
            DtrEnable = true,
            RtsEnable = true,
            ReadBufferSize = 4096,
            Encoding = Encoding.ASCII
        };

        _port.Open();
    }

    public byte ReadByte()
    {
        int value;
        while (true)
        {
            try
            {
                value = _port.ReadByte();
                return (byte)value;
            }
            catch (TimeoutException)
            {
                // einfach weiter versuchen
            }
        }
    }

    public void WriteBytes(byte[] data)
    {
        _port.Write(data, 0, data.Length);
    }

    public void Dispose()
    {
        try { _port.Close(); } catch { }
        _port.Dispose();
    }
}