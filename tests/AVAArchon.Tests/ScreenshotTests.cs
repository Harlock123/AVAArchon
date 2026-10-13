using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAArchon.Engine;
using AVAArchon.Views;
using Controls = AVAArchon.Engine.Controls;

namespace AVAArchon.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set ARCHON_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.Advance(0.016);
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("ARCHON_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAArchon-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void Ticks(GameEngine g, int n)
    {
        for (int i = 0; i < n; i++) g.Tick(default, default);
    }

    /// <summary>Sends a piece at an enemy (placing it within reach first), and runs to the arena.</summary>
    private static Fight Duel(GameEngine g, Piece attacker, Piece defender, int fromX, int fromY)
    {
        attacker.X = fromX;
        attacker.Y = fromY;
        g.Pick(attacker);
        g.MoveSelected(defender.X, defender.Y);
        for (int i = 0; i < 600 && g.Phase != Phase.Fight; i++) g.Tick(default, default);
        Assert.Equal(Phase.Fight, g.Phase);
        return g.Fight!;
    }

    /// <summary>Lets two computer brains fight it out until the moment is right.</summary>
    private static void Brawl(GameEngine g, Fight f, Func<Fight, bool> moment, int seed)
    {
        var rng = new Random(seed);
        var a = new Brain(Difficulty.Hard, rng);
        var b = new Brain(Difficulty.Normal, rng);
        for (int i = 0; i < 60 * 30 && g.Phase == Phase.Fight; i++)
        {
            g.Tick(a.Think(f, f.Dawn, GameEngine.Step), b.Think(f, f.Dusk, GameEngine.Step));
            if (f.Time > Fight.Ready + 1 && moment(f)) return;
        }
    }

    [AvaloniaFact]
    public void The_title()
    {
        var (w, c, g) = Open();
        g.Tick(default, default);
        c.Advance(1.3);
        Shot(w, c, "title");
    }

    [AvaloniaFact]
    public void The_board()
    {
        var (w, c, g) = Open(5);
        g.Players = Players.Two;
        g.StartGame();
        Ticks(g, 200);
        // A gryphon picked up: where it can fly lit.
        g.Pick(g.Board.At(1, 8)!);
        g.Cursors[0] = (5, 6);
        Shot(w, c, "board");

        // The spellbook.
        var (w2, c2, g2) = Open(6);
        g2.Players = Players.Two;
        g2.StartGame();
        Ticks(g2, 200);
        g2.Board.At(0, 2)!.Alive = false;
        g2.BeginSpellFor(Side.Dawn, Spell.Heal);
        g2.Tick(default, new Controls(0, 0, false, false));
        // (Back out of the target step to show the book itself.)
        g2.Tick(new Controls(0, 0, false, true), default);
        g2.Tick(default, default);
        Shot(w2, c2, "spellbook");
    }

    [AvaloniaFact]
    public void A_war_in_progress()
    {
        var (w, c, g) = Open(9);
        g.StartComputerGame(Difficulty.Normal, Difficulty.Normal);
        // Play on until the war is well under way, then stop on a quiet board.
        for (int i = 0; i < 60 * 60 * 4 && g.State == GameState.Playing && g.Round < 14; i++) g.Tick(default, default);
        for (int i = 0; i < 60 * 30 && g.Phase != Phase.Board; i++) g.Tick(default, default);
        Ticks(g, 20);
        g.Cursors[(int)g.Turn] = (4, 4);
        Shot(w, c, "war");
        // On to the end.
        for (int i = 0; i < 60 * 60 * 30 && g.State == GameState.Playing; i++) g.Tick(default, default);
        Assert.Equal(GameState.Over, g.State);
        Ticks(g, 60 * 4);
        Shot(w, c, "victory");
    }

    [AvaloniaFact]
    public void Fights_in_the_arena()
    {
        var (w, c, g) = Open(21);
        g.Players = Players.Two;
        g.StartGame();
        var b = g.Board;
        // The challenge.
        var lancer = b.At(0, 2)!;
        var ogre = b.At(8, 1)!;
        lancer.X = 3; lancer.Y = 1;
        ogre.X = 6;
        g.Pick(lancer);
        g.MoveSelected(6, 1);
        for (int i = 0; i < 600 && g.Phase != Phase.Clash; i++) g.Tick(default, default);
        Ticks(g, 30);
        Shot(w, c, "challenge");
        for (int i = 0; i < 600 && g.Phase != Phase.Fight; i++) g.Tick(default, default);
        var f = g.Fight!;
        Brawl(g, f, x => x.Missiles.Count > 0 && Fight.Distance(x.Dawn, x.Dusk) > 180 && x.Dawn.Hurt <= 0, 3);
        Shot(w, c, "arena");

        // Lightning against a hydra's fire.
        var (w2, c2, g2) = Open(22);
        g2.Players = Players.Two;
        g2.StartGame();
        var f2 = Duel(g2, g2.Board.At(0, 3)!, g2.Board.At(8, 5)!, 6, 5);
        Brawl(g2, f2, x => x.Missiles.Any(m => m.Owner == x.Dawn && Math.Abs(m.X - x.Dawn.X) > 120) && x.Missiles.Any(m => m.Owner == x.Dusk), 5);
        Shot(w2, c2, "lightning");

        // A sunbird's burst.
        var (w3, c3, g3) = Open(23);
        g3.Players = Players.Two;
        g3.StartGame();
        var f3 = Duel(g3, g3.Board.At(0, 5)!, g3.Board.At(7, 6)!, 5, 6);
        Brawl(g3, f3, x => x.Dawn.Striking is > 0.18 and < 0.3, 7);
        f3.Dawn.Hurt = f3.Dusk.Hurt = 0;
        Shot(w3, c3, "burst");
    }
}
