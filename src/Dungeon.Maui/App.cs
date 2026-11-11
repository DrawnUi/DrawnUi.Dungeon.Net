namespace DungeonRun;

/// <summary>One window with the game's page; every label draws in the game's font unless it names another.</summary>
public class App : Application
{
    public App()
    {
        // The first font registered is the default in DrawnUI for Rust: every label is FontGame.
        Resources.Add(new Style(typeof(SkiaLabel))
        {
            ApplyToDerivedTypes = true,
            Setters = { new Setter { Property = SkiaLabel.FontFamilyProperty, Value = "FontGame" } },
        });
    }

    protected override Window CreateWindow(IActivationState activationState) => new(new GamePage()) { Title = "Dungeon Run" };
}
