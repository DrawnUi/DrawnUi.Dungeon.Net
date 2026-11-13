// Dungeon Run (port of examples/dungeon/src/lib.rs): a first-person runner. The dungeon is one SKMesh a frame,
// lit by its SkSL fragment program (Scene), under an SkSL post effect. Everything else is ordinary DrawnUI over it:
// a SkiaShell whose popups are the dialogs, plates for the buttons, shapes and labels for the HUD and the title,
// in the look of the HUD (dark fills, amber lines and glow). The panel of a dialog is a shader too.
// This file: the app's state and the tree. DungeonApp.Tick.cs: one frame. DungeonApp.Input.cs: keys and the
// pointer. DungeonApp.Dialogs.cs: help and pause.

using DrawnUi.Models;

namespace DungeonRun;

public partial class DungeonApp
{
    /// <summary>The amber of every line, caption and frame of the interface.</summary>
    const uint ACCENT = 0xFFFF_B060;
    /// <summary>What hurts.</summary>
    const uint HOT = 0xFFFF_3B6B;
    /// <summary>A link, and the same under the mouse.</summary>
    const uint LINK = 0xFF8C_B8FF;
    const uint LINK_HOVER = 0xFFD2_E4FF;
    const uint GOOD = 0xFF6C_FF5A;
    /// <summary>The arrows of the help come from this fallback font.</summary>
    public const string SYMBOLS = "FontSymbols";

    /// <summary>
    /// The framework the game runs on, as the title and the help name it ("DRAWNUI FOR .NET SAMPLE"): set by each head
    /// at startup, ".NET" (pure WebAssembly), "BLAZOR", "MAUI", "OPENTK", "WPF".
    /// </summary>
    public static string Framework { get; set; } = ".NET";

    /// <summary>The DrawnUI docs page of <see cref="Framework"/>, opened by the help's link.</summary>
    static string FrameworkDocs => Framework switch
    {
        "BLAZOR" => "https://drawnui.net/articles/blazor/index.html",
        "OPENTK" => "https://drawnui.net/articles/opentk/index.html",
        "WPF" => "https://drawnui.net/articles/wpf/index.html",
        _ => "https://drawnui.net/",
    };

    /// <summary>
    /// The framework's name in the bottom right corner at all times, so every screenshot and video says what runs it.
    /// Set to false to hide it (e.g. for clean captures).
    /// </summary>
    public static bool ShowWatermark = true;

    /// <summary>The watermark's text: the framework this head runs on.</summary>
    static string WatermarkText => Framework switch
    {
        ".NET" => ".NET WASM",
        "MAUI" => ".NET MAUI",
        _ => Framework,
    };

    /// <summary>The source code of this sample, opened by the help's second link.</summary>
    const string SourceCode = "https://github.com/DrawnUi/DrawnUi.Dungeon.Net";

    /// <summary>The title's gradient: gold to amber.</summary>
    static readonly uint[] GOLD = [0xFFFF_F4D0, 0xFFFF_C050, 0xFFD0_6810];
    static readonly uint[] FIRE = [0xFFFF_E870, 0xFFFF_8A20, 0xFFE0_1810];

    /// <summary>GAME OVER and the caption under it (55 points below) go up by this, to stand centered as one block.</summary>
    const float GAME_OVER_LIFT = 20.0f;
    /// <summary>Canvas widths under this (points) have no room for the score, the health and the buttons in one row.</summary>
    const float NARROW = 620.0f;
    /// <summary>A cell of the health that is out.</summary>
    const uint CELL_OFF = 0x8010_1420;

