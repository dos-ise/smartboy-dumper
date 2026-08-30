using System;
using System.IO;
using System.Text;
using System.Windows.Controls;

namespace SmartBoyDumperGUI
{
    /// <summary>
    /// Leitet Console.Write/-WriteLine in eine TextBox um, inklusive
    /// Unterstützung für Backspace ('\b'), das SmartboyDumper für seine
    /// Fortschrittsanzeige (z.B. "00%" -> "01%") verwendet.
    /// </summary>
    public class TextBoxWriter : TextWriter
    {
        private readonly TextBox _textBox;
        private readonly StringBuilder _baseText = new();
        private readonly StringBuilder _currentLine = new();

        public TextBoxWriter(TextBox textBox)
        {
            _textBox = textBox;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\b')
            {
                if (_currentLine.Length > 0)
                    _currentLine.Length--;
            }
            else if (value == '\n')
            {
                _baseText.Append(_currentLine).Append(Environment.NewLine);
                _currentLine.Clear();
            }
            else if (value != '\r')
            {
                _currentLine.Append(value);
            }

            UpdateTextBox();
        }

        public override void Write(string? value)
        {
            if (value == null) return;
            foreach (char c in value)
                Write(c);
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            Write('\n');
        }

        private void UpdateTextBox()
        {
            _textBox.Dispatcher.Invoke(() =>
            {
                _textBox.Text = _baseText.ToString() + _currentLine;
                _textBox.ScrollToEnd();
            });
        }
    }
}