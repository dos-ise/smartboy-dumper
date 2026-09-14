using System.Diagnostics;
using Android.Hardware.Usb;
using Anotherlab.UsbSerialForAndroid.Driver;
using smartboy_dumper;

public class AndroidUsbSerialTransport : IByteTransport
{
    private readonly UsbDeviceConnection _connection;
    private readonly UsbSerialPort _port;
    private readonly Queue<byte> _rx = new();
    private readonly byte[] _chunk = new byte[4096];

    // Wie bei der Desktop-Variante: das Board resettet vermutlich über
    // DTR/RTS beim Verbindungsaufbau und braucht danach eine kurze
    // Boot-/Reinit-Zeit, bevor es das Cartridge sauber scannt und die
    // Handshake-Sequenz ("vsnm...startrom") sendet.
    private const int PostResetSettleMs = 2000;

    public AndroidUsbSerialTransport(UsbManager manager, UsbDevice device)
    {
        var table = new ProbeTable();
        table.AddProduct(0x16D0, 0x0557, typeof(CdcAcmSerialDriver));

        var prober = new UsbSerialProber(table);
        var driver = prober.ProbeDevice(device)
                     ?? throw new SmartboyException("Kein passender Seriell-Treiber gefunden.");

        _connection = manager.OpenDevice(driver.Device)
                      ?? throw new SmartboyException("Konnte USB-Verbindung nicht öffnen.");

        _port = driver.Ports[0];
        _port.Open(_connection);
        _port.SetParameters(115200, UsbSerialPort.DATABITS_8, StopBits.One, Parity.None);
        _port.SetDTR(true);
        _port.SetRTS(true);

        Thread.Sleep(PostResetSettleMs);
        DiscardBootGarbage();
    }

    // Liest mit kurzen Timeouts leer, bis nichts mehr kommt - Ersatz für
    // SerialPort.DiscardInBuffer(), das es hier nicht gibt.
    private void DiscardBootGarbage()
    {
        int discarded = 0;
        while (true)
        {
            int n = _port.Read(_chunk, 50);
            if (n <= 0)
                break;
            discarded += n;
        }

        if (discarded > 0)
            Debug.WriteLine($"=== {discarded} Byte(s) Boot-Müll verworfen ===");
    }

    public byte ReadByte()
    {
        while (_rx.Count == 0)
        {
            int n = _port.Read(_chunk, 1000);

            for (int i = 0; i < n; i++)
            {
                _rx.Enqueue(_chunk[i]);
            }
        }

        return _rx.Dequeue();
    }

    public void WriteBytes(byte[] data)
    {
        _port.Write(data, 1000);
    }

    public void Dispose()
    {
        try { _port?.Close(); } catch { }
        try { _connection?.Close(); } catch { }
    }
}