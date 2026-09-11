using System;
using System.Text;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SmartBoyDumperGUI
{
    /// <summary>
    /// TextWriter, der Konsolenausgaben gepuffert und gedrosselt in eine
    /// TextBox schreibt. Statt bei jedem Write() sofort (und synchron) auf
    /// den UI-Thread zu wechseln - was bei vielen kleinen Writes vom
    /// Dump-Thread aus zum Einfrieren des Fensters führt - werden die Daten
    /// nur in einem Puffer gesammelt. Ein Timer auf dem UI-Thread holt sich
    /// den Puffer in festen Abständen ab und aktualisiert die TextBox in
    /// einem Rutsch.
    /// </summary>
    public class TextBoxWriter : System.IO.TextWriter
    {
        private readonly TextBox _textBox;
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly object _lock = new object();
        private readonly DispatcherTimer _timer;

        public TextBoxWriter(TextBox textBox, TimeSpan? flushInterval = null)
        {
            _textBox = textBox;

            // Läuft an den Dispatcher der TextBox gebunden -> im Tick-Handler
            // sind wir automatisch auf dem UI-Thread, kein Invoke nötig.
            _timer = new DispatcherTimer(DispatcherPriority.Background, _textBox.Dispatcher)
            {
                Interval = flushInterval ?? TimeSpan.FromMilliseconds(100)
            };
            _timer.Tick += (_, _) => FlushToTextBox();
            _timer.Start();
        }

        public override Encoding Encoding => Encoding.UTF8;

        // Wird vom Hintergrund-Thread (dem laufenden Dump) aufgerufen.
        // Nur ein billiger Lock + Append - KEIN Dispatcher-Zugriff hier.
        public override void Write(char value)
        {
            lock (_lock)
                _buffer.Append(value);
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            lock (_lock)
                _buffer.Append(value);
        }

        private void FlushToTextBox()
        {
            string chunk;
            lock (_lock)
            {
                if (_buffer.Length == 0)
                    return;

                chunk = _buffer.ToString();
                _buffer.Clear();
            }

            // Nur automatisch runterscrollen, wenn der Nutzer ohnehin schon
            // am Ende war (sonst nervt es, wenn man gerade hochgescrollt hat).
            bool wasAtEnd = _textBox.VerticalOffset >= _textBox.ExtentHeight - _textBox.ViewportHeight - 1;

            _textBox.AppendText(chunk);

            if (wasAtEnd)
                _textBox.ScrollToEnd();
        }

        /// <summary>
        /// Timer stoppen und letzten Rest des Puffers noch anzeigen -
        /// im finally-Block nach Ende des Dumps aufrufen.
        /// </summary>
        public void StopAndFlush()
        {
            _timer.Stop();
            FlushToTextBox();
        }
    }
}