    // ---- the tree ----
    Scene _scene;
    OffscreenLayer _sceneLayer;
    float _appliedScale;
    SkiaShaderEffect _post;
    SkiaLayout _title;
    SkiaLabel _score;
    SkiaLayout _hud;
    /// <summary>Everything over the game that must stay clear of the system bars and cutouts.</summary>
    SkiaLayout _safe;
    /// <summary>The frame rate, top left, on the title only (the distance is there during a run).</summary>
    SkiaLabelFps _fps;
    /// <summary>Help and pause, top right.</summary>
    SkiaLayout _buttons;
    /// <summary>The prompt (JUMP, CHANGE LANE, a power-up's name): its glow layer, its label.</summary>
    SkiaLayout _promptBox;
    SkiaShaderEffect _promptGlow;
    SkiaLabel _prompt;
    /// <summary>The light dim and blur under a dialog.</summary>
    SkiaBackdrop _veil;
    /// <summary>The ten cells of the health, one per tenth.</summary>
    readonly SkiaShape[] _cells = new SkiaShape[10];
    SkiaLayout _scoreBox;
    /// <summary>The score's violet glow, on the score only while it jumps.</summary>
    SkiaShaderEffect _scoreGlow;
    /// <summary>The game's name on the title: smaller on a narrow screen.</summary>
    SkiaLabel _name;
    /// <summary>The best escape time of the session, on the title.</summary>
    SkiaLabel _best;
    /// <summary>The big label in the middle: the countdown, GAME OVER, YOU ESCAPED!</summary>
    SkiaLayout _count;
    SkiaShaderEffect _countGlow;
    SkiaLabel _countLabel;
    /// <summary>Over everything after GAME OVER: its frozen picture, burning away.</summary>
    SkiaLayout _curtain;
    SkiaShaderEffect _curtainEffect;
    /// <summary>"TAP OR PRESS SPACE TO CONTINUE" under GAME OVER (the title's caption style).</summary>
    SkiaLabel _restart;
    /// <summary>The mark of a row of orbs (x2, x3) by the score.</summary>
    SkiaLabel _streak;
    DungeonGame _game;

    // ---- the state (Rust `App`) ----
    float _promptPop;
    float _promptTime;
    uint _shownDistance;
    /// <summary>1 when points were just won, fading: the score jumps.</summary>
    float _pop;
    Input _input;
    /// <summary>The lane key that is held (-1 left, 1 right) and the seconds to its next step.</summary>
    int _held;
    float _holdWait;
    /// <summary>The press already steered: its release is not a tap.</summary>
    bool _swiped;
    /// <summary>Lanes the drag in progress has stepped (right positive).</summary>
    int _panSteps;
    /// <summary>A dialog is open: the world stands still.</summary>
    bool _paused;
    /// <summary>The open dialog is the help (closing it during a run brings the pause back).</summary>
    bool _helpOpen;
    /// <summary>What the labels show.</summary>
    Phase _phase;
    uint _shownScore;
    /// <summary>Lit cells of the health bar, and the hits the run has taken, as last shown.</summary>
    int _shownCells;
    uint _shownHits;
    /// <summary>The canvas is too narrow for one row of HUD.</summary>
    bool _narrow;
    /// <summary>Points: how high and wide the canvas is.</summary>
    float _canvasHeight;
    float _canvasWidth;
    Hint _hint;
    /// <summary>What the count shows (3, 2, 1, 0 = GO, -2 = GAME OVER, -3 = YOU ESCAPED!, -1 = nothing), its jump, the seconds GO stays.</summary>
    int _shownCount;
    float _countPop;
    float _go;
    bool _curtainOn;
    /// <summary>A key or a tap left GAME OVER: one frame for the curtain to take its picture, then the title.</summary>
    int _leaving;
    /// <summary>GAME OVER has stood long enough to take a key or a tap (a press in panic does not skip it).</summary>
    bool _overReady;
    /// <summary>The last press was a finger: the texts speak of taps only.</summary>
    bool _touched;
    bool _restartShown;
    float _shownBurn;
    /// <summary>The health flash, the surge, the color of its rays, the orb pulse and the ghost's flash the post effect was last given.</summary>
    readonly float[] _shownFx2 = new float[7];
    readonly float[] _shownShield = new float[2];
    float _lastBurst;
    /// <summary>The orbs' color the post effect was last given.</summary>
    readonly float[] _shownOrb = new float[3];
    float _streakTime;
    /// <summary>Seconds the prompt still shows a power-up's name.</summary>
    float _notice;
    /// <summary>How often the jump and the lane prompts were shown; three times teach it.</summary>
    readonly int[] _taught = new int[2];
    /// <summary>The ghost has the run: no prompt over it.</summary>
    bool _ghosted;
    /// <summary>A lesson waiting for the prompt to be free.</summary>
    Prompt? _pending;
    bool _taughtHeal;
    bool _taughtDrain;
    bool _taughtGhost;
    /// <summary>LOW HEALTH was said; said again after the health came back over the line.</summary>
    bool _warnedLow;
    /// <summary>Seconds run without a hit, and how many times that was praised.</summary>
    float _clean;
    uint _cheers;

