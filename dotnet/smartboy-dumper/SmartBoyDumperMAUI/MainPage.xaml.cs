using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using CommunityToolkit.Maui.Storage;
using smartboy_dumper;
using SmartBoyDumperMAUI.Platforms.Android;

namespace SmartBoyDumperMAUI
{
    public partial class MainPage : ContentPage
    {
        private enum UiState { Idle, Running, WaitingForCartridge, CartridgeDetected, Dumping, Success, Error }
        private global::Android.Net.Uri? _outputFolderUri;
        private SmartboyDumper? _dumper;
        // Separates Feld für den Transport (nicht nur lokal in
        // ActionButton_Clicked), damit der Cancel-Button ihn erreichen
        // und den laufenden Lese-Loop kooperativ abbrechen kann, statt
        // direkt Dispose() auf dem UI-Thread aufzurufen.
        private AndroidUsbSerialTransport? _transport;
        private string? _lastResultPath;
        private UiState _state = UiState.Idle;

        public MainPage()
        {
            InitializeComponent();
            LoadOutputFolderPreference();
        }

        private void LoadOutputFolderPreference()
        {
            var saved = Preferences.Default.Get<string?>("output_folder_uri", null);

            if (!string.IsNullOrEmpty(saved))
            {
                _outputFolderUri = global::Android.Net.Uri.Parse(saved);
                var context = global::Android.App.Application.Context;
                OutputFolderLabel.Text = $"Saving to: {OutputSaver.GetFolderDisplayName(context, _outputFolderUri!)}";
            }
            else
            {
                OutputFolderLabel.Text = "Saving to: Downloads";
            }
        }

        private async void ActionButton_Clicked(object sender, EventArgs e)
        {
            if (_state is UiState.Running or UiState.WaitingForCartridge or
                UiState.CartridgeDetected or UiState.Dumping)
            {
                // WICHTIG: nicht direkt Dispose() auf dem UI-Thread aufrufen.
                // Wenn die USB-Verbindung gerade hakt, kann ein noch offener
                // Bulk-Transfer im Hintergrund-Thread stecken, und Close()
                // dann darauf warten - das würde den UI-Thread einfrieren
                // (ANR). Stattdessen erst kooperativ abbrechen lassen
                // (Read-Schleife beendet sich selbst binnen ~1s) und das
                // eigentliche Dispose() auf einem Hintergrund-Thread machen.
                _transport?.Cancel();
                var dumperToDispose = _dumper;
                _dumper = null;
                _transport = null;
                SetState(UiState.Idle);

                await Task.Run(() => dumperToDispose?.Dispose());
                return;
            }

            ErrorCard.IsVisible = false;
            ResultSection.IsVisible = false;
            ProgressSection.IsVisible = false;

            var context = global::Android.App.Application.Context;
            var device = await UsbPermissionHelper.FindAndRequestDeviceAsync(context);

            if (device == null)
            {
                ShowError("No Smartboy adapter found, or access was denied.");
                return;
            }

            var manager = (UsbManager)context.GetSystemService(Context.UsbService)!;

            AndroidUsbSerialTransport transport;
            try
            {
                transport = new AndroidUsbSerialTransport(manager, device);
            }
            catch (SmartboyException ex)
            {
                ShowError(ex.Message);
                return;
            }

            _transport = transport;

            var outputDir = Path.Combine(FileSystem.Current.AppDataDirectory, "Dumps");
            _dumper = new SmartboyDumper(transport, outputDir);

            _dumper.LogMessage += (_, msg) => global::Android.Util.Log.Info("SmartboyDumper", msg);

            _dumper.CartridgeAwaited += (_, __) => MainThread.BeginInvokeOnMainThread(() =>
                SetState(UiState.WaitingForCartridge));

            _dumper.RomNameDetected += (_, name) => MainThread.BeginInvokeOnMainThread(() =>
                SetState(UiState.CartridgeDetected, romName: name));

            _dumper.RomSizeDetected += (_, sizeBytes) => MainThread.BeginInvokeOnMainThread(() =>
                SetState(UiState.Dumping, romSizeBytes: sizeBytes));

            _dumper.DumpProgressChanged += (_, percent) => MainThread.BeginInvokeOnMainThread(() =>
                UpdateProgress(percent));

            _dumper.DumpCompleted += (_, path) => MainThread.BeginInvokeOnMainThread(async () =>
            {
                SetState(UiState.Success, filePath: path);
                await SaveOutputAsync(path);
            });

            SetState(UiState.Running);

            try
            {
                await Task.Run(() => _dumper!.Run());
            }
            catch (OperationCanceledException)
            {
                // Nutzer hat selbst über den Cancel-Button abgebrochen -
                // kein Fehlerdialog nötig, State wurde dort schon gesetzt.
            }
            catch (SmartboyException ex)
            {
                MainThread.BeginInvokeOnMainThread(() => ShowError(ex.Message));
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() => ShowError($"Unexpected error: {ex.GetType().Name}: {ex.Message}"));
            }
        }

