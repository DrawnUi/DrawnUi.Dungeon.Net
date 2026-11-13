// Scene resolution cap: the 3D scene and its post effect run a heavy shader over every pixel, so on dense screens
// (phones, Retina, 4K) they are drawn into a smaller picture and scaled up to the screen. The HUD, the texts and the
// dialogs always stay at full resolution.

namespace DungeonRun;

/// <summary>
/// The scene is drawn with at most <see cref="MaxDensity"/> pixels per point: on a screen of a higher density the
/// scene layer is laid out at <see cref="For"/> of the screen and scaled back up; at or under it, it draws 1:1.
/// </summary>
public static class SceneCap
{
    /// <summary>Pixels per point the scene may use (0 = no cap). DUNGEON_MAXDENSITY or the Android intent extra
    /// maxdensity change it for tries.</summary>
    public static float MaxDensity = float.TryParse(Environment.GetEnvironmentVariable("DUNGEON_MAXDENSITY"),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 1.33f;

    /// <summary>The share of the screen the scene is drawn at, for a canvas of <paramref name="density"/> pixels per point.</summary>
    public static float For(double density) =>
        MaxDensity > 0.0f && density > MaxDensity ? (float)(MaxDensity / density) : 1.0f;
}

/// <summary>
/// A layer whose children draw into an offscreen GPU picture of the layer's own size, then shown as an image (scaled
/// with the layer's transform, smoothly filtered). Laid out at a share of the screen and scaled back up, it draws its
/// content with fewer pixels. The picture is made again only when the size changes.
/// </summary>
public unsafe class OffscreenLayer : SkiaLayer
{
    // SkiaSharp's own bindings, called through function pointers: a snapshot of the surface drawn with linear
    // filtering and released without a managed SKImage per frame. Null if this SkiaSharp lacks them: then Snapshot().
    static readonly delegate*<IntPtr, IntPtr> SnapshotImage = (delegate*<IntPtr, IntPtr>)Native("sk_surface_new_image_snapshot");
    static readonly delegate*<IntPtr, IntPtr, SKRect*, SKRect*, SKSamplingOptions*, IntPtr, void> DrawImageRect =
        (delegate*<IntPtr, IntPtr, SKRect*, SKRect*, SKSamplingOptions*, IntPtr, void>)Native("sk_canvas_draw_image_rect");
    static readonly delegate*<IntPtr, void> UnrefImage = (delegate*<IntPtr, void>)Native("sk_image_unref");

    static IntPtr Native(string name)
    {
        var method = typeof(SKSurface).Assembly.GetType("SkiaSharp.SkiaApi")?.GetMethod(name,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        return method == null ? IntPtr.Zero : method.MethodHandle.GetFunctionPointer();
    }

    /// <summary>Off: the children draw straight onto the canvas.</summary>
    public bool Offscreen { get; set; }

    SKSurface _surface;
    SkiaDrawingContext _recording;
    readonly SKPaint _paint = new();
    static readonly SKSamplingOptions Linear = new(SKFilterMode.Linear, SKMipmapMode.None);

    protected override void Paint(DrawingContext ctx)
    {
        var dest = ctx.Destination;
        if (!Offscreen || dest.Width < 1 || dest.Height < 1)
        {
            base.Paint(ctx);
            return;
        }
        int width = (int)Math.Ceiling(dest.Width), height = (int)Math.Ceiling(dest.Height);
        if (_surface == null || _recording.Width != width || _recording.Height != height)
        {
            if (_surface != null)
                ReturnSurface(_surface);
            _surface = CreateSurface(width, height, true);
            _recording = new SkiaDrawingContext { IsVirtual = true, Width = width, Height = height, Surface = _surface, Canvas = _surface.Canvas };
        }
        _recording.Superview = ctx.Context.Superview;
        _recording.FrameTimeNanos = ctx.Context.FrameTimeNanos;
        var canvas = _surface.Canvas;
        canvas.Clear();
        canvas.Save();
        canvas.Translate(-dest.Left, -dest.Top);
        base.Paint(ctx.WithContext(_recording));
        canvas.Restore();
        canvas.Flush();
        var to = new SKRect(dest.Left, dest.Top, dest.Left + width, dest.Top + height);
        if (SnapshotImage != null && DrawImageRect != null && UnrefImage != null)
        {
            var from = new SKRect(0, 0, width, height);
            var sampling = Linear;
            IntPtr image = SnapshotImage(_surface.Handle);
            DrawImageRect(ctx.Context.Canvas.Handle, image, &from, &to, &sampling, _paint.Handle);
            UnrefImage(image); // before the next frame draws into the surface: no copy of the picture
        }
        else
        {
            using var image = _surface.Snapshot();
            ctx.Context.Canvas.DrawImage(image, to, Linear, _paint);
        }
    }

    public override void OnDisposing()
    {
        if (_surface != null)
            ReturnSurface(_surface);
        _surface = null;
        _paint.Dispose();
        base.OnDisposing();
    }
}
