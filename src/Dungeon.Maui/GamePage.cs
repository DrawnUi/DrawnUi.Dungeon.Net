namespace DungeonRun;

/// <summary>The game on a GPU canvas filling the page (the SKMesh scene draws on the GPU).</summary>
#if MACCATALYST
// the page DrawnUI hands the Mac keyboard to
public class GamePage : DrawnUiBasePage
#else
public class GamePage : ContentPage
#endif
{
    public GamePage()
    {
        BackgroundColor = Colors.Black;
#if IOS
        // the dungeon fills the screen under the notch and the home indicator; the HUD keeps to the safe insets
        SafeAreaEdges = SafeAreaEdges.None;
#endif
        Content = new Canvas
        {
            RenderingMode = RenderingModeType.Accelerated,
            // a game: every touch is its own, something moves every frame
            Gestures = GesturesMode.Lock,
            UpdateMode = UpdateModeType.Constant,
            BackgroundColor = Colors.Black,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Content = new DungeonApp().Build(),
        };
    }
}
