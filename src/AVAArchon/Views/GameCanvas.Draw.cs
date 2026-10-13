using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAArchon.Engine;

namespace AVAArchon.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAArchon/Assets/Fonts#VT323"));

    /// <summary>The board's place on the screen, and the size of a square.</summary>
    private const double BoardX = 224, BoardY = 104, Cell = 64;
    /// <summary>The arena's place on the screen.</summary>
    private const double ArenaX = 32, ArenaY = 150;

    private const uint Ink = 0xFFECE6D6, Dim = 0xFF8C8A9C, Gold = 0xFFFFD456, Red = 0xFFFF5A4A, Ground = 0xFF0E0C16;
    private const uint LightSquare = 0xFFE4D9B4, DarkSquare = 0xFF1C2036;
    public const uint DawnColour = 0xFF48A8FF, DuskColour = 0xFFE0405E;

    public static uint SideColour(Side s) => s == Side.Dawn ? DawnColour : DuskColour;

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width);
        return p;
    }

    private static uint Alpha(uint argb, double a) => ((uint)Math.Clamp(a * 255, 0, 255) << 24) | (argb & 0xFFFFFF);

    private static uint Mix(uint a, uint b, double t)
    {
        uint C(int s) => (uint)Math.Round(((a >> s) & 0xFF) * (1 - t) + ((b >> s) & 0xFF) * t);
        return 0xFF000000 | (C(16) << 16) | (C(8) << 8) | C(0);
    }

    private static double TextWidth(string text, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brushes.White).Width;

    private static void Text(DrawingContext dc, string text, double x, double y, double size, uint colour, int align = 0)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double left = align == 0 ? x : align == 1 ? x - ft.Width / 2 : x - ft.Width;
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xA0000000)), new Point(left + 2, y + 2));
        dc.DrawText(ft, new Point(left, y));
    }

    /// <summary>Text wrapped to a width; returns where the next line would go.</summary>
    private static double Wrap(DrawingContext dc, string text, double x, double y, double width, double size, uint colour)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            string tryLine = line.Length == 0 ? word : line + " " + word;
            if (TextWidth(tryLine, size) > width && line.Length > 0)
            {
                Text(dc, line, x, y, size, colour);
                y += size * 0.95;
                line = word;
            }
            else line = tryLine;
        }
        if (line.Length > 0) { Text(dc, line, x, y, size, colour); y += size * 0.95; }
        return y;
    }

    /// <summary>Draws a piece's picture with its top-left at (x, y); white all over when <paramref name="flash"/>.</summary>
    private static void Sprite(DrawingContext dc, Kind kind, uint tint, double x, double y, double scale, bool flip = false, bool flash = false, double fade = 1)
    {
        var rows = Sprites.All[kind];
        for (int r = 0; r < 16; r++)
        {
            string row = rows[r];
            int c = 0;
            while (c < 16)
            {
                uint col = Sprites.Colour(row[c], tint);
                int start = c;
                while (c < 16 && Sprites.Colour(row[c], tint) == col) c++;
                if (col == 0) continue;
                if (flash) col = row[start] == 'k' ? 0xFF602020 : 0xFFFFFFFF;
                if (fade < 1) col = Alpha(col, fade);
                double left = flip ? x + (16 - c) * scale : x + start * scale;
                dc.FillRectangle(Brush(col), new Rect(left, y + r * scale, (c - start) * scale + 0.3, scale + 0.3));
            }
        }
    }

    private static void Box(DrawingContext dc, Rect r, uint edge = 0xFF6C6488, uint fill = 0xF0141220)
    {
        dc.FillRectangle(Brush(fill), r);
        dc.DrawRectangle(Pen(edge, 2), r.Deflate(1));
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(Ground), new Rect(0, 0, ScreenWidth, ScreenHeight));
        switch (g.State)
        {
            case GameState.Title:
                DrawTitle(dc);
                return;
            case GameState.Over:
                DrawBoardScreen(dc);
                DrawOver(dc);
                return;
        }
        if (g.Phase is Phase.Fight or Phase.Aftermath && g.Fight is not null) DrawArena(dc, g.Fight);
        else DrawBoardScreen(dc);
        if (g.State == GameState.Attract) Text(dc, "DEMONSTRATION  -  PRESS ENTER", ScreenWidth / 2, 738, 26, Blink(0.8) ? Gold : Dim, 1);
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, ScreenWidth, ScreenHeight));
            Box(dc, new Rect(312, 300, 400, 140));
            Text(dc, "PAUSED", 512, 318, 48, Gold, 1);
            Text(dc, "Any key to go on  -  Esc for the title", 512, 384, 24, Ink, 1);
        }
    }

    private bool Blink(double period) => _clock % period < period * 0.6;

    // ───────────────────────────── the board ─────────────────────────────

    private void DrawBoardScreen(DrawingContext dc)
    {
        var g = Engine;
        var b = g.Board;
        DrawHeader(dc);
        DrawSidePanel(dc, Side.Dawn, 14);
        DrawSidePanel(dc, Side.Dusk, ScreenWidth - 14 - 196);

        // The squares, and the power points on them.
        dc.FillRectangle(Brush(0xFF070610), new Rect(BoardX - 8, BoardY - 8, Cell * 9 + 16, Cell * 9 + 16));
        for (int y = 0; y < Board.Size; y++)
            for (int x = 0; x < Board.Size; x++)
            {
                var r = SquareRect(x, y);
                dc.FillRectangle(Brush(SquareColour(b, x, y)), r);
                if (Board.SquareAt(x, y) == Square.Shifting) dc.DrawRectangle(Pen(Alpha(0xFF9080C0, 0.35), 1), r.Deflate(3));
                if (Board.IsPowerPoint(x, y)) DrawPowerPoint(dc, r, b.Shade(x, y));
            }

        // What can be reached, or touched by a spell.
        uint turnColour = SideColour(g.Turn);
        if (g.State != GameState.Over)
            foreach (var (x, y) in g.Lit)
            {
                var r = SquareRect(x, y);
                bool enemy = b.At(x, y) is { } o && o.Side != g.Turn;
                dc.FillRectangle(Brush(Alpha(enemy ? Red : turnColour, 0.28 + 0.1 * Math.Sin(_clock * 6))), r);
            }

        // The fight's square, flashing as the challenge is made.
        if (g.Phase == Phase.Clash && Blink(0.25))
            dc.DrawRectangle(Pen(Red, 4), SquareRect(g.Battleground.X, g.Battleground.Y).Deflate(2));

        foreach (var p in b.Pieces)
        {
            if (!p.Alive || (p == g.Mover && g.Phase == Phase.Moving)) continue;
            DrawPiece(dc, p, p.X, p.Y);
        }
        if (g.Mover is { } m && g.Phase == Phase.Moving)
        {
            var (mx, my) = g.MoverAt;
            double lift = m.Type.Flies ? Math.Sin(Math.Clamp(g.PhaseTime / g.MoveSeconds, 0, 1) * Math.PI) * 14 : 0;
            DrawPiece(dc, m, mx, my - lift / Cell, shadow: lift);
        }

        // A spell's sparkle.
        if (g.Phase == Phase.Cast && g.SpellTarget is { Alive: true } t)
        {
            var r = SquareRect(t.X, t.Y);
            for (int i = 0; i < 10; i++)
            {
                double a = _clock * 5 + i * Math.PI / 5, rad = 18 + 14 * Math.Sin(g.PhaseTime * 6 + i);
                dc.FillRectangle(Brush(i % 2 == 0 ? Gold : 0xFFFFFFFF), new Rect(r.Center.X + Math.Cos(a) * rad - 2, r.Center.Y + Math.Sin(a) * rad - 2, 4, 4));
            }
        }

        // The cursor of the side to move.
        if (g.State is GameState.Playing or GameState.Attract or GameState.Paused && g.Phase is Phase.Board or Phase.Target)
        {
            var (cx, cy) = g.Cursor;
            var cr = SquareRect(cx, cy);
            dc.DrawRectangle(Pen(Blink(0.5) ? turnColour : 0xFFFFFFFF, 3), cr.Deflate(1.5));
        }
        if (g.Selected is { } s && g.Phase is Phase.Board or Phase.Spellbook or Phase.Target or Phase.Revive)
            dc.DrawRectangle(Pen(Gold, 3), SquareRect(s.X, s.Y).Inflate(1));

        DrawFooter(dc);
        if (g.Phase == Phase.Spellbook) DrawSpellbook(dc);
        if (g.Phase == Phase.Revive) DrawRevive(dc);
        if (g.Message is { } msg) DrawBanner(dc, msg);
    }

    private static Rect SquareRect(double x, double y) => new(BoardX + x * Cell, BoardY + y * Cell, Cell, Cell);

    private static uint SquareColour(Board b, int x, int y) => Mix(DarkSquare, LightSquare, b.Shade(x, y) / (double)Board.MaxLumen);

    private void DrawPowerPoint(DrawingContext dc, Rect r, int shade)
    {
        uint c = Mix(0xFFFFB020, 0xFFFFF0A0, 0.5 + 0.5 * Math.Sin(_clock * 3));
        if (shade > 3) c = Mix(c, 0xFF805010, 0.45);
        var mid = r.Center;
        double s = 26;
        var diamond = new StreamGeometry();
        using (var ctx = diamond.Open())
        {
            ctx.BeginFigure(new Point(mid.X, mid.Y - s), false);
            ctx.LineTo(new Point(mid.X + s, mid.Y));
            ctx.LineTo(new Point(mid.X, mid.Y + s));
            ctx.LineTo(new Point(mid.X - s, mid.Y));
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(null, Pen(c, 3), diamond);
        foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            dc.FillRectangle(Brush(c), new Rect(mid.X + dx * 25 - 3, mid.Y + dy * 25 - 3, 6, 6));
    }

    private void DrawPiece(DrawingContext dc, Piece p, double x, double y, double shadow = 0)
    {
        var r = SquareRect(x, y);
        if (shadow > 0) dc.FillRectangle(Brush(0x50000000), new Rect(r.X + 14, r.Bottom - 12 + shadow, 36, 6));
        Sprite(dc, p.Kind, SideColour(p.Side), r.X + 4, r.Y + 2, 3.5, flip: p.Side == Side.Dusk && Faces(p.Kind));
        if (p.Wounded)
        {
            double f = (double)p.Life / p.Type.Life;
            dc.FillRectangle(Brush(0xFF300808), new Rect(r.X + 8, r.Bottom - 7, 48, 4));
            dc.FillRectangle(Brush(f > 0.5 ? 0xFF60E060 : f > 0.25 ? 0xFFF0C040 : Red), new Rect(r.X + 8, r.Bottom - 7, 48 * f, 4));
        }
        if (p.Imprisoned > 0)
            for (int i = 0; i < 5; i++)
                dc.FillRectangle(Brush(0xE0B0B0C0), new Rect(r.X + 8 + i * 11, r.Y + 4, 3, Cell - 8));
    }

    /// <summary>Pictures drawn facing right, which Dusk's pieces turn to face left.</summary>
    private static bool Faces(Kind k) => k is Kind.Gryphon or Kind.Lancer or Kind.Spitter or Kind.Wyvern or Kind.Archer or Kind.Sentinel or Kind.Ogre
        or Kind.Stonewarden or Kind.Stormcaller or Kind.Lightbringer or Kind.Shadelord;

    private void DrawHeader(DrawingContext dc)
    {
        var g = Engine;
        var b = g.Board;
        Text(dc, "AVA ARCHON", 512, 8, 40, Gold, 1);
        // The lumen: six steps from night to day, and which way it's heading.
        double lx = 512 - 3 * 34, ly = 56;
        for (int i = 0; i <= Board.MaxLumen; i++)
        {
            var r = new Rect(lx + i * 34, ly, 28, 20);
            dc.FillRectangle(Brush(Mix(DarkSquare, LightSquare, i / 5.0)), r);
            if (i == b.Lumen) dc.DrawRectangle(Pen(Gold, 3), r.Inflate(2));
        }
        Text(dc, "NIGHT", lx - 10, ly - 2, 22, Dim, 2);
        Text(dc, "DAY", lx + 6 * 34 + 4, ly - 2, 22, Dim);
        Text(dc, b.LumenDirection > 0 ? "brightening" : "darkening", 512, ly + 20, 18, Dim, 1);
    }

    private void DrawSidePanel(DrawingContext dc, Side side, double x)
    {
        var g = Engine;
        var b = g.Board;
        bool toMove = g.Turn == side && g.State != GameState.Over;
        var r = new Rect(x, 104, 196, 576);
        Box(dc, r, toMove ? SideColour(side) : 0xFF3A3650);
        string who = g.State == GameState.Attract || g.IsComputer(side) ? "COMPUTER" : g.Players == Players.Two ? (side == Side.Dawn ? "PLAYER 1" : "PLAYER 2") : "YOU";
        Text(dc, side == Side.Dawn ? "DAWN" : "DUSK", x + 98, 112, 40, SideColour(side), 1);
        Text(dc, who, x + 98, 150, 22, toMove ? Gold : Dim, 1);
        if (toMove) Text(dc, Blink(0.8) ? "TO MOVE" : "", x + 98, 172, 22, Ink, 1);

        int living = b.Living(side).Count();
        int points = Board.PowerPoints.Count(pp => b.At(pp.X, pp.Y)?.Side == side);
        Text(dc, $"Pieces  {living}", x + 14, 204, 24, Ink);
        Text(dc, $"Power points  {points}/5", x + 14, 228, 24, points >= 4 ? Gold : Ink);

        Text(dc, "SPELLS", x + 14, 264, 22, Dim);
        var used = b.SpellsUsed(side);
        for (int i = 0; i < Spells.All.Length; i++)
        {
            var s = Spells.All[i];
            Text(dc, Spells.Name(s), x + 20, 288 + i * 22, 21, used.Contains(s) ? 0xFF4C4A5C : Ink);
        }

        Text(dc, "FALLEN", x + 14, 448, 22, Dim);
        int n = 0;
        foreach (var p in b.Fallen(side))
        {
            Sprite(dc, p.Kind, Mix(SideColour(side), 0xFF505060, 0.5), x + 12 + n % 6 * 29, 472 + n / 6 * 30, 1.7);
            n++;
        }
    }

    private void DrawFooter(DrawingContext dc)
    {
        var g = Engine;
        var b = g.Board;
        var r = new Rect(BoardX - 8, 696, Cell * 9 + 16, 66);
        Box(dc, r, 0xFF3A3650);
        if (g.State is GameState.Attract or GameState.Over) return;
        var (cx, cy) = g.Cursor;
        string square = Board.SquareAt(cx, cy) switch
        {
            Square.Light => "Light square",
            Square.Dark => "Dark square",
            _ => $"Shifting square, lumen {b.Lumen}",
        };
        if (Board.IsPowerPoint(cx, cy)) square += "  -  POWER POINT";
        string prompt = g.Phase switch
        {
            Phase.Board when g.Selected is null => "Choose a piece",
            Phase.Board => g.Selected!.Type.Leader && Spells.CanCastAny(b, g.Turn) ? "Choose a square, or the leader again for spells" : "Choose where it goes",
            Phase.Target when g.Casting is Spell.Teleport && g.FirstTarget is not null => "Where shall it go?",
            Phase.Target when g.Casting is Spell.Exchange && g.FirstTarget is not null => "And swap it with?",
            Phase.Target when g.Casting is Spell.Revive => "Where shall it return?",
            Phase.Target => $"{Spells.Name(g.Casting!.Value)}: choose a piece",
            _ => "",
        };
        if (g.IsComputer(g.Turn)) prompt = "Thinking...";
        string keys = g.Players == Players.Two ? (g.Turn == Side.Dawn ? "WASD  Space: choose  Shift: back" : "Arrows  Enter: choose  RShift: back") : "Move: arrows/WASD  Fire: Space/Enter  Back: Shift";
        if (b.At(cx, cy) is { } p)
        {
            Sprite(dc, p.Kind, SideColour(p.Side), r.X + 10, r.Y + 6, 3.4);
            Text(dc, $"{p.Name}", r.X + 74, r.Y + 6, 28, SideColour(p.Side));
            string move = p.Type.Flies ? $"flies {p.Type.Move}" : $"walks {p.Type.Move}";
            Text(dc, $"Life {p.Life}/{p.Type.Life}  {move}  {p.Type.Weapon}", r.X + 74, r.Y + 34, 21, Ink);
            Text(dc, square, r.Right - 10, r.Y + 8, 20, Dim, 2);
            Text(dc, prompt, r.Right - 10, r.Y + 34, 21, Gold, 2);
        }
        else
        {
            Text(dc, square, r.X + 12, r.Y + 6, 22, Dim);
            Text(dc, prompt, r.X + 12, r.Y + 32, 24, Gold);
            if (!g.IsComputer(g.Turn)) Text(dc, keys, r.Right - 10, r.Y + 36, 18, Dim, 2);
        }
    }

    private static void DrawBanner(DrawingContext dc, string text)
    {
        double w = TextWidth(text, 32) + 48;
        var r = new Rect(512 - w / 2, 364, w, 48);
        Box(dc, r, Gold, 0xE8100E1A);
        Text(dc, text, 512, 371, 32, Ink, 1);
    }

    private void DrawSpellbook(DrawingContext dc)
    {
        var g = Engine;
        var r = new Rect(262, 150, 500, 420);
        Box(dc, r, Gold, 0xF4120F1E);
        Text(dc, $"THE {g.Board.Leader(g.Turn)?.Name.ToUpperInvariant()}'S SPELLS", r.Center.X, r.Y + 10, 30, Gold, 1);
        for (int i = 0; i < Spells.All.Length; i++)
        {
            var s = Spells.All[i];
            bool can = Spells.CanCast(g.Board, g.Turn, s);
            bool used = g.Board.SpellsUsed(g.Turn).Contains(s);
            double y = r.Y + 54 + i * 34;
            if (i == g.SpellIndex) dc.FillRectangle(Brush(0xFF3A3060), new Rect(r.X + 12, y - 2, r.Width - 24, 32));
            Text(dc, Spells.Name(s), r.X + 26, y, 28, can ? Ink : 0xFF5A566C);
            if (used) Text(dc, "cast", r.Right - 24, y + 4, 22, 0xFF5A566C, 2);
            else if (!can) Text(dc, "nothing to cast on", r.Right - 24, y + 4, 20, 0xFF5A566C, 2);
        }
        Wrap(dc, Spells.About(Spells.All[g.SpellIndex]), r.X + 24, r.Bottom - 70, r.Width - 48, 22, Dim);
        Text(dc, "Fire: cast   Back: close", r.Center.X, r.Bottom - 26, 18, Dim, 1);
    }

    private void DrawRevive(DrawingContext dc)
    {
        var g = Engine;
        var r = new Rect(312, 150, 400, 80 + g.ReviveChoices.Count * 40);
        Box(dc, r, Gold, 0xF4120F1E);
        Text(dc, "BRING BACK WHICH?", r.Center.X, r.Y + 10, 30, Gold, 1);
        for (int i = 0; i < g.ReviveChoices.Count; i++)
        {
            var p = g.ReviveChoices[i];
            double y = r.Y + 50 + i * 40;
            if (i == g.ReviveIndex) dc.FillRectangle(Brush(0xFF3A3060), new Rect(r.X + 12, y - 2, r.Width - 24, 38));
            Sprite(dc, p.Kind, SideColour(p.Side), r.X + 24, y, 2.2);
            Text(dc, p.Name, r.X + 70, y + 4, 28, Ink);
        }
    }

    // ───────────────────────────── the arena ─────────────────────────────

    private void DrawArena(DrawingContext dc, Fight f)
    {
        var g = Engine;
        var area = new Rect(ArenaX, ArenaY, Fight.Width, Fight.Height);
        uint floor = Mix(0xFF141828, 0xFFB8AC88, f.Shade / (double)Board.MaxLumen);
        dc.FillRectangle(Brush(floor), area);
        // A faint tiled floor.
        uint seam = Mix(floor, 0xFF000000, 0.12);
        for (double x = 0; x < Fight.Width; x += 60) dc.FillRectangle(Brush(seam), new Rect(ArenaX + x, ArenaY, 2, Fight.Height));
        for (double y = 0; y < Fight.Height; y += 60) dc.FillRectangle(Brush(seam), new Rect(ArenaX, ArenaY + y, Fight.Width, 2));
        dc.DrawRectangle(Pen(0xFF6C6488, 4), area.Inflate(2));

        using (dc.PushClip(area))
        using (dc.PushTransform(Matrix.CreateTranslation(ArenaX, ArenaY)))
        {
            foreach (var p in f.Pillars)
            {
                double breath = p.Breath(f.Time);
                bool solid = p.Solid(f.Time);
                double a = solid ? 1 : Math.Clamp((breath + 1) / 0.45, 0.12, 0.5);
                dc.DrawEllipse(Brush(Alpha(0xFF58507A, a)), Pen(Alpha(0xFF2A2440, a), 3), new Point(p.X, p.Y), p.Radius, p.Radius);
                dc.DrawEllipse(Brush(Alpha(0xFF8A80B0, a)), null, new Point(p.X - 6, p.Y - 7), p.Radius * 0.45, p.Radius * 0.45);
            }
            foreach (var fighter in new[] { f.Dawn, f.Dusk }) DrawFighter(dc, f, fighter);
            foreach (var m in f.Missiles) DrawMissile(dc, m);
        }

        // Life bars, names and the squares' strength.
        DrawLifeBar(dc, f.Dawn, 32, true);
        DrawLifeBar(dc, f.Dusk, ScreenWidth - 32, false);
        Text(dc, "VS", 512, 88, 40, Gold, 1);
        if (f.Time < Fight.Ready)
        {
            Text(dc, $"{f.Dawn.Type.Name.ToUpperInvariant()}  AGAINST  {f.Dusk.Type.Name.ToUpperInvariant()}", 512, 380, 40, Ink, 1);
            Text(dc, Blink(0.3) ? "READY" : "", 512, 430, 34, Gold, 1);
        }
        if (g.Phase == Phase.Aftermath) DrawBanner(dc, g.Outcome);
        string help = g.Players == Players.Two ? "Dawn: WASD + Space    Dusk: arrows + Enter" : "Move and aim: arrows or WASD    Fire: Space, Ctrl or Enter";
        if (g.State != GameState.Attract) Text(dc, help, 512, 706, 22, Dim, 1);
    }

    private static void DrawLifeBar(DrawingContext dc, Fighter f, double x, bool left)
    {
        double w = 380;
        double bx = left ? x : x - w;
        uint colour = f.Type.Kind == Kind.Elemental ? 0xFFFF8A20 : SideColour(f.Side);
        string name = (f.Type.Kind == Kind.Elemental ? "ELEMENTAL" : f.Type.Name.ToUpperInvariant()) + (f.Bonus > 0 ? $"  +{f.Bonus}" : "");
        Text(dc, name, left ? bx : bx + w, 76, 34, colour, left ? 0 : 2);
        dc.FillRectangle(Brush(0xFF2A0A10), new Rect(bx, 114, w, 22));
        double frac = Math.Clamp(f.Life / f.MaxLife, 0, 1);
        dc.FillRectangle(Brush(colour), new Rect(left ? bx : bx + w * (1 - frac), 114, w * frac, 22));
        dc.DrawRectangle(Pen(0xFF6C6488, 2), new Rect(bx, 114, w, 22));
    }

    private void DrawFighter(DrawingContext dc, Fight f, Fighter fighter)
    {
        if (fighter.Dead && Engine.Phase == Phase.Aftermath)
        {
            // Fading where it fell.
            double fade = Math.Max(0, 1 - Engine.PhaseTime);
            Sprite(dc, fighter.Type.Kind, SideColour(fighter.Side), fighter.X - 40, fighter.Y - 40, 5, fighter.Fx < 0 && Faces(fighter.Type.Kind), false, fade);
            return;
        }
        uint tint = fighter.Type.Kind == Kind.Elemental ? 0xFFFF8A20 : SideColour(fighter.Side);
        var t = fighter.Type;
        // A burst all around.
        if (t.Attack == Attack.Aura && fighter.Striking > 0)
        {
            double k = 1 - fighter.Striking / 0.7;
            double rad = Fighter.Radius + t.Reach * Math.Min(1, k * 1.6);
            uint c = t.Kind == Kind.Wailer ? 0xFFC080FF : 0xFFFF9030;
            dc.DrawEllipse(Brush(Alpha(c, 0.25 * (1 - k))), Pen(Alpha(c, 0.9 * (1 - k * 0.6)), 6), new Point(fighter.X, fighter.Y), rad, rad);
        }
        double bob = t.Flies ? Math.Sin(f.Time * 6 + (int)fighter.Side) * 3 : 0;
        if (t.Flies) dc.DrawEllipse(Brush(0x40000000), null, new Point(fighter.X, fighter.Y + 38), 24, 7);
        Sprite(dc, t.Kind, tint, fighter.X - 40, fighter.Y - 40 + bob, 5, fighter.Fx < 0 && Faces(t.Kind) || fighter.Fx < 0 && t.Kind is Kind.Elemental, fighter.Hurt > 0);
        // A blow: a slash where it lands.
        if (t.Attack == Attack.Melee && fighter.Striking > 0)
        {
            var (zx, zy, zr) = Fight.StrikeZone(fighter);
            double k = fighter.Striking / 0.22;
            dc.DrawEllipse(null, Pen(Alpha(0xFFFFFFFF, 0.9 * k), 4), new Point(zx, zy), zr * (1.1 - k * 0.4), zr * (1.1 - k * 0.4));
        }
    }

    private void DrawMissile(DrawingContext dc, Missile m)
    {
        var k = m.Owner.Type.Kind;
        var at = new Point(m.X, m.Y);
        double len = Math.Sqrt(m.Vx * m.Vx + m.Vy * m.Vy), ux = m.Vx / len, uy = m.Vy / len;
        switch (k)
        {
            case Kind.Archer:
                dc.DrawLine(Pen(0xFFE8D8A8, 3), new Point(m.X - ux * 18, m.Y - uy * 18), at);
                dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(m.X - 3, m.Y - 3, 6, 6));
                break;
            case Kind.Stonewarden or Kind.Ogre:
                dc.DrawEllipse(Brush(k == Kind.Ogre ? 0xFF7A6A5A : 0xFF9A9AA8), Pen(0xFF302A28, 2), at, m.Size, m.Size);
                break;
            case Kind.Stormcaller:
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(m.X - ux * 30, m.Y - uy * 30), false);
                    for (int i = 1; i <= 4; i++)
                    {
                        double s = i % 2 == 0 ? 7 : -7;
                        ctx.LineTo(new Point(m.X - ux * (30 - i * 7.5) - uy * s, m.Y - uy * (30 - i * 7.5) + ux * s));
                    }
                    ctx.EndFigure(false);
                }
                dc.DrawGeometry(null, Pen(0xFFFFF080, 4), geo);
                dc.DrawGeometry(null, Pen(0xFFFFFFFF, 1.5), geo);
                break;
            }
            case Kind.Spitter:
                dc.DrawEllipse(Brush(0xFF90F040), Pen(0xFF406020, 2), at, m.Size, m.Size * 0.8);
                break;
            case Kind.Hydra or Kind.Elemental:
                dc.DrawEllipse(Brush(0xFFFF8A20), null, at, m.Size + 2, m.Size + 2);
                dc.DrawEllipse(Brush(0xFFFFE080), null, at, m.Size * 0.5, m.Size * 0.5);
                break;
            default:
            {
                uint c = SideColour(m.Owner.Side);
                dc.DrawEllipse(Brush(Alpha(c, 0.4)), null, at, m.Size + 5, m.Size + 5);
                dc.DrawEllipse(Brush(Sprites.Shade(c, 1.6)), null, at, m.Size, m.Size);
                break;
            }
        }
    }

    // ───────────────────────────── title and end ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        var g = Engine;
        // Day on the left, night on the right.
        for (int i = 0; i < 32; i++)
            dc.FillRectangle(Brush(Mix(0xFF4A5A88, 0xFF140A1A, i / 31.0)), new Rect(i * 32, 0, 33, ScreenHeight));
        Text(dc, "AVA ARCHON", 512, 40, 110, Gold, 1);
        Text(dc, "Dawn against Dusk, on the board and in the arena", 512, 156, 30, Ink, 1);

        Kind[] dawn = [Kind.Lightbringer, Kind.Sentinel, Kind.Archer, Kind.Gryphon, Kind.Stonewarden, Kind.Lancer, Kind.Stormcaller, Kind.Sunbird];
        Kind[] dusk = [Kind.Shadelord, Kind.Ghoul, Kind.Spitter, Kind.Wyvern, Kind.Ogre, Kind.Wraith, Kind.Hydra, Kind.Wailer];
        for (int i = 0; i < 8; i++)
        {
            double bob = Math.Sin(_clock * 2 + i) * 3;
            Sprite(dc, dawn[i], DawnColour, 72 + i * 112, 208 + bob, 4);
            Sprite(dc, dusk[i], DuskColour, 72 + i * 112, 296 - bob, 4, flip: Faces(dusk[i]));
        }

        var rows = g.TitleRows;
        var box = new Rect(312, 396, 400, 56 + rows.Count * 42);
        Box(dc, box);
        for (int i = 0; i < rows.Count; i++)
        {
            double y = box.Y + 18 + i * 42;
            bool on = i == g.TitleRow;
            if (on) dc.FillRectangle(Brush(0xFF3A3060), new Rect(box.X + 10, y - 3, box.Width - 20, 38));
            string label = rows[i] switch
            {
                "players" => g.Players == Players.One ? "One player" : "Two players",
                "side" => g.HumanSide == Side.Dawn ? "Lead Dawn (moves first)" : "Lead Dusk",
                "skill" => $"Computer: {g.Difficulty}",
                _ => "Begin the war",
            };
            uint col = rows[i] == "begin" ? Gold : rows[i] == "side" ? SideColour(g.HumanSide) : Ink;
            Text(dc, (on && rows[i] != "begin" ? "<  " : "") + label + (on && rows[i] != "begin" ? "  >" : ""), box.Center.X, y, 32, col, 1);
        }
        Text(dc, "Up/Down choose   Left/Right change   Enter begins", 512, box.Bottom + 14, 22, Dim, 1);
        Text(dc, "Hold all five power points, or destroy the other side, to win.", 512, 680, 24, Ink, 1);
        Text(dc, "Player 1: WASD + Space    Player 2: arrows + Enter    M mutes    Esc pauses", 512, 712, 20, Dim, 1);
    }

    private void DrawOver(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0x80000000), new Rect(0, 0, ScreenWidth, ScreenHeight));
        var r = new Rect(232, 250, 560, 240);
        uint colour = g.Winner is { } w ? SideColour(w) : Gold;
        Box(dc, r, colour, 0xF4100E1A);
        string title = g.Winner switch { Side.Dawn => "DAWN TRIUMPHS", Side.Dusk => "DUSK TRIUMPHS", _ => "A TRUCE" };
        Text(dc, title, 512, r.Y + 16, 64, colour, 1);
        Wrap(dc, g.Verdict + ".", r.X + 30, r.Y + 96, r.Width - 60, 26, Ink);
        int dawn = g.Board.Living(Side.Dawn).Count(), dusk = g.Board.Living(Side.Dusk).Count();
        Text(dc, $"After {g.Round} rounds.   Dawn has {dawn} {(dawn == 1 ? "piece" : "pieces")} left, Dusk {dusk}.", 512, r.Y + 160, 22, Dim, 1);
        if (g.StateTime > 3) Text(dc, Blink(0.8) ? "Press fire for the title" : "", 512, r.Bottom - 36, 22, Gold, 1);
    }
}
