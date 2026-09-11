using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;

namespace SmartboyDumperCs
{
    public enum InState
    {
        None,
        Nm,       // NAME
        Rb,       // SIZE
        StartRom, // ROM
        Srm,      // SRAM
        End,      // Ende des Dumps
        Nr        // Kein ROM eingelegt
    }

    public class SmartboyException : Exception
    {
        public SmartboyException(string message) : base(message) { }
    }

    /// <summary>
    /// Portierung von smartboy-dumper (Bastien Nocera, GPLv3) nach C#.
    /// Das Original spricht das Gerät als CDC-ACM (virtueller serieller Port,
    /// /dev/ttyACM0 unter Linux) an - hier daher SerialPort statt USB-Bulk.
    /// </summary>
    public class SmartboyDumper : IDisposable
    {
        private const int BankSize = 16 * 1024;

        // Reihenfolge/Bedeutung 1:1 aus dem Original übernommen.
        private static readonly string[] Tags =
        {
            "",
            "nm",
            "rb",
            "startrom",
            "srm",
            "end",
            "nr",
        };

        // GB_MAGIC_STRING aus dem Original, als Bytes statt C-String
        private static readonly byte[] GbMagic =
        {
            0xce, 0xed, 0x66, 0x66, 0xcc, 0x0d, 0x00, 0x0b,
            0x03, 0x73, 0x00, 0x83, 0x00, 0x0c, 0x00, 0x0d,
            0x00, 0x08, 0x11, 0x1f, 0x88, 0x89, 0x00, 0x0e
        };
        private const int GbMagicOffset = 260;

        private readonly SerialPort _port;

        // Byte-Ringpuffer für gepuffertes Lesen: statt jedes Byte einzeln
        // per _port.ReadByte() zu holen (teuer, kann bei kurzen Stalls -
        // z.B. durch Console.Write - zu Datenverlust am USB-Treiber führen),
        // wird blockweise in _readChunk gelesen und von hier serviert.
        private readonly Queue<byte> _rxBuffer = new Queue<byte>();
        private readonly byte[] _readChunk = new byte[4096];

        private InState _state = InState.None;
        private int _tagPos = 0;
        private bool _romReq;
        private bool _cartReq;

        private string _romName;
        private int _nrBanks = -1;

        public bool Verbose { get; set; }

        /// <summary>
        /// Wenn true, wird jedes empfangene Byte roh als Hex + ASCII auf der
        /// Konsole ausgegeben, bevor es in die State Machine geht. Praktisch
        /// zum Debuggen, wenn unklar ist, was das Gerät tatsächlich sendet.
        /// </summary>
        public bool DumpRawBytes { get; set; }

        /// <param name="portName">COM-Port des Smartboy-Adapters, z. B. "COM5"</param>
        /// <param name="baudRate">Baudrate der virtuellen seriellen Schnittstelle</param>
        public SmartboyDumper(string portName, int baudRate = 115200)
        {
            _port = new SerialPort(portName, baudRate)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                DtrEnable = true,   // manche CDC-ACM-Geräte brauchen DTR, um Daten zu senden
                RtsEnable = true,
                ReadBufferSize = 65536 // großzügiger Reservepuffer gegen Überlauf
            };

            try
            {
                _port.Open();
            }
            catch (Exception ex)
            {
                throw new SmartboyException($"Konnte {portName} nicht öffnen: {ex.Message}");
            }
        }

        // --- Low-Level I/O -----------------------------------------------

        private byte ReadByte()
        {
            while (_rxBuffer.Count == 0)
            {
                int bytesRead;
                try
                {
                    bytesRead = _port.Read(_readChunk, 0, _readChunk.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }

                for (int i = 0; i < bytesRead; i++)
                    _rxBuffer.Enqueue(_readChunk[i]);
            }

            byte b = _rxBuffer.Dequeue();

            if (DumpRawBytes)
            {
                char c = (b >= 0x20 && b < 0x7F) ? (char)b : '.';
                Console.WriteLine($"RX 0x{b:X2}  ({c})");
            }

            return b;
        }

        private void WriteString(string s)
        {
            try
            {
                var bytes = Encoding.ASCII.GetBytes(s);
                _port.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                throw new SmartboyException($"Schreibfehler: {ex.Message}");
            }
        }

        /// <summary>
        /// Reine Diagnosefunktion: liest fortlaufend Rohbytes vom Port und
        /// gibt sie als Hex-Dump aus, ohne die State Machine zu durchlaufen.
        /// Mit Strg+C oder Prozessabbruch beenden.
        /// </summary>
        public void DumpRawStream()
        {
            Console.WriteLine("*** Roh-Debug-Modus: gebe alle empfangenen Bytes als Hex-Dump aus");
            Console.WriteLine("*** (Strg+C zum Beenden)");
            Console.WriteLine();

            var lineBuf = new List<byte>();
            int column = 0;

            while (true)
            {
                byte b = ReadByte();

                lineBuf.Add(b);
                Console.Write($"{b:X2} ");
                column++;

                if (column == 16)
                {
                    PrintAsciiTail(lineBuf);
                    lineBuf.Clear();
                    column = 0;
                }
            }
        }

        private static void PrintAsciiTail(List<byte> bytes)
        {
            Console.Write(" | ");
            foreach (var b in bytes)
            {
                char c = (b >= 0x20 && b < 0x7F) ? (char)b : '.';
                Console.Write(c);
            }
            Console.WriteLine();
        }

        // --- Tag-Erkennung (entspricht suffix_get_tag / prefix_get_tag) --

        private static InState SuffixGetTag(string str)
        {
            if (string.IsNullOrEmpty(str))
                return InState.None;

            for (int i = 1; i < Tags.Length; i++)
            {
                if (str.EndsWith(Tags[i], StringComparison.Ordinal))
                    return (InState)i;
            }

            return InState.None;
        }

        private static InState PrefixGetTag(string str)
        {
            if (string.IsNullOrEmpty(str))
                return InState.None;

            for (int i = 1; i < Tags.Length; i++)
            {
                if (str.StartsWith(Tags[i], StringComparison.Ordinal))
                    return (InState)i;
            }

            return InState.None;
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
                Console.WriteLine($"*** ROM-Name erkannt: {_romName}");
            }

            if (Verbose)
                Console.WriteLine($"Neuer Zustand nach Name: {Tags[(int)state]}");

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
                while (digits < sizeStr.Length && char.IsDigit(sizeStr[digits]))
                    digits++;

                _nrBanks = digits > 0 ? int.Parse(sizeStr.Substring(0, digits)) : 0;
                Console.WriteLine(
                    $"*** ROM-Größe erkannt: {_nrBanks * BankSize} Bytes ({_nrBanks} x {BankSize / 1024}kB)");
            }

