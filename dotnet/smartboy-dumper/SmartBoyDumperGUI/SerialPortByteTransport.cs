using smartboy_dumper;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;

public class SerialPortByteTransport : IByteTransport, IDisposable
{
    private readonly SerialPort _port;
    private readonly StreamWriter _log;

    private readonly Queue<byte> _rx = new();
    private readonly byte[] _chunk = new byte[4096];

    private const int PostResetSettleMs = 2000;

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

        _log = new StreamWriter("smartboy_serial.log", append: true, Encoding.UTF8)
        {
            AutoFlush = true
        };

        _log.WriteLine("=== SerialPortByteTransport STARTED ===");
        _log.WriteLine($"Port: {portName}, Baud: {baudRate}");

        Thread.Sleep(PostResetSettleMs);

        int discarded = _port.BytesToRead;
        if (discarded > 0)
            _log.WriteLine($"=== {discarded} Byte(s) Boot-Müll verworfen ===");

        _port.DiscardInBuffer();
    }

    public byte ReadByte()
    {
        while (_rx.Count == 0)
        {
            try
            {
                int n = _port.Read(_chunk, 0, _chunk.Length);
                if (n > 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        byte b = _chunk[i];
                        _rx.Enqueue(b);
                        _log.WriteLine("READ: " + b.ToString("X2"));
                    }
                }
                else
                {
                    _log.WriteLine("READ: TIMEOUT (no data)");
                }
            }
            catch (TimeoutException)
            {
                _log.WriteLine("READ: TIMEOUT");
            }
        }

        return _rx.Dequeue();
    }

    public void WriteBytes(byte[] data)
    {
        _port.Write(data, 0, data.Length);

        string hex = BitConverter.ToString(data);
        Debug.WriteLine("WRITE: " + hex);
        _log.WriteLine("WRITE: " + hex);
    }

    public void Dispose()
    {
        try
        {
            _log.WriteLine("=== SerialPortByteTransport CLOSED ===");
            _log.Flush();
            _log.Dispose();
        }
        catch { }

        try { _port.Close(); } catch { }
        _port.Dispose();
    }
}