        private void SetState(UiState state, string? romName = null, int romSizeBytes = 0, string? filePath = null)
        {
            _state = state;

            switch (state)
            {
                case UiState.Idle:
                    StatusIcon.Text = "🎮";
                    StatusTitle.Text = "Ready";
                    StatusSubtitle.Text = "Connect the adapter and start.";
                    ActionButton.Text = "Start Dump";
                    ActionButton.BackgroundColor = (Color)Resources["PrimaryColor"];
                    ProgressSection.IsVisible = false;
                    ResultSection.IsVisible = false;
                    break;

                case UiState.Running:
                    StatusIcon.Text = "🔌";
                    StatusTitle.Text = "Connected";
                    StatusSubtitle.Text = "Waiting for device status...";
                    ActionButton.Text = "Cancel";
                    ActionButton.BackgroundColor = Colors.Gray;
                    SaveAsButton.IsEnabled = true;      // <- neu
                    SaveAsButton.Text = "Save to Downloads";  // <- neu
                    break;

                case UiState.WaitingForCartridge:
                    StatusIcon.Text = "📥";
                    StatusTitle.Text = "Ready to Read";
                    StatusSubtitle.Text = "Please insert a cartridge.";
                    ActionButton.Text = "Cancel";
                    break;

                case UiState.CartridgeDetected:
                    StatusIcon.Text = "🧩";
                    StatusTitle.Text = romName ?? "Cartridge Detected";
                    StatusSubtitle.Text = "Determining ROM size...";
                    ActionButton.Text = "Cancel";
                    break;

                case UiState.Dumping:
                    StatusIcon.Text = "💾";
                    StatusTitle.Text = "Reading Cartridge";
                    StatusSubtitle.Text = $"Transferring {romSizeBytes / 1024} KB";
                    ProgressSection.IsVisible = true;
                    DumpProgressBar.Progress = 0;
                    ProgressLabel.Text = "0%";
                    ActionButton.Text = "Cancel";
                    break;

                case UiState.Success:
                    _lastResultPath = filePath;
                    StatusIcon.Text = "✅";
                    StatusTitle.Text = "Done!";
                    StatusSubtitle.Text = "The ROM file has been saved successfully.";
                    ProgressSection.IsVisible = false;
                    ResultSection.IsVisible = true;
                    ResultPathLabel.Text = filePath;
                    ActionButton.Text = "New Dump";
                    ActionButton.BackgroundColor = (Color)Resources["PrimaryColor"];
                    _dumper?.Dispose();
                    _dumper = null;
                    _transport = null;
                    break;

                case UiState.Error:
                    ActionButton.Text = "Try Again";
                    ActionButton.BackgroundColor = (Color)Resources["PrimaryColor"];
                    _dumper?.Dispose();
                    _dumper = null;
                    _transport = null;
                    break;
            }
        }

        private void UpdateProgress(int percent)
        {
            DumpProgressBar.Progress = percent / 100.0;
            ProgressLabel.Text = $"{percent}%";
        }

        private void ShowError(string message)
        {
            ErrorLabel.Text = message;
            ErrorCard.IsVisible = true;
            SetState(UiState.Error);
        }

        private async void SaveAsButton_Clicked(object sender, EventArgs e)
        {
            await SaveOutputAsync(_lastResultPath);
        }

        private global::Android.Net.Uri? _lastResultUri;

        // In SaveToDownloadsAsync ergänzen:
        private async Task SaveOutputAsync(string? sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return;

            SaveAsButton.IsEnabled = false;

            try
            {
                var context = global::Android.App.Application.Context;

                var (displayPath, uri) = await Task.Run(() =>
                    _outputFolderUri != null
                        ? OutputSaver.SaveToCustomFolder(context, sourcePath, _outputFolderUri)
                        : OutputSaver.SaveToDownloads(context, sourcePath));

                var savePath = Path.ChangeExtension(sourcePath, ".sav");
                if (File.Exists(savePath))
                {
                    var (saveDisplay, _) = await Task.Run(() =>
                        _outputFolderUri != null
                            ? OutputSaver.SaveToCustomFolder(context, savePath, _outputFolderUri)
                            : OutputSaver.SaveToDownloads(context, savePath));
                    displayPath = $"{displayPath} + {Path.GetFileName(saveDisplay)}";
                }

                _lastResultUri = uri;
                ResultPathLabel.Text = $"Saved to {displayPath}";
                SaveAsButton.Text = "Saved ✓";
                PlayButton.IsVisible = true;
            }
            catch (Exception ex)
            {
                ShowError($"Save failed: {ex.Message}");
                SaveAsButton.IsEnabled = true;
            }
        }

        private void PlayButton_Clicked(object sender, EventArgs e)
        {
            var context = global::Android.App.Application.Context;
            var preferred = Preferences.Default.Get<string?>("preferred_emulator", null);

            EmulatorLauncher.TryOpenDirectly(context, preferred);
        }

        private async void ShowLastCrash_Clicked(object sender, EventArgs e)
        {
            var path = Path.Combine(FileSystem.Current.AppDataDirectory, "crash.txt");
            var text = File.Exists(path) ? File.ReadAllText(path) : "No crash log available.";
            await DisplayAlert("Last Crash", text, "OK");
        }

        protected override void OnDisappearing()
        {
            // Gleiches Prinzip wie beim Cancel-Button: erst kooperativ
            // abbrechen, dann erst (im Hintergrund) entsorgen.
            _transport?.Cancel();
            var dumperToDispose = _dumper;
            _dumper = null;
            _transport = null;
            Task.Run(() => dumperToDispose?.Dispose());
            base.OnDisappearing();
        }

        private async void ChangeFolderButton_Clicked(object sender, EventArgs e)
        {
            var activity = Platform.CurrentActivity as Activity;
            if (activity == null) return;

            var uri = await FolderPickerHelper.PickFolderAsync(activity);
            if (uri == null) return; // Nutzer hat abgebrochen

            var context = global::Android.App.Application.Context;
            context.ContentResolver!.TakePersistableUriPermission(uri,
                ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);

            _outputFolderUri = uri;
            Preferences.Default.Set("output_folder_uri", uri.ToString());
            OutputFolderLabel.Text = $"Saving to: {OutputSaver.GetFolderDisplayName(context, uri)}";
        }
    }
}