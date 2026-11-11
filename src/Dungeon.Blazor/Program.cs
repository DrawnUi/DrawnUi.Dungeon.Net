global using DrawnUi;
global using DrawnUi.Draw;
global using DrawnUi.Views;
global using DrawnUi.Controls;
global using DrawnUi.Gaming;
global using DrawnUi.Infrastructure.Enums;
global using AppoMobi.Gestures;
global using SkiaSharp;
using DrawnUi.Models;
using DungeonRun;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// Dungeon Run in the browser: Blazor WebAssembly on DrawnUi.Blazor.

// Every label is text to a screen reader.
SkiaLabel.DefaultAccessibilityRole = Aria.RoleText;
// The title and the help name the framework: "DRAWNUI FOR BLAZOR SAMPLE".
DungeonApp.Framework = "BLAZOR";

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await Super.UseDrawnUi(builder)
    .WithBaseUrl(builder.HostEnvironment.BaseAddress)
    .WithOptions(o => o.UseDesktopKeyboard = true)
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
    .BuildAndRunAsync();
