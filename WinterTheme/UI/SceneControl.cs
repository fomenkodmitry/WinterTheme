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
    private TimeSpan? _last;
    private float _acc;
    private bool _running;

    public SceneControl(World world, OverlayWindow owner)
    {
        _world = world;
        _owner = owner;
        ClipToBounds = false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _running = true;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _running = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnFrame(TimeSpan now)
    {
        if (!_running) return;
        float dt = _last is null ? 1 / 60f : (float)(now - _last.Value).TotalSeconds;
        _last = now;
        _acc += Math.Clamp(dt, 0, 0.1f);

        // 30 fps by default (cheap), 60 fps when "smooth" is on; never more, even on ProMotion
        if (_acc >= (Settings.Current.Smooth60 ? 1 / 62f : 1 / 31f))
        {
            float step = Math.Min(_acc, 0.05f);
            _acc = 0;
            lock (_world)
            {
                _world.PollInput();
                _world.Update(step);
            }
            _owner.Tick();
            InvalidateVisual();
        }

        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
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
            lock (_world)
            {
                lock (_painter)
                    _painter.Draw(canvas, _world);
            }
            canvas.Restore();
        }
    }
}