            if (Verbose)
                Console.WriteLine($"Neuer Zustand nach ROM-Bänken: {Tags[(int)state]}");

            return state;
        }

        // --- ROM-Dump ------------------------------------------------------

        private static string CreateFilename(string romName, byte[] buf)
        {
            if (buf.Length < GbMagicOffset + GbMagic.Length)
                return null;

            bool isGbc = false;
            for (int i = 0; i < GbMagic.Length; i++)
            {
                if (buf[GbMagicOffset + i] != GbMagic[i])
                {
                    isGbc = true;
                    break;
                }
            }

            return isGbc ? $"{romName}.gbc" : $"{romName}.gb";
        }

        private void DumpRom()
        {
            int romSize = _nrBanks * BankSize;
            Console.WriteLine("*** Starte ROM-Dump");
            Console.Write("***  00%");

            var buf = new byte[romSize];
            for (int offset = 0; offset < romSize; offset++)
            {
                buf[offset] = ReadByte();

                if (offset % 1024 == 0 || offset == romSize - 1)
                    Console.Write($"\b\b\b{(offset + 1) * 100 / romSize:D2}%");
            }

            Console.WriteLine("\b\b\b100%");

            var filename = CreateFilename(_romName, buf) ?? $"{_romName}.bin";
            Console.WriteLine($"*** Speichere '{filename}'");
            File.WriteAllBytes(filename, buf);
            Console.WriteLine($"*** '{filename}' geschrieben");
        }

        // --- Hauptschleife (entspricht fd_watch) ---------------------------

        /// <summary>
        /// Blockierende Hauptschleife. Läuft bis ein ROM erfolgreich
        /// gedumpt wurde oder ein Fehler auftritt.
        /// </summary>
        public void Run()
        {
            Console.WriteLine("*** Warte auf Cartridge / Smartboy-Daten");

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
                    Console.WriteLine("*** Fordere ROM an");
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
                            // Srm/End/StartRom außerhalb des erwarteten Moments gehören zum
                            // autonomen Chatter des Geräts oder treten auf, weil wir mitten
                            // im laufenden Zyklus mitlesen - einfach ignorieren und auf den
                            // nächsten bekannten Tag warten, statt abzustürzen.
                            if (Verbose)
                                Console.WriteLine($"Zustand {Tags[(int)_state]} außerhalb des erwarteten Ablaufs ignoriert");
                            _state = InState.None;
                            _tagPos = 0;
                            break;
                    }

                    if (_state != InState.Nr)
                        continue;
                }

                byte b = ReadByte();

                if (_state == InState.None)
                {
                    for (int i = 1; i < Tags.Length; i++)
                    {
                        if (b == (byte)Tags[i][0])
                        {
                            _state = (InState)i;
                            _tagPos = 1;   // erstes Zeichen wurde bereits erkannt
                            if (Verbose)
                                Console.WriteLine($"Möglicher neuer Zustand {Tags[i]}");
                            break;
                        }
                    }

                    continue;
                }

                var tag = Tags[(int)_state];
                var partial = tag.Substring(0, Math.Min(_tagPos, tag.Length)) + (char)b;

                var newPossibleState = PrefixGetTag(partial);
                if (newPossibleState != InState.None && newPossibleState != _state)
                {
                    if (Verbose)
                        Console.WriteLine($"Neuer möglicher Zustand {Tags[(int)newPossibleState]}, war {tag}");
                    _state = newPossibleState;
                    tag = Tags[(int)_state];
                }

                if (_tagPos < tag.Length && tag[_tagPos] == (char)b)
                {
                    _tagPos++;
                    if (_tagPos == tag.Length)
                    {
                        if (Verbose)
                            Console.WriteLine($"Zustand {tag} vollständig");
                        _tagPos = -1;

                        if (_state == InState.Nr && !_cartReq)
                        {
                            Console.WriteLine("*** Cartridge einlegen");
                            _cartReq = true;
                        }
                        else
                        {
                            _cartReq = false;
                        }
                    }
                }
                else
                {
                    if (Verbose)
                        Console.WriteLine($"Alter Zustand {tag} verworfen, setze zurück");
                    _state = InState.None;
                    _tagPos = 0;
                }
            }
        }

        public void Dispose()
        {
            if (_port != null && _port.IsOpen)
                _port.Close();

            _port?.Dispose();
        }
    }
}