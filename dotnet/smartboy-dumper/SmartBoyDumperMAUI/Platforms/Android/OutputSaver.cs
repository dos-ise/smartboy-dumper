using System;
using System.IO;
using Android.Content;
using Android.OS;
using Android.Provider;
using AndroidX.DocumentFile.Provider;

namespace SmartBoyDumperMAUI.Platforms.Android
{
    public static class OutputSaver
    {
        /// <summary>Speichert dialogfrei im öffentlichen Downloads-Ordner (Standardverhalten).</summary>
        public static (string DisplayPath, global::Android.Net.Uri Uri) SaveToDownloads(
            Context context, string sourcePath, string mimeType = "application/octet-stream")
        {
            var fileName = Path.GetFileName(sourcePath);
            var resolver = context.ContentResolver!;

            var values = new ContentValues();
            values.Put(MediaStore.MediaColumns.DisplayName, fileName);
            values.Put(MediaStore.MediaColumns.MimeType, mimeType);
            values.Put(MediaStore.MediaColumns.RelativePath, global::Android.OS.Environment.DirectoryDownloads);

            var collection = MediaStore.Downloads.GetContentUri(MediaStore.VolumeExternal)!;
            var itemUri = resolver.Insert(collection, values)
                ?? throw new IOException("Konnte Eintrag in Downloads nicht anlegen.");

            using (var output = resolver.OpenOutputStream(itemUri))
            using (var input = File.OpenRead(sourcePath))
            {
                input.CopyTo(output!);
            }

            return ($"Downloads/{fileName}", itemUri);
        }

        /// <summary>Speichert in einem vom Nutzer gewählten, persistenten Ordner (SAF).</summary>
        public static (string DisplayPath, global::Android.Net.Uri Uri) SaveToCustomFolder(
            Context context, string sourcePath, global::Android.Net.Uri treeUri, string mimeType = "application/octet-stream")
        {
            var fileName = Path.GetFileName(sourcePath);

            var dir = DocumentFile.FromTreeUri(context, treeUri)
                ?? throw new IOException("Ordner ist nicht mehr zugänglich. Bitte erneut auswählen.");

            // Vorhandene Datei mit gleichem Namen ersetzen
            dir.FindFile(fileName)?.Delete();

            var newFile = dir.CreateFile(mimeType, fileName)
                ?? throw new IOException("Konnte Datei im gewählten Ordner nicht anlegen.");

            using (var output = context.ContentResolver!.OpenOutputStream(newFile.Uri))
            using (var input = File.OpenRead(sourcePath))
            {
                input.CopyTo(output!);
            }

            var folderName = dir.Name ?? "Custom folder";
            return ($"{folderName}/{fileName}", newFile.Uri);
        }

        /// <summary>Menschenlesbarer Name eines Ordners für die Anzeige in der UI.</summary>
        public static string GetFolderDisplayName(Context context, global::Android.Net.Uri treeUri)
        {
            var dir = DocumentFile.FromTreeUri(context, treeUri);
            return dir?.Name ?? "Selected folder";
        }
    }
}