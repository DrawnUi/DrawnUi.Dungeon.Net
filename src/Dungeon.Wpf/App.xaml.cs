global using DrawnUi;
global using DrawnUi.Draw;
global using DrawnUi.Views;
global using DrawnUi.Controls;
global using DrawnUi.Gaming;
global using DrawnUi.Infrastructure.Enums;
global using AppoMobi.Gestures;
global using SkiaSharp;
using System.Windows;
using DrawnUi.Models;
using DrawnUi.Wpf;
using Setter = DrawnUi.Draw.Setter;
using Style = DrawnUi.Draw.Style;

namespace DungeonRun;

/// <summary>
/// Dungeon Run on WPF: the fonts and startup settings once, before the window: the Rust game's window size and the
/// keyboard for the whole window.
/// </summary>
public partial class App : Application
{
    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Every label is text to a screen reader.
        SkiaLabel.DefaultAccessibilityRole = Aria.RoleText;
        // The title and the help name the framework: "DRAWNUI FOR WPF SAMPLE".
        DungeonApp.Framework = "WPF";

        Super.UseDrawnUi()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("fonts/Orbitron-Regular.ttf", "FontGame");
                fonts.AddFont("fonts/Orbitron-Black.ttf", "FontScore");
                fonts.AddFont("fonts/NotoSansMathSymbols-Subset.ttf", DungeonApp.SYMBOLS);
            })
            .ConfigureStyles(styles =>
            {
                // The first font registered is the default in DrawnUI for Rust: every label is FontGame.
                styles.AddStyle(new Style
                {
                    TargetType = typeof(SkiaLabel),
                    ApplyToDerivedTypes = true,
                    Setters = { new Setter { Property = SkiaLabel.FontFamilyProperty, Value = "FontGame" } },
                });
            })
            .WithSettings(new DrawnUiStartupSettings
            {
                DesktopWindow = new WindowParameters { Width = 1100, Height = 700 },
                UseDesktopKeyboard = true,
            })
            .Build();
    }
}
