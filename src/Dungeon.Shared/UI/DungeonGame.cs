// The game control: a DrawnGame whose loop, keys and gestures go to the app (Rust: DrawnGame::new() with
// on_game_loop / on_key_down / on_key_up / consume_gestures), with the scene under the post effect inside.

namespace DungeonRun;

public class DungeonGame : DrawnGame
{
    readonly DungeonApp _app;
    bool _started;

    public DungeonGame(DungeonApp app)
    {
        _app = app;
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        BackgroundColor = Colors.Black;
        StartLoop();
    }

    public override void GameLoop(float deltaSeconds)
    {
        // the first tick has no previous frame to measure from
        if (!_started)
        {
            _started = true;
            deltaSeconds = 0.0f;
        }
        _app.Tick(deltaSeconds);
    }

    public override void OnKeyDown(InputKey key) => _app.KeyDown(key);

    public override void OnKeyUp(InputKey key) => _app.KeyUp(key);

    /// <summary>The game takes every press that lands on it, so what is tapped lies over it, not in it.</summary>
    public override ISkiaGestureListener ProcessGestures(SkiaGesturesParameters args, GestureEventProcessingInfo apply)
    {
        _app.Gesture(args, RenderingScale);
        return this;
    }
}
