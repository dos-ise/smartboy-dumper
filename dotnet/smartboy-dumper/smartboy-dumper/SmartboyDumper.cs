using System.Text;
using System.Collections.Generic;

namespace smartboy_dumper
{
    public enum InState { None, Nm, Rb, StartRom, Srm, End, Nr }

    public class SmartboyException : Exception
    {
        public SmartboyException(string message) : base(message) { }
    }

    public class SmartboyDumper : IDisposable
    {
        private const int BankSize = 16 * 1024;

        private static readonly string[] Tags =
        { "", "nm", "rb", "startrom", "srm", "end", "nr" };

        private static readonly byte[] GbMagic =
        {
            0xce, 0xed, 0x66, 0x66, 0xcc, 0x0d, 0x00, 0x0b,
            0x03, 0x73, 0x00, 0x83, 0x00, 0x0c, 0x00, 0x0d,
            0x00, 0x08, 0x11, 0x1f, 0x88, 0x89, 0x00, 0x0e
        };
        private const int GbMagicOffset = 260;

        private readonly IByteTransport _transport;
        private readonly string _outputDirectory;

        private InState _state = InState.None;
        private int _tagPos = 0;
        private string _pending = string.Empty;
        private int _bytesSinceLastTag = 0;
        private bool _syncLostReported = false;
        private const int SyncLostThreshold = 2048; // Bytes ohne vollständigen Tag-Match
        private bool _romReq;
        private bool _cartReq;

        private string? _romName;
        private int _nrBanks = -1;

        public event EventHandler? CartridgeAwaited;
        public event EventHandler<string>? RomNameDetected;
        public event EventHandler<int>? RomSizeDetected;
        public event EventHandler<int>? DumpProgressChanged;
        public event EventHandler<string>? DumpCompleted;
        // Feuert einmalig, wenn über SyncLostThreshold Bytes hinweg kein
        // einziger Tag vollständig erkannt wurde - typischerweise, weil die
        // initiale "vsnm...startrom"-Ankündigung verpasst wurde (z.B. weil
        // das Cartridge schon vor dem Öffnen des Ports gesteckt hat).
        public event EventHandler? SyncLost;

        public SmartboyDumper(IByteTransport transport, string outputDirectory)
        {
            _transport = transport;
            _outputDirectory = outputDirectory;
            Directory.CreateDirectory(_outputDirectory);
        }

        // --- Low-Level I/O -----------------------------------------------

        private byte ReadByte() => _transport.ReadByte();

        private void WriteString(string s) =>
            _transport.WriteBytes(Encoding.ASCII.GetBytes(s));

        // --- Tag-Erkennung -------------------------------------------------

        private static InState SuffixGetTag(string str)
        {
            if (string.IsNullOrEmpty(str)) return InState.None;
            for (int i = 1; i < Tags.Length; i++)
                if (str.EndsWith(Tags[i], StringComparison.Ordinal)) return (InState)i;
            return InState.None;
        }

        // Liefert alle Tags, zu denen "pending" noch werden könnte
        // (d.h. Tags[i] beginnt mit "pending"). Ersetzt das alte,
        // fehlerhafte PrefixGetTag (das fälschlich str.StartsWith(tag)
        // statt tag.StartsWith(str) prüfte und daher bei kurzen,
        // gerade erst begonnenen Präfixen nie traf).
        private static List<InState> TagsStartingWith(string pending)
        {
            var result = new List<InState>();
            for (int i = 1; i < Tags.Length; i++)
                if (Tags[i].Length >= pending.Length &&
                    string.CompareOrdinal(Tags[i], 0, pending, 0, pending.Length) == 0)
                    result.Add((InState)i);
            return result;
        }

        private InState ReadUntilNewState(StringBuilder sb)
        {
            InState state;
            while ((state = SuffixGetTag(sb.ToString())) == InState.None)
                sb.Append((char)ReadByte());
            return state;
        }

        private InState ReadNameUntilNewState()
        {
            var sb = new StringBuilder();
            var state = ReadUntilNewState(sb);

            if (_romName == null)
            {
                sb.Length -= Tags[(int)state].Length;
                _romName = sb.ToString();
                RomNameDetected?.Invoke(this, _romName);
            }

            return state;
        }

        private InState ReadSizeUntilNewState()
        {
            var sb = new StringBuilder();
            var state = ReadUntilNewState(sb);

            if (_nrBanks == -1)
            {
                var sizeStr = sb.ToString();
                int digits = 0;
                while (digits < sizeStr.Length && char.IsDigit(sizeStr[digits])) digits++;

                _nrBanks = digits > 0 ? int.Parse(sizeStr.Substring(0, digits)) : 0;
                RomSizeDetected?.Invoke(this, _nrBanks * BankSize);
            }

            return state;
        }

        // --- ROM-Dump ------------------------------------------------------

        private static string? CreateFilename(string romName, byte[] buf)
        {
            if (buf.Length < GbMagicOffset + GbMagic.Length)
                return null;

            bool isGbc = false;
            for (int i = 0; i < GbMagic.Length; i++)
            {
                if (buf[GbMagicOffset + i] != GbMagic[i]) { isGbc = true; break; }
            }

            var safeName = string.Join("_", romName.Trim().Split(Path.GetInvalidFileNameChars()));
            return isGbc ? $"{safeName}.gbc" : $"{safeName}.gb";
        }

