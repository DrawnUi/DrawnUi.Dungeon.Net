using System.Windows;
using DrawnUi.Wpf;

namespace DungeonRun;

/// <summary>Hosts the shared game (src/Dungeon.Shared) in the window. Size and keyboard come from App.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Content = new DrawnUiElement(() => new Canvas
        {
            // a game: something moves every frame
            UpdateMode = UpdateModeType.Constant,
            BackgroundColor = Colors.Black,
        })
        {
            RenderingMode = RenderingModeType.Accelerated,
            Gestures = GesturesMode.Lock,
            Content = new DungeonApp().Build(),
        };
    }
}