    // the uniforms handed to the effects, kept for their allocation
    readonly float[] _uFx = new float[4];
    readonly float[] _uFx2 = new float[4];
    readonly float[] _uRay = new float[3];
    readonly float[] _uOrb = new float[3];
    readonly float[] _uShield = new float[2];
    readonly float[] _uBurn = new float[1];
    readonly float[] _uScoreGlow = new float[1];
    readonly float[] _uPromptGlow = new float[1];
    readonly float[] _uPromptTint = new float[3];
    readonly float[] _uCountTint = new float[3];

    /// <summary>A color (0xAARRGGBB).</summary>
    static Color C(uint argb) => Color.FromRgba((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

    /// <summary>A color (0xAARRGGBB) as the red, green and blue a shader's tint takes.</summary>
    static float[] Tint(uint color) => [(color >> 16 & 0xFF) / 255.0f, (color >> 8 & 0xFF) / 255.0f, (color & 0xFF) / 255.0f];

    /// <summary>
    /// The gradient of the heavy letters (the countdown, GAME OVER, the title, the prompts): light at the top, deep
    /// at the bottom.
    /// </summary>
    static SkiaGradient Fire(uint[] colors) => new()
    {
        Type = GradientType.Linear,
        StartXRatio = 0,
        StartYRatio = 0,
        EndXRatio = 0,
        EndYRatio = 1,
        Colors = [C(colors[0]), C(colors[1]), C(colors[2])],
    };

    static SkiaShaderEffect Glow(float glow, float[] tint) => new SkiaShaderEffect { ShaderCode = Shaders.GLOW }
        .SetUniform("uGlow", glow)
        .SetUniform("uWash", 0.0f)
        .SetUniform("uTint", tint)
        .OnShaderError((me, error) => Console.WriteLine($"dungeon: glow SkSL: {error}"));

    static SkiaLabel Line(string text, double size, uint color) => new(text)
    {
        FontSize = size,
        TextColor = C(color),
        FontFamilyFallback = SYMBOLS,
    };

    static SkiaLabel Centered(string text, double size, uint color)
    {
        var label = Line(text, size, color);
        label.HorizontalOptions = LayoutOptions.Center;
        label.HorizontalTextAlignment = DrawTextAlignment.Center;
        return label;
    }

    /// <summary>What every button is made of: a dark plate whose thin line is white at rest and amber, with a glow, under the pointer or a press.</summary>
    static Plate MakePlate(string name) => new(C(0x66FF_FFFF), C(ACCENT))
    {
        CornerRadius = 2,
        BackgroundColor = C(0xE608_0B14),
        StrokeWidth = 1.55,
        AnimationTapped = SkiaTouchAnimation.Ripple,
        // A Tab stop a screen reader calls a button; Enter and Space press it.
        AccessibilityRole = Aria.RoleButton,
        AccessibilityLabel = name,
    };

    /// <summary>A button's caption: the button itself carries the name a screen reader says.</summary>
    static SkiaLabel Caption(string text, double size)
    {
        var label = Line(text, size, 0xE6FF_FFFF);
        label.AccessibilityRole = Aria.RolePresentation;
        return label;
    }

    /// <summary>The main action: the plate with a lit LED at its left edge.</summary>
    static Plate Button(string text)
    {
        var plate = MakePlate(text);
        plate.HeightRequest = 40;
        plate.Children = new List<SkiaControl>
        {
            new SkiaShape
            {
                WidthRequest = 3,
                HeightRequest = 18,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalOptions = LayoutOptions.Center,
                BackgroundColor = C(0xFFFF_E6C0),
                Shadows = new List<SkiaShadow> { new() { Color = C(ACCENT), X = 0, Y = 0, Blur = 7, Opacity = 1 } },
            },
            Caption(text, 14).Center().WithMargin(26, 0, 20, 0),
        };
        return plate;
    }

    /// <summary>Any other action.</summary>
    static Plate GhostButton(string text)
    {
        var plate = MakePlate(text);
        plate.HeightRequest = 40;
        plate.Children = new List<SkiaControl> { Caption(text, 14).Center().WithMargin(20, 0, 20, 0) };
        return plate;
    }

    /// <summary>A square button of the HUD.</summary>
    static Plate Key(string text, string name)
    {
        var plate = MakePlate(name);
        plate.WidthRequest = 40;
        plate.HeightRequest = 40;
        plate.Children = new List<SkiaControl> { Caption(text, 15).Center() };
        return plate;
    }

    /// <summary>
    /// The part of the screen the system bars and cutouts leave free, in points, as DrawnUI measures it (0 on the web
    /// and the desktop): the 3D view and the post effect fill the whole screen, the HUD, the texts and the dialogs
    /// stay inside.
    /// </summary>
    static Thickness SafeInsets => new(Super.Screen.LeftInset, Super.Screen.TopInset, Super.Screen.RightInset, Super.Screen.BottomInset);

    void ApplySafeInsets()
    {
        _safe.Margin = SafeInsets;
        _canvasWidth = -1; // the sizes that follow the free width are set again at the next frame
    }

    void ApplyRenderScale(float scale)
    {
        _appliedScale = scale;
        _sceneLayer.HorizontalFillRatio = scale;
        _sceneLayer.VerticalFillRatio = scale;
        _sceneLayer.ScaleX = 1.0 / scale;
        _sceneLayer.ScaleY = 1.0 / scale;
        _sceneLayer.Offscreen = scale < 1.0;
        Console.WriteLine($"dungeon: scene at {scale * 100:F0} % of the screen (cap {SceneCap.MaxDensity:0.##} px per point, screen {_game?.RenderingScale:0.##})");
    }

    /// <summary>The whole tree: the shell, its root layer and everything over the game.</summary>
    public SkiaControl Build()
    {
        Super.InsetsChanged += (s, e) => MainThread.BeginInvokeOnMainThread(ApplySafeInsets);
        _post = new SkiaShaderEffect { ShaderCode = Shaders.POST }
            .SetUniform("uFx2", _uFx2)
            .SetUniform("uRay", 1.0f, 0.55f, 0.22f)
            .SetUniform("uOrb", _uOrb)
            .SetUniform("uShield", _uShield)
            .OnShaderError((me, error) => Console.WriteLine($"dungeon: post SkSL: {error}"));
        _scoreGlow = new SkiaShaderEffect { ShaderCode = Shaders.GLOW }
            .SetUniform("uGlow", 1.0f)
            .SetUniform("uWash", 0.7f)
            .SetUniform("uTint", 0.62f, 0.22f, 1.0f)
            .OnShaderError((me, error) => Console.WriteLine($"dungeon: glow SkSL: {error}"));
        _countGlow = Glow(1.0f, [1.0f, 0.16f, 0.04f]);
        _promptGlow = Glow(1.0f, [0.62f, 0.22f, 1.0f]);
        _curtainEffect = new SkiaShaderEffect { ShaderCode = Shaders.CURTAIN, UseBackground = PostRendererEffectUseBackgroud.Once }
            .SetUniform("uBurn", _uBurn)
            .OnShaderError((me, error) => Console.WriteLine($"dungeon: curtain SkSL: {error}"));

        // the dialogs open over it: a SkiaShell's popups (DungeonApp.Shell.cs)
        return Shell(new SkiaLayer
        {
            VerticalOptions = LayoutOptions.Fill,
            Children = new List<SkiaControl>
            {
                // The game takes every press that lands on it, so what is tapped lies over it, not in it.
                new DungeonGame(this)
                {
                    Children = new List<SkiaControl>
                    {
                        // The 3D scene and its post effect under the scene cap (SceneCap.cs): a layer that share
                        // of the screen, drawn offscreen below 1 and scaled back up to the whole of it.
                        new OffscreenLayer
                        {
                            HorizontalOptions = LayoutOptions.Fill,
                            VerticalOptions = LayoutOptions.Fill,
                            AnchorX = 0,
                            AnchorY = 0,
                            Children =
                            {
                                // Draws every frame: no cache. The post effect reads what it drew.
                                new Scene
                                {
                                    HorizontalOptions = LayoutOptions.Fill,
                                    VerticalOptions = LayoutOptions.Fill,
                                    VisualEffects = { _post },
                                }.Assign(out _scene),
                            },
                        }.Assign(out _sceneLayer)
                         .Initialize(me => ApplyRenderScale(1.0f)),
                    },
                }.Assign(out _game),

                // The HUD, the title and the texts stay out of the system bars and cutouts (ApplySafeInsets).
                new SkiaLayer
                {
                    VerticalOptions = LayoutOptions.Fill,
                    Children = new List<SkiaControl>
                    {
                        // The health, top center: ten slanted cells, one per tenth. One cached image, drawn again only
                        // when the health changes.
                        new SkiaStack
                        {
                            Spacing = 7,
                            WidthRequest = 266,
                            HorizontalOptions = LayoutOptions.Center,
                            Margin = new Thickness(0, 16, 0, 0),
                            Opacity = 0,
                            InputTransparent = true,
                            UseCache = SkiaCacheType.GPU,
                            // Read as one value, said again when it changes.
                            AccessibilityRole = Aria.RoleStatus,
                            AccessibilityLabel = "Health 100 percent",
                            AccessibilityLive = "polite",
                            Children = new List<SkiaControl>
                            {
                                new SkiaRow
                                {
                                    Spacing = 5,
                                    Children = Enumerable.Range(0, 10).Select(i => (SkiaControl)new SkiaShape
                                    {
                                        WidthRequest = 22,
                                        HeightRequest = 14,
                                        CornerRadius = 1,
                                        SkewX = -18,
                                        BackgroundColor = C(GOOD),
                                        StrokeColor = C(0x66FF_FFFF),
                                        StrokeWidth = 1,
                                        Shadows = new List<SkiaShadow> { new() { Color = C(GOOD), X = 0, Y = 0, Blur = 7, Opacity = 0.9 } },
                                    }.Assign(out _cells[i])).ToList(),
                                },
                            },
                        }.Assign(out _hud),

                        new SkiaLabelFps
                        {
                            FontSize = 12,
                            Padding = new Thickness(8, 4, 8, 4),
                            Margin = new Thickness(16, 16, 0, 0),
                            HorizontalOptions = LayoutOptions.Start,
                            VerticalOptions = LayoutOptions.Start,
                        }.Assign(out _fps),

                        // The distance, top left, in steps of ten meters: quiet (soft white, no glow) so it does not pull
                        // the eye from the run; an orb makes it jump in a violet flash (FlashScore).
                        new SkiaLayer
                        {
                            WidthRequest = 260,
                            HeightRequest = 60,
                            HorizontalOptions = LayoutOptions.Start,
                            Margin = new Thickness(8, 4, 0, 0),
                            Opacity = 0,
                            InputTransparent = true,
                            UseCache = SkiaCacheType.GPU,
                            Children = new List<SkiaControl>
                            {
                                new SkiaLabel("0 M")
                                {
                                    FontFamily = "FontScore",
                                    FontSize = 30,
                                    TextColor = C(0xCCFF_FFFF),
                                    StrokeColor = C(0x9900_0000),
                                    StrokeWidth = 1,
                                    AnchorX = 0,
                                    AnchorY = 0,
                                    Margin = new Thickness(14, 8, 0, 0),
                                }.Assign(out _score),
                            },
                        }.Assign(out _scoreBox),

                        new SkiaLabel("x2")
                        {
                            FontFamily = "FontScore",
                            FontSize = 16,
                            TextColor = C(0xFFC9_8BFF),
                            Margin = new Thickness(23, 52, 0, 0),
                            Opacity = 0,
                            InputTransparent = true,
                        }.Assign(out _streak),

                        new SkiaRow
                        {
                            Spacing = 8,
                            HorizontalOptions = LayoutOptions.End,
                            Margin = new Thickness(0, 14, 16, 0),
                            Children = new List<SkiaControl>
                            {
                                Key("?", "Help").OnTapped(me => ToggleDialog(HelpDialog)),
                                Key("II", "Pause").OnTapped(me => Menu()),
                            },
                        }.Assign(out _buttons),

                        // The prompt, above the middle: heavy letters in the color of what it asks, in a glow of the same
                        // color (ShowPrompt); its own cached image under the glow shader, like the countdown.
                        new SkiaLayer
                        {
                            WidthRequest = 760,
                            HeightRequest = 110,
                            HorizontalOptions = LayoutOptions.Center,
                            Margin = new Thickness(0, 96, 0, 0),
                            IsVisible = false,
                            InputTransparent = true,
                            UseCache = SkiaCacheType.GPU,
                            VisualEffects = { _promptGlow },
                            Children = new List<SkiaControl>
                            {
                                new SkiaLabel("")
                                {
                                    FontFamily = "FontScore",
                                    FontSize = 44,
                                    TextColor = Colors.White,
                                    StrokeColor = C(0xFF14_0820),
                                    StrokeWidth = 2.5,
                                    HorizontalTextAlignment = DrawTextAlignment.Center,
                                    AccessibilityLive = "assertive",
                                }.Center().Assign(out _prompt),
                            },
                        }.Assign(out _promptBox),

                        // The countdown and GAME OVER, in the middle: heavy letters, yellow to red, in a red glow.
                        new SkiaLayer
                        {
                            WidthRequest = 760,
                            HeightRequest = 220,
                            HorizontalOptions = LayoutOptions.Center,
                            VerticalOptions = LayoutOptions.Center,
                            IsVisible = false,
                            InputTransparent = true,
                            UseCache = SkiaCacheType.GPU,
                            VisualEffects = { _countGlow },
                            Children = new List<SkiaControl>
                            {
                                new SkiaLabel("3")
                                {
                                    FontFamily = "FontScore",
                                    FontSize = 120,
                                    TextColor = Colors.White,
                                    FillGradient = Fire(FIRE),
                                    StrokeColor = C(0xFF30_0404),
                                    StrokeWidth = 3,
                                    HorizontalTextAlignment = DrawTextAlignment.Center,
                                    AccessibilityLive = "assertive",
                                }.Center().Assign(out _countLabel),
                            },
                        }.Assign(out _count),

                        // Still over a scene that draws every frame: one cached image to blit.
                        new SkiaStack
                        {
                            Spacing = 14,
                            HorizontalOptions = LayoutOptions.Center,
                            VerticalOptions = LayoutOptions.Center,
                            InputTransparent = true,
                            UseCache = SkiaCacheType.GPU,
                            Children = new List<SkiaControl>
                            {
                                // The name, white in a glow of the interface's accent color: its own cached image under
                                // the glow shader.
                                new SkiaLayer
                                {
                                    HeightRequest = 120,
                                    Margin = new Thickness(0, -32, 0, -34),
                                    UseCache = SkiaCacheType.GPU,
                                    VisualEffects = { Glow(0.5f, Tint(ACCENT)) },
                                    Children = new List<SkiaControl>
                                    {
                                        // The same heavy letters as GAME OVER, gold to amber instead of fire.
                                        new SkiaLabel("DUNGEON RUN")
                                        {
                                            FontFamily = "FontScore",
                                            FontSize = 44,
                                            TextColor = Colors.White,
                                            FillGradient = Fire(GOLD),
                                            StrokeColor = C(0xFF2A_1404),
                                            StrokeWidth = 2.5,
                                            HorizontalOptions = LayoutOptions.Center,
                                            VerticalOptions = LayoutOptions.Center,
                                            HorizontalTextAlignment = DrawTextAlignment.Center,
                                        }.Assign(out _name),
                                    },
                                },
                                Centered($"DRAWNUI FOR {Framework} SAMPLE", 14, ACCENT),
                                Centered("", 11, ACCENT).WithVisibility(false).Assign(out _best),
                                Centered("TAP OR PRESS SPACE TO PLAY", 11, 0x99FF_FFFF),
                            },
                        }.Assign(out _title),

                        Centered("", 11, 0x99FF_FFFF).Adapt(me =>
                        {
                            me.VerticalOptions = LayoutOptions.Center;
                            me.Margin = new Thickness(0, 110, 0, 0);
                            me.TranslationY = -GAME_OVER_LIFT;
                            me.Opacity = 0;
                            me.InputTransparent = true;
                        }).Assign(out _restart),

                        // The framework's name, bottom right, over the post effect: faint, never read aloud.
                        new SkiaLabel(WatermarkText)
                        {
                            FontSize = 22,
                            FontFamily = "FontScore",
                            TextColor = C(0x33FF_FFFF),
                            HorizontalOptions = LayoutOptions.End,
                            VerticalOptions = LayoutOptions.End,
                            Margin = new Thickness(0, 0, 16, 12),
                            InputTransparent = true,
                            IsVisible = ShowWatermark,
                            UseCache = SkiaCacheType.Image,
                            AccessibilityRole = Aria.RolePresentation,
                        },
                    },
                }.Adapt(me => me.Margin = SafeInsets).Assign(out _safe),

                new SkiaLayer
                {
                    VerticalOptions = LayoutOptions.Fill,
                    IsVisible = false,
                    InputTransparent = true,
                    VisualEffects = { _curtainEffect },
                }.Assign(out _curtain),

                new SkiaBackdrop
                {
                    Blur = 1.5,
                    BackgroundColor = C(0x7300_0000),
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                    InputTransparent = true,
                    IsVisible = false,
                }.Assign(out _veil),
            },
        });
    }
}
