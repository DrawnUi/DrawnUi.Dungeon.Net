global using System.Runtime.InteropServices.JavaScript;
global using DrawnUi;
global using DrawnUi.Draw;
global using DrawnUi.Views;
global using DrawnUi.Controls;
global using DrawnUi.Gaming;
global using DrawnUi.Infrastructure.Enums;
global using AppoMobi.Gestures;
global using SkiaSharp;
using DrawnUi.Models;

namespace DungeonRun;

/// <summary>Dungeon Run in the browser: pure WebAssembly on DrawnUi.Web.</summary>
public static partial class Program
{
    /// <summary>The running game.</summary>
    public static DungeonApp App { get; private set; }

    [JSExport]
    public static Task Main()
    {
        // Every label is text to a screen reader.
        SkiaLabel.DefaultAccessibilityRole = Aria.RoleText;

        return Super.UseDrawnUi()
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
            .RunAsync("canvas", () => new Canvas
            {
                RenderingMode = RenderingModeType.Accelerated,
                // A game: every touch is its own. No pull-down or rubber band of the page takes a drag.
                Gestures = GesturesMode.Lock,
                BackgroundColor = Colors.Black,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Content = (App = new DungeonApp()).Build(),
            });
    }
}
