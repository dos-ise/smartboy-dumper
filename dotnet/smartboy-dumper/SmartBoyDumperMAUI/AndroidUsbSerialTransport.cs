using System.Diagnostics;
using Android.Hardware.Usb;
using Anotherlab.UsbSerialForAndroid.Driver;
using smartboy_dumper;

public class AndroidUsbSerialTransport : IByteTransport
{
    private readonly UsbDeviceConnection _connection;
    private readonly UsbSerialPort _port;
    private readonly byte[] _chunk = new byte[64]; // CDC-ACM Paketgröße

    private byte[] _buffer = Array.Empty<byte>();
    private int _bufferPos;

    // Kooperatives Abbruch-Flag: Cancel() setzt es, ReadByte() prüft es
    // zwischen den (max. 1s langen) Lese-Versuchen und gibt dann sauber
    // auf, BEVOR Dispose() die Verbindung schließt. Damit vermeiden wir,
    // dass Close() auf einen noch offenen Bulk-Transfer wartet und den
    // aufrufenden Thread (im schlimmsten Fall den UI-Thread) blockiert.
    private volatile bool _cancelled;

    // Wenn über mehrere Sekunden gar keine Antwort kommt, ist die
    // Verbindung vermutlich tot - dann lieber einen klaren Fehler werfen
    // statt endlos weiter zu pollen (fühlt sich sonst wie ein Freeze an).
    private int _consecutiveTimeouts;
    private const int MaxConsecutiveTimeouts = 5; // 5 x 1000ms ≈ 5s ohne Antwort

    private const int PostResetSettleMs = 2000;

    public AndroidUsbSerialTransport(UsbManager manager, UsbDevice device)
    {
        var table = new ProbeTable();
        table.AddProduct(0x16D0, 0x0557, typeof(CdcAcmSerialDriver));

        var prober = new UsbSerialProber(table);
        var driver = prober.ProbeDevice(device)
                     ?? throw new SmartboyException("No matching serial driver found.");

        _connection = manager.OpenDevice(driver.Device)
                      ?? throw new SmartboyException("Could not open USB connection.");

        _port = driver.Ports[0];
        _port.Open(_connection);
        _port.SetParameters(115200, UsbSerialPort.DATABITS_8, StopBits.One, Parity.None);
        _port.SetDTR(true);
        _port.SetRTS(true);

        Thread.Sleep(PostResetSettleMs);
        DiscardBootGarbage();
    }

    // Signalisiert der laufenden ReadByte()-Schleife, sich selbst zu
    // beenden. Vor Dispose() aufrufen, NICHT direkt Dispose() aus dem
    // UI-Thread heraus während ein Dump läuft.
    public void Cancel() => _cancelled = true;

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
            Debug.WriteLine($"=== {discarded} byte(s) of boot garbage discarded ===");
    }

    public byte ReadByte()
    {
        while (_bufferPos >= _buffer.Length)
        {
            if (_cancelled)
                throw new OperationCanceledException("USB transfer cancelled.");

            int n;
            try
            {
                n = _port.Read(_chunk, 1000);
            }
            catch (Java.Lang.Exception ex)
            {
                // usb-serial-for-android wirft bei Verbindungsabbruch
                // (Kabel raus, Adapter hängt) oft eine native Exception
                // statt nur zu timeouten - explizit als SmartboyException
                // weiterreichen, statt den Rohfehler durchsickern zu lassen.
                throw new SmartboyException($"USB connection lost: {ex.Message}");
            }

            if (n <= 0)
            {
                _consecutiveTimeouts++;
                if (_consecutiveTimeouts >= MaxConsecutiveTimeouts)
                    throw new SmartboyException(
                        "No response from the USB adapter. Unplug and replug the adapter, then try again.");
                continue;
            }
            _consecutiveTimeouts = 0;
            _buffer = new byte[n];
            Array.Copy(_chunk, _buffer, n);
            _bufferPos = 0;
        }

        return _buffer[_bufferPos++];
    }

    public void WriteBytes(byte[] data)
    {
        _port.Write(data, 1000);
    }

    public void Dispose()
    {
        _cancelled = true;
        try { _port?.Close(); } catch { }
        try { _connection?.Close(); } catch { }
    }
}