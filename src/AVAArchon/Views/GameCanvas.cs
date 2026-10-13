using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AVAArchon.Engine;

namespace AVAArchon.Views;

/// <summary>
/// The screen: a fixed 1024×768 picture (the board, or the arena) scaled to fit the window. Runs the
/// engine from a 60 Hz timer.
/// </summary>
public sealed partial class GameCanvas : Control
{
    public const double ScreenWidth = 1024;
    public const double ScreenHeight = 768;

    private readonly DispatcherTimer? _timer;
    private DateTime _last = DateTime.Now;
    private bool _padPausePrev, _padBackPrev;
    private double _clock;

    public GameEngine Engine { get; }
    public SoundManager Sound { get; }
    public InputHandler Input { get; } = new();

    public GameCanvas(bool silent = false) : this(new GameEngine(Environment.TickCount), new SoundManager(silent), startTimer: true) { }

    /// <summary>For tests: an engine of your own, and no timer (call <see cref="Tick"/>).</summary>
    public GameCanvas(GameEngine engine, SoundManager sound, bool startTimer)
    {
        Engine = engine;
        Sound = sound;
        Engine.Sound += Sound.Play;
        Focusable = true;
        ClipToBounds = true;
        if (startTimer)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) => Tick();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer?.Start();
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        Sound.Dispose();
        Input.Dispose();
    }

    /// <summary>One frame: read the controls, run the game, redraw.</summary>
    public void Tick()
    {
        var now = DateTime.Now;
        double dt = Math.Min(0.1, (now - _last).TotalSeconds);
        _last = now;
        _clock += dt;
        Input.Update();
        HandlePad();
        Engine.Update(dt, Input.Player1, Input.Player2, Input.Start);
        Cursor = Engine.State is GameState.Playing or GameState.Attract ? new Cursor(StandardCursorType.None) : null;
        InvalidateVisual();
    }

    /// <summary>For tests: moves the drawing clock (glows, blinking words) without running the game.</summary>
    public void Advance(double dt)
    {
        _clock += dt;
        InvalidateVisual();
    }

    private void HandlePad()
    {
        bool Edge(bool now, ref bool prev) { bool e = now && !prev; prev = now; return e; }
        if (Edge(Input.PadPause, ref _padPausePrev))
        {
            if (Engine.State == GameState.Playing) Engine.Pause();
            else if (Engine.State == GameState.Paused) Engine.Resume();
        }
        if (Edge(Input.PadBack, ref _padBackPrev) && Engine.State == GameState.Paused) Engine.ReturnToTitle();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Key == Key.S && ctrl)
        {
            SaveScreenshot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"AVAArchon_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"));
            return;
        }
        switch (e.Key)
        {
            case Key.M or Key.F2:
                Sound.IsMuted = !Sound.IsMuted;
                return;
            case Key.Escape:
                if (Engine.State == GameState.Playing) Engine.Pause();
                else if (Engine.State == GameState.Paused) Engine.ReturnToTitle();
                else if (Engine.State is GameState.Attract or GameState.Over) Engine.ReturnToTitle();
                else (TopLevel.GetTopLevel(this) as Window)?.Close();
                return;
            case Key.P:
                if (Engine.State == GameState.Playing) { Engine.Pause(); return; }
                break;
        }
        if (Engine.State == GameState.Paused)
        {
            Engine.Resume();
            return;
        }
        Input.OnKeyDown(e.Key);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        Input.OnKeyUp(e.Key);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e) => Focus();

    /// <summary>Saves what's on screen as a PNG.</summary>
    public void SaveScreenshot(string path)
    {
        var size = new PixelSize((int)Math.Max(1, Bounds.Width), (int)Math.Max(1, Bounds.Height));
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(this);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }

    /// <summary>How the fixed screen fits the window: scale and letterbox offsets.</summary>
    private (double Scale, double X, double Y) Fit()
    {
        double s = Math.Min(Bounds.Width / ScreenWidth, Bounds.Height / ScreenHeight);
        return (s, (Bounds.Width - ScreenWidth * s) / 2, (Bounds.Height - ScreenHeight * s) / 2);
    }

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        var (s, ox, oy) = Fit();
        if (s <= 0) return;
        using (dc.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(ox, oy)))
        using (dc.PushClip(new Rect(0, 0, ScreenWidth, ScreenHeight)))
            DrawScreen(dc);
    }
}
