using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SmartBoyDumperMAUI
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
        private bool _romReq;
        private bool _cartReq;

        private string? _romName;
        private int _nrBanks = -1;

        public event EventHandler? CartridgeAwaited;
        public event EventHandler<string>? RomNameDetected;
        public event EventHandler<int>? RomSizeDetected;
        public event EventHandler<int>? DumpProgressChanged;
        public event EventHandler<string>? DumpCompleted;

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

        private static InState PrefixGetTag(string str)
        {
            if (string.IsNullOrEmpty(str)) return InState.None;
            for (int i = 1; i < Tags.Length; i++)
                if (str.StartsWith(Tags[i], StringComparison.Ordinal)) return (InState)i;
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
                        case InState.Nr:
                            _romName = null;
                            _nrBanks = -1;
                            _romReq = false;
                            if (!_cartReq)
                            {
                                CartridgeAwaited?.Invoke(this, EventArgs.Empty);
                                _cartReq = true;
                            }
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

                if (_state == InState.None)
                {
                    for (int i = 1; i < Tags.Length; i++)
                    {
                        if (b == (byte)Tags[i][0])
                        {
                            _state = (InState)i;
                            _tagPos = 1;
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
                    _state = newPossibleState;
                    tag = Tags[(int)_state];
                }

                if (_tagPos < tag.Length && tag[_tagPos] == (char)b)
                {
                    _tagPos++;
                    if (_tagPos == tag.Length)
                    {
                        _tagPos = -1;
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
                }
                else
                {
                    _state = InState.None;
                    _tagPos = 0;
                }
            }
        }

        public void Dispose() => _transport.Dispose();
    }
}