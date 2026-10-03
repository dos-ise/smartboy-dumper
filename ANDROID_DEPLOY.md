# Build & Installation der Android-App (SmartBoyDumperMAUI) per ADB

Stand: 2026-10-03

## Umgebung
- .NET SDK `11.0.100-preview.7`, Workloads `android`, `maui` installiert
- Testgerät: Google Pixel 8a (`akita`), Android 17, mit angeschlossenem Hyperkin-Gerät
- ADB: `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe`

## 1. ADB global verfügbar machen
`adb` war nicht im PATH. Der Ordner wurde dauerhaft zum Benutzer-PATH hinzugefügt
(neue Terminals müssen geöffnet werden):

```powershell
$d="C:\Program Files (x86)\Android\android-sdk\platform-tools"
[Environment]::SetEnvironmentVariable("Path",
  ([Environment]::GetEnvironmentVariable("Path","User").TrimEnd(';')+";"+$d),"User")
```

## 2. Verbindung zum Gerät
- `192.168.178.72` (erster Versuch): Ping ok, Port 5555 geschlossen, Status blieb `offline`. Keine Verbindung.
- `192.168.178.155`: Port 5555 geschlossen. Wireless Debugging verwendet zufällige Ports,
  daher Portscan 30000–50000 (offen u.a. 33527, 33617, 45063, 45901).
- Pairing über Entwickleroptionen → Wireless Debugging → Kopplungscode:
  ```powershell
  adb pair 192.168.178.155:<Pairing-Port>   # Code eingeben
  ```
  Erfolgreich auf Port 45063. Danach erscheint das Gerät per mDNS automatisch:
  ```
  adb devices -l
  adb-3C211JEKB03986-1HhiJn._adb-tls-connect._tcp  device  model:Pixel_8a
  ```
- Hinweis: Pairing-Port und Connect-Port sind verschieden; der Pairing-Port ändert sich bei jedem Öffnen des Dialogs.

## 3. Build
```powershell
cd dotnet\smartboy-dumper
dotnet build smartboy-dumper.slnx -c Debug
```
Ergebnis: 0 Fehler, 30 Warnungen (Obsolete-/Nullable-/Plattform-API-Hinweise), ca. 2 min.
Projekte: `smartboy-dumper`, `SmartBoyDumperGUI`, `SmartBoyDumperMAUI` (net11.0-android).

## 4. Installation
```powershell
cd SmartBoyDumperMAUI
dotnet build -c Debug -t:Install -f net11.0-android --no-restore
adb shell pm list packages | Select-String smartboy
adb shell monkey -p com.dossoft.smartboydumpermaui 1   # App starten
```
Ergebnis: Paket `com.dossoft.smartboydumpermaui` installiert und gestartet.

## 5. Funktionstest mit Hyperkin Smartboy
- Gerät erkannt: VID 16D0 / PID 0557 (Filter in `device_filter.xml`), Pixel ist USB-Host (`host_connected=true`).
- App per `adb shell monkey -p com.dossoft.smartboydumpermaui 1` gestartet, "Start Dump" per
  `adb shell input tap 540 774` ausgelöst. Fortschritt per Screenshot (`adb shell screencap`) geprüft.
- Die USB-Berechtigung war bereits erteilt, es erschien kein Dialog und die App lief ohne Absturz (kein FATAL im Logcat).
- Ergebnis: `/sdcard/Download/CONKER CGB.gb`, 2.097.152 Bytes (2 MB, 08:34).
  Header per `adb pull` geprüft: Titel "CONKER CGB", Cartridge-Typ 0x1B (MBC5+RAM+Battery),
  ROM-Größe-Code 0x06 (2 MB), Header-Checksumme gültig, Daten plausibel (kein leerer 0x00/0xFF-Dump).
- Hinweis: Eine vollständige Prüfsumme gegen ein Referenz-ROM wurde nicht durchgeführt.

## 6. Test im Emulator (My Boy! GBC, `com.fastemulator.gbc`)
- Direktes Öffnen per `am start -a VIEW -n com.fastemulator.gbc/.EmulatorActivity -d file:///...` lädt das ROM nicht
  (die App landet nur in der ROM-Liste).
- Funktionierender Weg: `RomListActivity` → "+" → "Add a game" → Dateiauswahl → `Download/CONKER CGB.gb` → Eintrag antippen.
- Ergebnis: Das Spiel startet ohne Absturz, Sprachauswahl (English/Français/Deutsch) wird korrekt dargestellt.
  Der Dump ist damit lauffähig und gültig.
- Hinweis zur Automatisierung: Screenshots (`adb shell screencap`) sind 900 px breit skaliert gegenüber der Displayauflösung 1080x2400;
  Tap-Koordinaten müssen mit Faktor 1,2 umgerechnet werden.
