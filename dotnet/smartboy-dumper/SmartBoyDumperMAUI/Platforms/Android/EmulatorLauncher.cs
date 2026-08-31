using Android.Content;
using Android.Content.PM;

namespace SmartBoyDumperMAUI.Platforms.Android
{
    public static class EmulatorLauncher
    {
        private static readonly string[] KnownEmulators =
        {
            "com.fastemulator.gbc",      // My OldBoy! (kostenpflichtig)
            "com.fastemulator.gbcfree",  // My OldBoy! Free
        };

        public static bool TryOpenDirectly(Context context, string? preferredPackage = null)
        {
            var pm = context.PackageManager;

            // 1. Preferred Package zuerst testen
            if (!string.IsNullOrEmpty(preferredPackage))
            {
                if (IsInstalled(pm, preferredPackage))
                    return LaunchPackage(context, preferredPackage);
            }

            // 2. Alle bekannten Emulatoren testen
            foreach (var pkg in KnownEmulators)
            {
                if (IsInstalled(pm, pkg))
                    return LaunchPackage(context, pkg);
            }

            return false;
        }

        private static bool IsInstalled(PackageManager pm, string packageName)
        {
            try
            {
                pm.GetPackageInfo(packageName, PackageInfoFlags.Activities);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool LaunchPackage(Context context, string packageName)
        {
            try
            {
                var pm = context.PackageManager;

                // Hole den Launcher-Intent der App
                var intent = pm.GetLaunchIntentForPackage(packageName);
                if (intent == null)
                    return false;

                intent.AddFlags(ActivityFlags.NewTask);

                context.StartActivity(intent);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
