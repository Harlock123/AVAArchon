namespace AVAArchon.Engine;

public enum GameState { Title, Attract, Playing, Paused, Over }

/// <summary>Where a turn is.</summary>
public enum Phase
{
    /// <summary>Choosing a piece and where it goes.</summary>
    Board,
    /// <summary>A piece on its way across the board.</summary>
    Moving,
    /// <summary>The leader's book of spells is open.</summary>
    Spellbook,
    /// <summary>Choosing a fallen piece to bring back.</summary>
    Revive,
    /// <summary>Choosing whom (or where) a spell is for.</summary>
    Target,
    /// <summary>A spell taking effect.</summary>
    Cast,
    /// <summary>Two pieces meet: the challenge, before the arena.</summary>
    Clash,
    /// <summary>The fight in the arena.</summary>
    Fight,
    /// <summary>The fight's over: who won, before the board comes back.</summary>
    Aftermath,
}

public enum Players { One, Two }

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Cursor, Select, Cancel, Buzz, Step, Fly, Clash, Swing, Arrow, Bolt, Boulder, Lightning, Acid, Fire, Flame, Wail,
    Hit, Death, Spell, PowerPoint, Lumen, Menu,
    TitleTune, Victory, MusicStop,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and both players'
/// controls; it runs in fixed 60 Hz steps, so it plays the same at any frame rate and a seed always
/// plays the same game.
/// </summary>
public sealed partial class GameEngine
{
    public const double Step = 1.0 / 60;
    public const double TitleIdleSeconds = 20;
    public const double AttractSeconds = 150;
    /// <summary>A game with this many turns in a row and no fight is a draw.</summary>
    public const int QuietTurnsForDraw = 150;

    private Random _rng;
    private double _accumulator;
    private bool _startPrev;
    private bool _titleTunePending = true;
    private readonly Pad[] _pads = [new(), new(), new()];

