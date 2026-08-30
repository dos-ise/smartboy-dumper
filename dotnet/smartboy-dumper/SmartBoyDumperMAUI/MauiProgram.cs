using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;

namespace SmartBoyDumperMAUI;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
            .UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            System.IO.File.WriteAllText(
                Path.Combine(FileSystem.Current.AppDataDirectory, "crash.txt"),
                ex?.ToString() ?? "Unbekannter Fehler");
        };

        return builder.Build();
	}
}
