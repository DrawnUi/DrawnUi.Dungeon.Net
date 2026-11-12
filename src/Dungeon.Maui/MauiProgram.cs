global using DrawnUi;
global using DrawnUi.Draw;
global using DrawnUi.Views;
global using DrawnUi.Controls;
global using DrawnUi.Gaming;
global using DrawnUi.Infrastructure.Enums;
global using AppoMobi.Gestures;
global using SkiaSharp;
// DrawnUi and MAUI both declare these; the game means the drawn ones.
global using FontWeight = DrawnUi.Draw.FontWeight;
using DrawnUi.Models;

namespace DungeonRun;

/// <summary>Dungeon Run on .NET MAUI (Android, iOS, Mac Catalyst): the fonts through the MAUI font pipeline, the game in one page.</summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Every label is text to a screen reader.
        SkiaLabel.DefaultAccessibilityRole = Aria.RoleText;
        // The title and the help name the framework: "DRAWNUI FOR MAUI SAMPLE".
        DungeonApp.Framework = "MAUI";

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Orbitron-Regular.ttf", "FontGame");
                fonts.AddFont("Orbitron-Black.ttf", "FontScore");
                fonts.AddFont("NotoSansMathSymbols-Subset.ttf", DungeonApp.SYMBOLS);
            });

        builder.UseDrawnUi(new DrawnUiStartupSettings
        {
#if MACCATALYST
            // the keys of the desktop heads (arrows, Space, Escape) and their window: 1100x700 points
            UseDesktopKeyboard = true,
            DesktopWindow = new() { Width = 1100, Height = 700 },
#endif
        });

        return builder.Build();
    }
}