    public GameEngine(int seed = 1983)
    {
        _rng = new Random(seed);
        Board = Board.Start();
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public double StateTime { get; private set; }
    public bool Silent { get; set; }

    // The title's choices.
    public Players Players { get; set; } = Players.One;
    public Side HumanSide { get; set; } = Side.Dawn;
    public Difficulty Difficulty { get; set; } = Difficulty.Normal;
    /// <summary>The highlighted row of the title's menu.</summary>
    public int TitleRow { get; private set; } = 3;

    public Board Board { get; private set; }
    public Side Turn { get; private set; } = Side.Dawn;
    /// <summary>Full rounds played (Dawn and Dusk each moving once).</summary>
    public int Round { get; private set; } = 1;
    public int QuietTurns { get; private set; }
    public Phase Phase { get; private set; } = Phase.Board;
    public double PhaseTime { get; private set; }
    public Side? Winner { get; private set; }
    public string Verdict { get; private set; } = "";

    /// <summary>A line across the screen, and how long it stays.</summary>
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private Computer? _dawnAi, _duskAi;
    public bool IsComputer(Side side) => (side == Side.Dawn ? _dawnAi : _duskAi) is not null;
    private Computer? Ai(Side side) => side == Side.Dawn ? _dawnAi : _duskAi;

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, double seconds = 2.5)
    {
        Message = text;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls p1, Controls p2, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Step)
        {
            _accumulator -= Step;
            Tick(p1, p2, start);
        }
    }

    /// <summary>One player's controls, with presses picked out and the stick repeating when held.</summary>
    private sealed class Pad
    {
        public Controls Now;
        private Controls _prev;
        private double _held;
        public bool FireEdge => Now.Fire && !_prev.Fire;
        public bool AltEdge => Now.Alt && !_prev.Alt;
        public (int X, int Y) Step { get; private set; }

        public void Feed(Controls c, double dt)
        {
            _prev = Now;
            Now = c;
            Step = (0, 0);
            if (c.Dx == 0 && c.Dy == 0) { _held = 0; return; }
            if (c.Dx != _prev.Dx || c.Dy != _prev.Dy) { _held = 0; Step = (c.Dx, c.Dy); return; }
            double before = _held;
            _held += dt;
            const double delay = 0.32, every = 0.11;
            if (_held >= delay && Math.Floor((_held - delay) / every) != Math.Floor((before - delay) / every)) Step = (c.Dx, c.Dy);
        }
    }

    private static Controls Merge(Controls a, Controls b) =>
        new(a.Dx != 0 ? a.Dx : b.Dx, a.Dy != 0 ? a.Dy : b.Dy, a.Fire || b.Fire, a.Alt || b.Alt);

    /// <summary>The controls that work a side: in a two-player game each has their own; alone, either set will do.</summary>
    private Pad PadFor(Side side) => Players == Players.Two && State == GameState.Playing ? _pads[side == Side.Dawn ? 0 : 1] : _pads[2];
    private Pad AnyPad => _pads[2];

    public void Tick(Controls p1, Controls p2, bool start = false)
    {
        double dt = Step;
        StateTime += dt;
        PhaseTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        _pads[0].Feed(p1, dt);
        _pads[1].Feed(p2, dt);
        _pads[2].Feed(Merge(p1, p2), dt);
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }

        switch (State)
        {
            case GameState.Title:
                if (_titleTunePending) { _titleTunePending = false; Play(Sfx.TitleTune); }
                TitleMenu(startPressed);
                if (State == GameState.Title && StateTime > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                if (startPressed || AnyPad.FireEdge) { ReturnToTitle(); break; }
                RunTurn(dt);
                if (StateTime > AttractSeconds) ReturnToTitle();
                break;
            case GameState.Playing:
                RunTurn(dt);
                break;
            case GameState.Over:
                if (StateTime > 3 && (startPressed || AnyPad.FireEdge || StateTime > 30)) ReturnToTitle();
                break;
        }
    }

    // ───────────────────────────── the title ─────────────────────────────

    /// <summary>The rows of the title's menu that apply now (side and skill only matter against the computer).</summary>
    public IReadOnlyList<string> TitleRows => Players == Players.One ? ["players", "side", "skill", "begin"] : ["players", "begin"];

    private void TitleMenu(bool start)
    {
        var pad = AnyPad;
        var rows = TitleRows;
        TitleRow = Math.Min(TitleRow, rows.Count - 1);
        var (sx, sy) = pad.Step;
        if (sy != 0) { TitleRow = (TitleRow + sy + rows.Count) % rows.Count; Play(Sfx.Menu); StateTime = 0; }
        if (sx != 0)
        {
            switch (rows[TitleRow])
            {
                case "players": Players = Players == Players.One ? Players.Two : Players.One; break;
                case "side": HumanSide = Board.Other(HumanSide); break;
                case "skill": Difficulty = (Difficulty)(((int)Difficulty + sx + 3) % 3); break;
            }
            Play(Sfx.Menu);
            StateTime = 0;
        }
        if (start || (pad.FireEdge && rows[TitleRow] == "begin")) StartGame();
        else if (pad.FireEdge) { TitleRow = rows.Count - 1; Play(Sfx.Menu); StateTime = 0; }
    }

    public void StartGame()
    {
        NewBoard();
        _dawnAi = Players == Players.One && HumanSide == Side.Dusk ? new Computer(Difficulty, _rng) : null;
        _duskAi = Players == Players.One && HumanSide == Side.Dawn ? new Computer(Difficulty, _rng) : null;
        State = GameState.Playing;
        StateTime = 0;
        Play(Sfx.MusicStop);
        Say(Players == Players.Two ? "Dawn moves first" : HumanSide == Side.Dawn ? "You lead Dawn: you move first" : "You lead Dusk: Dawn moves first", 3);
    }

    /// <summary>For tests: a game played to the end by the computer on both sides.</summary>
    internal void StartComputerGame(Difficulty dawn = Difficulty.Normal, Difficulty dusk = Difficulty.Normal)
    {
        NewBoard();
        _dawnAi = new Computer(dawn, _rng);
        _duskAi = new Computer(dusk, _rng);
        State = GameState.Playing;
        StateTime = 0;
    }

    public void StartAttract()
    {
        NewBoard();
        _dawnAi = new Computer(Difficulty.Normal, _rng);
        _duskAi = new Computer(Difficulty.Normal, _rng);
        State = GameState.Attract;
        StateTime = 0;
        Play(Sfx.MusicStop);
        Say("Dawn and Dusk, at war", 3);
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        TitleRow = TitleRows.Count - 1;
        _titleTunePending = true;
        Message = null;
        Play(Sfx.MusicStop);
    }

    public void Pause()
    {
        if (State == GameState.Playing) State = GameState.Paused;
    }

    public void Resume()
    {
        if (State == GameState.Paused) State = GameState.Playing;
    }

    private void NewBoard()
    {
        Board = Board.Start();
        Turn = Side.Dawn;
        Round = 1;
        QuietTurns = 0;
        Winner = null;
        Verdict = "";
        Cursors[0] = (1, 4);
        Cursors[1] = (7, 4);
        ToBoard();
    }

    private void GameOver(Side? winner, string verdict)
    {
        Winner = winner;
        Verdict = verdict;
        if (State == GameState.Attract)
        {
            // The demonstration ends with its verdict, then the title.
            State = GameState.Over;
            StateTime = 20;
        }
        else
        {
            State = GameState.Over;
            StateTime = 0;
        }
        Play(Sfx.Victory);
    }
}
