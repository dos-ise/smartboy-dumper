using smartboy_dumper;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;

public class SerialPortByteTransport : IByteTransport
{
    private readonly SerialPort _port;
    private readonly StreamWriter _log;

    // Viele Arduino-artige USB-Seriell-Boards (danach sieht der SmartBoy-Adapter
    // aus) resetten den Mikrocontroller über DTR beim Öffnen des Ports. Der
    // Adapter braucht danach eine kurze Boot-/Reinit-Zeit, bevor er das
    // Cartridge sauber scannt und die Handshake-Sequenz ("vsnm...startrom")
    // sendet. Liest man sofort nach Open(), bekommt man stattdessen
    // Boot-Rauschen/undefinierte Register-Reste.
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

        // Logfile öffnen (append)
        _log = new StreamWriter("smartboy_serial.log", append: true, Encoding.UTF8)
        {
            AutoFlush = true
        };

        _log.WriteLine("=== SerialPortByteTransport STARTED ===");
        _log.WriteLine($"Port: {portName}, Baud: {baudRate}");

        // Boot-/Reset-Phase abwarten, dann alles verwerfen, was der Adapter
        // währenddessen eventuell schon gesendet hat (Boot-Müll, keine
        // gültigen Tags), damit Run() erst ab dem frischen Handshake liest.
        Thread.Sleep(PostResetSettleMs);
        int discarded = _port.BytesToRead;
        if (discarded > 0)
        {
            _log.WriteLine($"=== {discarded} Byte(s) Boot-Müll verworfen ===");
        }
        _port.DiscardInBuffer();
    }

    public byte ReadByte()
    {
        int value;

        while (true)
        {
            try
            {
                value = _port.ReadByte();
                byte b = (byte)value;

                string hex = b.ToString("X2");

                _log.WriteLine("READ: " + hex);

                return b;
            }
            catch (TimeoutException)
            {
                // Timeout → loggen
                _log.WriteLine("READ: TIMEOUT");
            }
        }
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