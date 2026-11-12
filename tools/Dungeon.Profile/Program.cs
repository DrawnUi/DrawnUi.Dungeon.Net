global using DrawnUi;
global using DrawnUi.Draw;
global using DrawnUi.Views;
global using DrawnUi.Controls;
global using DrawnUi.Gaming;
global using DrawnUi.Infrastructure.Enums;
global using AppoMobi.Gestures;
global using SkiaSharp;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using DrawnUi.Testing;

namespace DungeonRun;

/// <summary>
/// Renders the game headless and reports per frame: managed bytes allocated, time, and the types the allocations
/// were sampled in (GCAllocationTick, one sample per ~100 KB). Usage: Dungeon.Profile [frames] [run], or shot (see Shot).
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        int frames = args.Length > 0 ? int.Parse(args[0]) : 1200;
        bool run = args.Contains("run");
        if (args.Contains("mesh"))
        {
            MeshOnly(frames);
            return;
        }
        if (args.Contains("fx"))
        {
            EffectRenders(args[Array.IndexOf(args, "fx") + 1], args.Length > Array.IndexOf(args, "fx") + 2 ? args[Array.IndexOf(args, "fx") + 2] : null);
            return;
        }
        if (args.Contains("wrappers"))
        {
            Wrappers();
            return;
        }
        if (args.Contains("effect"))
        {
            EffectOnly(frames);
            return;
        }

        if (args.Contains("shot"))
        {
            Shot(args, run);
            return;
        }

        using var host = new HeadlessCanvasHost(96, 64, 1.0f, Colors.Black);
        var app = new DungeonApp();
        host.Canvas.Content = app.Build();
        if (args.Contains("noscene"))
        {
            // everything but the mesh and the post effect, which are slow on a raster surface (SkSL on the CPU)
            var scene = (Scene)typeof(DungeonApp).GetField("_scene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(app);
            scene.IsVisible = false;
        }
        host.AdvanceFrames(60);
        if (run)
        {
            app.KeyDown(InputKey.Space); // start: countdown, then the run
            host.AdvanceFrames(240);
        }

        // which controls record their cache again, and how often
        var records = new Dictionary<string, int>();
        void Watch(SkiaControl control, string path)
        {
            var name = $"{path}/{control.GetType().Name}";
            control.CreatedCache += (s, e) => records[name] = records.GetValueOrDefault(name) + 1;
            foreach (var child in control.Views.ToList())
                Watch(child, name);
        }
        Watch((SkiaControl)host.Canvas.Content, "");

        using var listener = new AllocationListener();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var gc0 = GC.CollectionCount(0);
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
        {
            if (run && i % 30 == 0)
                app.KeyDown(InputKey.ArrowUp); // keep jumping now and then, the pilot is off in a run
            host.RenderFrame(16.0);
        }
        watch.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"{(run ? "run" : "title")}: {frames} frames, {bytes / frames} B/frame, {watch.Elapsed.TotalMilliseconds / frames:F2} ms/frame, gen0 {GC.CollectionCount(0) - gc0}");
        foreach (var (type, count) in listener.Types.OrderByDescending(x => x.Value).Take(40))
            Console.WriteLine($"  {count,5} x ~100KB  {type}");
        Console.WriteLine("cache records:");
        foreach (var (name, count) in records.OrderByDescending(x => x.Value).Take(20))
            Console.WriteLine($"  {count,6}  {name}");
    }

    /// <summary>
    /// One frame of the game as a PNG, the same pixels on every run (fixed 16 ms steps, no randomness): to check that a
    /// shader change leaves the picture identical. Usage: Dungeon.Profile 0 [run] shot WIDTH HEIGHT FRAMES
    /// OUT.png [z DISTANCE]. With run the run starts at frame 30; z moves the runner there just before the
    /// frame (2850: the daylight from the exit, 2990: the world outside). The scene is drawn only on the last frame,
    /// so a long run takes seconds.
    /// </summary>
    static void Shot(string[] args, bool run)
    {
        int at = Array.IndexOf(args, "shot");
        int z = Array.IndexOf(args, "z");
        using var host = new HeadlessCanvasHost(int.Parse(args[at + 1]), int.Parse(args[at + 2]), 1.0f, Colors.Black);
        var app = new DungeonApp();
        host.Canvas.Content = app.Build();
        int frames = int.Parse(args[at + 3]);
        var scene = (Scene)typeof(DungeonApp).GetField("_scene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(app);
        for (int i = 0; i < frames; i++)
        {
            scene.IsVisible = i == frames - 1;
            if (z > 0 && i == frames - 3)
                scene.World.Z = double.Parse(args[z + 1], System.Globalization.CultureInfo.InvariantCulture);
            if (run && i == 30)
                app.KeyDown(InputKey.Space);
            if (run && i > 240 && i % 30 == 0)
                app.KeyDown(InputKey.ArrowUp);
            host.RenderFrame(16.0);
        }
        host.SavePng(args[at + 4]);
    }

    /// <summary>
    /// The SKMesh calls of Scene alone, a mesh of ~490 triangles rebuilt every frame (count varying as the corridor
    /// does) and drawn on a tiny raster surface, so the allocations of the mesh path show without the pixel cost.
    /// </summary>
    static void MeshOnly(int frames)
    {
        using DrawnUi.Draw.Meshes.SKMeshSpecification spec = DrawnUi.Draw.Meshes.SKMeshSpecification.Make(
            [
                new(DrawnUi.Draw.Meshes.SKMeshSpecificationAttributeType.Float2, 0, "position"),
                new(DrawnUi.Draw.Meshes.SKMeshSpecificationAttributeType.Float4, 8, "a"),
                new(DrawnUi.Draw.Meshes.SKMeshSpecificationAttributeType.Float4, 24, "b"),
                new(DrawnUi.Draw.Meshes.SKMeshSpecificationAttributeType.Float4, 40, "c"),
            ], 56,
            [
                new(DrawnUi.Draw.Meshes.SKMeshSpecificationVaryingType.Float4, "a"),
                new(DrawnUi.Draw.Meshes.SKMeshSpecificationVaryingType.Float4, "b"),
                new(DrawnUi.Draw.Meshes.SKMeshSpecificationVaryingType.Float4, "c"),
            ], Shaders.MESH_VS, Shaders.MESH_FS, SKColorSpace.CreateSrgb(), SKAlphaType.Premul, out var errors);
        if (spec == null)
            throw new Exception(errors);
        var floats = new float[3000 * 3 * 14];
        var rnd = new Random(1);
        for (int i = 0; i < floats.Length; i++)
            floats[i] = (float)rnd.NextDouble() * 4 + 0.5f;
        var uniforms = new float[20];
        using var surface = SKSurface.Create(new SKImageInfo(4, 4));
        using var paint = new SKPaint { Color = SKColors.White };

        void Frame(int i)
        {
            int count = (480 + i * 7 % 40) * 3;
            using var buffer = DrawnUi.Draw.Meshes.SKMeshVertexBuffer.Make(
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(floats.AsSpan(0, count * 14)));
            using var data = SKData.CreateCopy(System.Runtime.InteropServices.MemoryMarshal.AsBytes(uniforms.AsSpan()));
            using var mesh = DrawnUi.Draw.Meshes.SKMesh.Make(spec, DrawnUi.Draw.Meshes.SKMeshMode.Triangles, buffer, count, 0, data,
                new SKRect(0, 0, 4, 4), out _);
            DrawnUi.Draw.Meshes.SKMeshCanvasExtensions.DrawMesh(surface.Canvas, mesh, paint);
        }

        for (int i = 0; i < 200; i++)
            Frame(i);
        {
            // bytes per step of one frame
            long vb = 0, data = 0, make = 0, draw = 0, dispose = 0;
            for (int i = 0; i < 400; i++)
            {
                int count = (480 + i * 7 % 40) * 3;
                long t0 = GC.GetAllocatedBytesForCurrentThread();
                var buffer = DrawnUi.Draw.Meshes.SKMeshVertexBuffer.Make(
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes(floats.AsSpan(0, count * 14)));
                long t1 = GC.GetAllocatedBytesForCurrentThread();
                var u = SKData.CreateCopy(System.Runtime.InteropServices.MemoryMarshal.AsBytes(uniforms.AsSpan()));
                long t2 = GC.GetAllocatedBytesForCurrentThread();
                var mesh = DrawnUi.Draw.Meshes.SKMesh.Make(spec, DrawnUi.Draw.Meshes.SKMeshMode.Triangles, buffer, count, 0, u,
                    new SKRect(0, 0, 4, 4), out _);
                long t3 = GC.GetAllocatedBytesForCurrentThread();
                DrawnUi.Draw.Meshes.SKMeshCanvasExtensions.DrawMesh(surface.Canvas, mesh, paint);
                long t4 = GC.GetAllocatedBytesForCurrentThread();
                mesh.Dispose(); u.Dispose(); buffer.Dispose();
                long t5 = GC.GetAllocatedBytesForCurrentThread();
                vb += t1 - t0; data += t2 - t1; make += t3 - t2; draw += t4 - t3; dispose += t5 - t4;
            }
            Console.WriteLine($"steps B/frame: vb {vb / 400}, data {data / 400}, make {make / 400}, draw {draw / 400}, dispose {dispose / 400}");
        }
        using var listener = new AllocationListener();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
            Frame(i);
        watch.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"mesh: {frames} frames, {bytes / frames} B/frame, {watch.Elapsed.TotalMilliseconds / frames:F3} ms/frame");
        foreach (var (type, count) in listener.Types.OrderByDescending(x => x.Value).Take(30))
            Console.WriteLine($"  {count,5} x ~100KB  {type}");
    }

    /// <summary>
    /// The fixed input DrawnUI for Rust's effect probe draws: a vertical gradient #203040 to #C08040 over the control's
    /// height, a white 40x40 square at (100, 60), or solid blue for the curtain's second step.
    /// </summary>
    sealed class Picture : SkiaControl
    {
        public bool Blue;

        protected override void Paint(DrawingContext ctx)
        {
            base.Paint(ctx);
            var r = ctx.Destination;
            var canvas = ctx.Context.Canvas;
            using var paint = new SKPaint();
            if (Blue)
            {
                paint.Color = new SKColor(0, 0, 255);
                canvas.DrawRect(r, paint);
                return;
            }
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Left, r.Bottom),
                [new SKColor(0x20, 0x30, 0x40), new SKColor(0xC0, 0x80, 0x40)], SKShaderTileMode.Clamp);
            canvas.DrawRect(r, paint);
            paint.Shader = null;
            paint.Color = SKColors.White;
            float s = (float)ctx.Scale;
            canvas.DrawRect(r.Left + 100 * s, r.Top + 60 * s, 40 * s, 40 * s, paint);
        }
    }

    /// <summary>
    /// POST, GLOW and CURTAIN over the fixed input, as rust-dungeon rendered them (Headless CPU raster, 256x160, scale
    /// 1): writes PNGs to <paramref name="outDir"/>, prints RGBA at 9 points and, with <paramref name="rustDir"/>, the
    /// difference to Rust's PNG of the same name (5x5 window averages).
    /// </summary>
    static void EffectRenders(string outDir, string rustDir)
    {
        Directory.CreateDirectory(outDir);
        var font = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Dungeon.Web/wwwroot/fonts/Orbitron-Black.ttf"));
        SkiaFontManager.Instance.RegisterFont("FontScore", font);
        SkiaFontManager.Instance.Initialize();

        (int, int)[] points = [(0, 0), (128, 0), (255, 0), (0, 80), (128, 80), (255, 80), (0, 159), (128, 159), (255, 159)];

        void Save(HeadlessCanvasHost host, string name)
        {
            using var image = host.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var file = File.Create(Path.Combine(outDir, name + ".png")))
                data.SaveTo(file);
            var line = string.Join(" | ", points.Select(p => { var c = bitmap.GetPixel(p.Item1, p.Item2); return $"{c.Red} {c.Green} {c.Blue}"; }));
            Console.WriteLine($"{name}: {line}");
            if (rustDir == null)
                return;
            var rustPath = Path.Combine(rustDir, name + ".png");
            if (!File.Exists(rustPath))
            {
                Console.WriteLine($"  no {rustPath}");
                return;
            }
            using var rust = SKBitmap.Decode(rustPath);
            double worst = 0, sum = 0;
            int worstX = 0, worstY = 0, n = 0;
            for (int y = 2; y < bitmap.Height - 2; y += 1)
            for (int x = 2; x < bitmap.Width - 2; x += 1)
            {
                double d = 0;
                for (int ch = 0; ch < 3; ch++)
                {
                    double a = 0, b = 0;
                    for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        var p = bitmap.GetPixel(x + dx, y + dy);
                        var q = rust.GetPixel(x + dx, y + dy);
                        a += ch == 0 ? p.Red : ch == 1 ? p.Green : p.Blue;
                        b += ch == 0 ? q.Red : ch == 1 ? q.Green : q.Blue;
                    }
                    d = Math.Max(d, Math.Abs(a - b) / 25);
                }
                sum += d;
                n++;
                if (d > worst)
                    (worst, worstX, worstY) = (d, x, y);
            }
            Console.WriteLine($"  vs Rust (5x5 means): mean {sum / n:F2}, worst {worst:F1} at ({worstX},{worstY})");
        }

        // POST on the picture (no cache: the engine makes the input texture)
        (string name, float[] fx, float[] fx2, float[] shield)[] states =
        [
            ("post_idle", [0, 0, 0, 1], [0, 0, 0, 0], [0, 0]),
            ("post_orb_peak", [0, 0, 0, 1], [0, 0, 0.55f, 0], [0, 0]),
            ("post_hit_peak", [0, 0, 0.8f, 1], [0, 0, 0, 0], [0, 0]),
            ("post_surge_mid", [1, 0, 0, 1], [0, 0.666667f, 0, 0], [0, 0]),
            ("post_shield_held", [0, 0, 0, 1], [0, 0, 0, 0], [1, 0]),
            ("post_ghost_mid", [0, 0, 0, 1], [0, 0, 0, 2.5f], [0, 0]),
        ];
        {
            using var host = new HeadlessCanvasHost(256, 160, 1f, Colors.Black);
            host.Canvas.Content = new Picture { HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill, UseCache = SkiaCacheType.None };
            host.AdvanceFrames(5);
            Save(host, "input");
        }
        foreach (var (name, fx, fx2, shield) in states)
        {
            using var host = new HeadlessCanvasHost(256, 160, 1f, Colors.Black);
            var picture = new Picture { HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill, UseCache = SkiaCacheType.None };
            picture.VisualEffects.Add(new SkiaShaderEffect { ShaderCode = Shaders.POST }
                .SetUniform("uFx", fx).SetUniform("uFx2", fx2).SetUniform("uRay", 1.0f, 0.55f, 0.22f)
                .SetUniform("uOrb", 0.147993f, 0.017940f, 0.448467f).SetUniform("uShield", shield)
                .OnShaderError((me, error) => Console.WriteLine($"post SkSL: {error}")));
            host.Canvas.Content = picture;
            host.AdvanceFrames(5);
            Save(host, name);
        }

        // GLOW on an Image-cached layer over the picture, holding GAME OVER
        {
            using var host = new HeadlessCanvasHost(256, 160, 1f, Colors.Black);
            var layer = new SkiaLayer
            {
                VerticalOptions = LayoutOptions.Fill,
                UseCache = SkiaCacheType.Image,
                Children =
                {
                    new SkiaLabel("GAME OVER")
                    {
                        FontFamily = "FontScore",
                        FontSize = 38,
                        TextColor = Colors.White,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        HorizontalTextAlignment = DrawTextAlignment.Center,
                    },
                },
            };
            layer.VisualEffects.Add(new SkiaShaderEffect { ShaderCode = Shaders.GLOW }
                .SetUniform("uGlow", 1.0f).SetUniform("uWash", 0.0f).SetUniform("uTint", 1.0f, 0.16f, 0.04f)
                .OnShaderError((me, error) => Console.WriteLine($"glow SkSL: {error}")));
            host.Canvas.Content = new SkiaLayer
            {
                VerticalOptions = LayoutOptions.Fill,
                Children = { new Picture { HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill, UseCache = SkiaCacheType.None }, layer },
            };
            host.AdvanceFrames(5);
            Save(host, "glow_game_over");
        }

        // CURTAIN: snapshot of the gradient at uBurn 0 (Once), then the picture turns blue and the curtain burns
        foreach (var burn in new[] { 0.3f, 0.6f, 0.9f })
        {
            using var host = new HeadlessCanvasHost(256, 160, 1f, Colors.Black);
            var picture = new Picture { HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill, UseCache = SkiaCacheType.None };
            var uBurn = new float[] { 0 };
            var curtain = new SkiaShaderEffect { ShaderCode = Shaders.CURTAIN, UseBackground = PostRendererEffectUseBackgroud.Once }
                .SetUniform("uBurn", uBurn)
                .OnShaderError((me, error) => Console.WriteLine($"curtain SkSL: {error}"));
            var layer = new SkiaLayer { VerticalOptions = LayoutOptions.Fill, UseCache = SkiaCacheType.None };
            layer.VisualEffects.Add(curtain);
            host.Canvas.Content = new SkiaLayer { VerticalOptions = LayoutOptions.Fill, Children = { picture, layer } };
            host.AdvanceFrames(5);
            picture.Blue = true;
            picture.Update();
            curtain.SetUniform("uBurn", burn);
            host.AdvanceFrames(5);
            Save(host, "curtain_" + burn.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    static void Wrappers()
    {
        var bytes = new byte[4096];
        var info = new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        using var surface = SKSurface.Create(new SKImageInfo(32, 32));
        var pts = new SKPoint[1500];
        var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
        void Measure(string name, Action a)
        {
            for (int i = 0; i < 50; i++) a();
            long t = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) a();
            Console.WriteLine($"{name}: {(GC.GetAllocatedBytesForCurrentThread() - t) / 200} B");
        }
        Measure("FromPixels+release", () =>
        {
            using var pm = new SKPixmap(info, pin.AddrOfPinnedObject(), info.RowBytes);
            using var img = SKImage.FromPixels(pm, (_, _) => { });
        });
        Measure("FromPixels no release", () =>
        {
            using var pm = new SKPixmap(info, pin.AddrOfPinnedObject(), info.RowBytes);
            using var img = SKImage.FromPixels(pm);
        });
        Measure("SKPixmap", () => { using var pm = new SKPixmap(info, pin.AddrOfPinnedObject(), info.RowBytes); });
        var shared = new SKPixmap();
        SKImageRasterReleaseDelegate staticRelease = (_, ctx) => { };
        var ctxObj = new object();
        Measure("FromPixels reused pixmap + static delegate", () =>
        {
            shared.Reset(info, pin.AddrOfPinnedObject(), info.RowBytes);
            using var img = SKImage.FromPixels(shared, staticRelease, ctxObj);
        });
        Measure("FromPixels reused pixmap, no release", () =>
        {
            shared.Reset(info, pin.AddrOfPinnedObject(), info.RowBytes);
            using var img = SKImage.FromPixels(shared);
        });
        foreach (var m in typeof(SKRuntimeEffect).GetMethods().Where(m => m.Name.StartsWith("ToShader") || m.Name.StartsWith("ToColorFilter")))
            Console.WriteLine("  " + m);
        var fx = SKRuntimeEffect.CreateShader("uniform float4 u; uniform shader c; half4 main(float2 p){ return c.eval(p) * u; }", out _);
        var fu = new SKRuntimeEffectUniforms(fx);
        var fc = new SKRuntimeEffectChildren(fx);
        using var csh = SKShader.CreateColor(SKColors.Red);
        fc["c"] = csh;
        Measure("ToShader", () => { using var s = fx.ToShader(fu, fc); });
        var arr = new float[4];
        Measure("write float[]", () => fu["u"] = arr);
        Measure("write span", () => fu["u"] = (ReadOnlySpan<float>)stackalloc float[] { 1, 2, 3, 4 });
        Measure("Contains", () => fu.Contains("u"));
        Measure("child write", () => fc["c"] = csh);
        Measure("child reset+write", () => { fc.Reset(); fc["c"] = csh; });
        using var img0 = surface.Snapshot();
        Measure("ToRawShader", () => { using var s = img0.ToRawShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling); });
        Measure("Snapshot", () => { surface.Canvas.DrawRect(0, 0, 1, 1, new SKPaint()); using var s = surface.Snapshot(); });
        Measure("SKPaint", () => { using var p = new SKPaint(); });
        Measure("Vertices", () => { using var v = SKVertices.CreateCopy(SKVertexMode.Triangles, pts, pts, null); });
        Measure("GCHandle", () => { var h = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned); h.Free(); });
        pin.Free();
    }

    /// <summary>The POST shader on an uncached control drawn every frame, alone: what a post effect allocates per frame.</summary>
    static void EffectOnly(int frames)
    {
        using var host = new HeadlessCanvasHost(32, 24, 1.0f, Colors.Black);
        var fx = new float[] { 0.5f, 0, 0, 0 };
        var effect = new SkiaShaderEffect { ShaderCode = Shaders.POST }.SetUniform("uFx", fx).SetUniform("uFx2", 0f, 0f, 0f, 0f)
            .SetUniform("uRay", 1f, 1f, 1f).SetUniform("uOrb", 0f, 0f, 0f).SetUniform("uShield", 0f, 0f);
        var shape = new SkiaShape { HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill, BackgroundColor = Colors.Red, UseCache = SkiaCacheType.None };
        shape.VisualEffects.Add(effect);
        host.Canvas.Content = new SkiaLayer { Children = { shape } };
        host.AdvanceFrames(60);
        using var listener = new AllocationListener();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < frames; i++)
        {
            fx[3] = i * 0.016f;
            effect.SetUniform("uFx", fx);
            shape.Update();
            host.RenderFrame(16.0);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"effect: {frames} frames, {bytes / frames} B/frame");
    }

    sealed class AllocationListener : EventListener
    {
        public readonly Dictionary<string, int> Types = new();

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventName == null || !e.EventName.StartsWith("GCAllocationTick"))
                return;
            int index = e.PayloadNames.IndexOf("TypeName");
            if (index < 0)
                return;
            var type = e.Payload[index] as string ?? "?";
            lock (Types)
                Types[type] = Types.TryGetValue(type, out var n) ? n + 1 : 1;
        }
    }
}