        private void DumpRom()
        {
            int romSize = _nrBanks * BankSize;
            var buf = new byte[romSize];
            int lastReportedPercent = -1;

            for (int offset = 0; offset < romSize; offset++)
            {
                buf[offset] = ReadByte();

                int percent = (offset + 1) * 100 / romSize;
                if (percent != lastReportedPercent)
                {
                    lastReportedPercent = percent;
                    DumpProgressChanged?.Invoke(this, percent);
                }
            }

            var filename = CreateFilename(_romName!, buf) ?? $"{_romName}.bin";
            var fullPath = Path.Combine(_outputDirectory, filename);
            File.WriteAllBytes(fullPath, buf);

            DumpCompleted?.Invoke(this, fullPath);
        }

        // --- Hauptschleife ---------------------------------------------

        public void Run()
        {
            while (true)
            {
                if (_nrBanks > 0 && _romName != null &&
                    _state == InState.StartRom && _tagPos == -1)
                {
                    DumpRom();
                    return;
                }

                if (_romName != null && _nrBanks > 0 && !_romReq)
                {
                    _romReq = true;
                    WriteString("sd");
                    continue;
                }

                if (_tagPos == -1)
                {
                    switch (_state)
                    {
                        case InState.Nm:
                            _state = ReadNameUntilNewState();
                            break;
                        case InState.Rb:
                            _state = ReadSizeUntilNewState();
                            break;
                        case InState.StartRom:
                            // "startrom" wurde erkannt, aber Name/Größe fehlen noch
                            // (z.B. weil nm/rb aus Sync-Gründen nicht sauber ankamen).
                            // NICHT einfach stillschweigend resetten (sonst werden
                            // anschließend ROM-Bytes fälschlich als Tag-Zeichen
                            // interpretiert) - stattdessen sichtbar machen und warten.
                            Console.WriteLine("*** 'startrom' ohne bekannten Namen/Größe empfangen - ignoriere Tag");
                            _state = InState.None;
                            _tagPos = 0;
                            break;
                        case InState.Nr:
                            _romName = null;
                            _nrBanks = -1;
                            _romReq = false;
                            if (!_cartReq)
                            {
                                Console.WriteLine("*** Cartridge einlegen");
                                _cartReq = true;
                            }
                            _state = InState.None;   // NEU
                            _tagPos = 0;              // NEU
                            break;
                        default:
                            _state = InState.None;
                            _tagPos = 0;
                            break;
                    }

                    if (_state != InState.Nr)
                        continue;
                }

                byte b = ReadByte();
                _bytesSinceLastTag++;

                if (!_syncLostReported && _bytesSinceLastTag > SyncLostThreshold)
                {
                    _syncLostReported = true;
                    Console.WriteLine($"*** Kein gültiger Tag seit {_bytesSinceLastTag} Bytes - " +
                                       "Handshake vermutlich verpasst. Cartridge neu einlegen / Port neu öffnen.");
                    SyncLost?.Invoke(this, EventArgs.Empty);
                }

                // Versuchen, das Byte an den bisher gesammelten Tag-Anfang
                // ("_pending") anzuhängen.
                string extended = _pending + (char)b;
                var candidates = TagsStartingWith(extended);

                if (candidates.Count == 0)
                {
                    // Fehlschlag: "_pending + b" passt zu keinem Tag mehr.
                    // WICHTIG (das war der eigentliche Bug): das aktuelle Byte
                    // wird NICHT verworfen, sondern sofort erneut als möglicher
                    // Beginn eines NEUEN Tags geprüft. So geht z.B. das 'n' von
                    // "nm" nicht mehr verloren, nur weil davor ein unbekanntes
                    // oder zu einem anderen Tag ("startrom"/"srm") gehörendes
                    // Präfix wie "vs" im Datenstrom stand.
                    _pending = string.Empty;
                    candidates = TagsStartingWith(((char)b).ToString());
                    extended = candidates.Count > 0 ? ((char)b).ToString() : string.Empty;
                }

                _pending = extended;

                if (candidates.Count == 0)
                {
                    // Byte gehört zu keinem bekannten Tag (z.B. Steuerzeichen) -
                    // hier ist Verwerfen tatsächlich korrekt.
                    _state = InState.None;
                    _tagPos = 0;
                    continue;
                }

                // Eindeutig vollständiges Tag erkannt (Länge passt exakt)?
                var exact = candidates.Find(c => Tags[(int)c].Length == _pending.Length);
                if (exact != InState.None)
                {
                    _state = exact;
                    _pending = string.Empty;
                    _tagPos = -1;
                    _bytesSinceLastTag = 0;
                    _syncLostReported = false;

                    if (_state == InState.Nr && !_cartReq)
                    {
                        CartridgeAwaited?.Invoke(this, EventArgs.Empty);
                        _cartReq = true;
                    }
                    else
                    {
                        _cartReq = false;
                    }
                }
                else
                {
                    // Noch nicht eindeutig (z.B. "n" könnte "nm" oder "nr" werden,
                    // "s" könnte "startrom" oder "srm" werden) - weiter sammeln.
                    _state = candidates.Count == 1 ? candidates[0] : InState.None;
                    _tagPos = 0;
                }
            }
        }

        public void Dispose() => _transport.Dispose();
    }
}