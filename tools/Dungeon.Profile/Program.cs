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
/// were sampled in (GCAllocationTick, one sample per ~100 KB). Usage: Dungeon.Profile [frames] [run].
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
