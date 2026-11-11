namespace DungeonRun;

/// <summary>The game on a GPU canvas filling the page (the SKMesh scene draws on the GPU).</summary>
public class GamePage : ContentPage
{
    public GamePage()
    {
        BackgroundColor = Colors.Black;
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
