using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using WinterTheme.Render;
using WinterTheme.Sim;

namespace WinterTheme.UI;

/// <summary>Full-screen control: drives the simulation from the vsync callback and paints via Skia.</summary>
public sealed class SceneControl : Control, ICustomHitTest
{
    private readonly World _world;
    private readonly OverlayWindow _owner;
    private readonly Painter _painter = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _last;
    private Avalonia.Threading.DispatcherTimer? _timer;

    public SceneControl(World world, OverlayWindow owner)
    {
        _world = world;
        _owner = owner;
        ClipToBounds = false;
    }

    // A plain timer at the chosen frame rate. RequestAnimationFrame fires on every vsync (120 Hz on ProMotion)
    // and keeps the render loop busy even on frames where nothing is drawn.
    private static TimeSpan Interval => TimeSpan.FromSeconds(1.0 / Settings.Current.FrameRate);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _last = _clock.Elapsed.TotalSeconds;
        _timer = new Avalonia.Threading.DispatcherTimer(Interval, Avalonia.Threading.DispatcherPriority.Render, (_, _) => OnFrame());
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnFrame()
    {
        if (_timer == null) return;
        Diag.Frame();
        double now = _clock.Elapsed.TotalSeconds;
        float step = (float)Math.Clamp(now - _last, 0, 0.05);
        _last = now;
        if (_timer.Interval != Interval) _timer.Interval = Interval;

        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        lock (_world)
        {
            _world.PollInput();
            _world.Update(step);
        }
        Diag.Update(System.Diagnostics.Stopwatch.GetTimestamp() - t0);
        _owner.Tick();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.Custom(new DrawOp(new Rect(Bounds.Size), _world, _painter));
    }

    public bool HitTest(Point point) => true;

    private sealed class DrawOp : ICustomDrawOperation
    {
        private readonly World _world;
        private readonly Painter _painter;

        public DrawOp(Rect bounds, World world, Painter painter)
        {
            Bounds = bounds;
            _world = world;
            _painter = painter;
        }

        public Rect Bounds { get; }
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease == null) return;
            using var api = lease.Lease();
            var canvas = api.SkCanvas;
            canvas.Save();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (_world)
            {
                lock (_painter)
                    _painter.Draw(canvas, _world);
            }
            Diag.Draw(System.Diagnostics.Stopwatch.GetTimestamp() - t0);
            canvas.Restore();
        }
    }
}
