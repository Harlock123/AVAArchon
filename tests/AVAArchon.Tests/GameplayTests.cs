using AVAArchon.Engine;
using AVAArchon.Views;

namespace AVAArchon.Tests;

public sealed class GameplayTests
{
    private static readonly Controls Fire = new(0, 0, true);
    private static readonly Controls Back = new(0, 0, false, true);

    /// <summary>A game with people on both sides, at the start.</summary>
    private static GameEngine TwoPlayers(int seed = 7)
    {
        var g = new GameEngine(seed) { Silent = true, Players = Players.Two };
        g.StartGame();
        return g;
    }

    /// <summary>Taps a control for one step, then lets go.</summary>
    private static void Tap(GameEngine g, Controls c, bool dusk = false)
    {
        if (dusk) g.Tick(default, c); else g.Tick(c, default);
        g.Tick(default, default);
    }

    /// <summary>Walks a side's cursor to a square, a tap at a time.</summary>
    private static void CursorTo(GameEngine g, int x, int y, bool dusk = false)
    {
        for (int i = 0; i < 20 && g.Cursor != (x, y); i++)
        {
            var (cx, cy) = g.Cursor;
            Tap(g, new Controls(Math.Sign(x - cx), Math.Sign(y - cy)), dusk);
        }
        Assert.Equal((x, y), g.Cursor);
    }

    private static void Until(GameEngine g, Func<bool> done, int limit = 60 * 30)
    {
        for (int i = 0; i < limit && !done(); i++) g.Tick(default, default);
        Assert.True(done());
    }

    [Fact]
    public void The_armies_start_drawn_up_with_their_leaders_on_power_points()
    {
        var b = Board.Start();
        Assert.Equal(18, b.Living(Side.Dawn).Count());
        Assert.Equal(18, b.Living(Side.Dusk).Count());
        Assert.Equal(Kind.Lightbringer, b.At(0, 4)!.Kind);
        Assert.Equal(Kind.Shadelord, b.At(8, 4)!.Kind);
        Assert.True(Board.IsPowerPoint(0, 4) && Board.IsPowerPoint(8, 4) && Board.IsPowerPoint(4, 4));
        Assert.Equal(7, b.Living(Side.Dawn).Count(p => p.Kind == Kind.Sentinel));
        Assert.Equal(7, b.Living(Side.Dusk).Count(p => p.Kind == Kind.Ghoul));
        Assert.All(b.Pieces, p => Assert.Equal(p.Type.Life, p.Life));
    }

    [Fact]
    public void Light_strengthens_dawn_and_dark_strengthens_dusk_and_the_lumen_swings_between()
    {
        var b = Board.Start();
        Assert.Equal(Square.Light, Board.SquareAt(0, 4));
        Assert.Equal(Square.Dark, Board.SquareAt(8, 4));
        Assert.Equal(Square.Shifting, Board.SquareAt(4, 4));
        Assert.Equal(5, b.Bonus(Side.Dawn, 0, 4));
        Assert.Equal(0, b.Bonus(Side.Dusk, 0, 4));
        Assert.Equal(5, b.Bonus(Side.Dusk, 8, 4));
        b.Lumen = 1;
        b.LumenDirection = -1;
        Assert.Equal(1, b.Bonus(Side.Dawn, 4, 4));
        Assert.Equal(4, b.Bonus(Side.Dusk, 4, 4));
        b.StepLumen();
        Assert.Equal(0, b.Lumen);
        Assert.Equal(1, b.LumenDirection);
        b.StepLumen();
        Assert.Equal(1, b.Lumen);
    }

    [Fact]
    public void Walkers_go_round_pieces_while_fliers_pass_over_them()
    {
        var b = Board.Start();
        // Hemmed in at the start: the archer in the corner has nowhere to go.
        Assert.Empty(b.Moves(b.At(0, 0)!));
        var sentinel = b.At(1, 4)!;
        var moves = b.Moves(sentinel);
        Assert.Contains((4, 4), moves);
        Assert.DoesNotContain((5, 4), moves);
        var gryphon = b.At(1, 0)!;
        Assert.Contains((6, 5), b.Moves(gryphon));
        // A flier may land on an enemy, never on a friend.
        var wyvern = b.At(7, 0)!;
        Assert.Contains((2, 0), b.Moves(wyvern));
        Assert.DoesNotContain((6, 0), b.Moves(b.At(1, 0)!).Where(m => b.At(m.X, m.Y)?.Side == Side.Dawn));
    }

    [Fact]
    public void Each_player_moves_with_their_own_controls_and_the_lumen_turns_after_a_round()
    {
        var g = TwoPlayers();
        int lumen = g.Board.Lumen;
        var sentinel = g.Board.At(1, 3)!;
        CursorTo(g, 1, 3);
        Tap(g, Fire);
        Assert.Same(sentinel, g.Selected);
        CursorTo(g, 3, 3);
        Tap(g, Fire);
        Until(g, () => g.Turn == Side.Dusk);
        Assert.Equal((3, 3), (sentinel.X, sentinel.Y));
        // Dawn's controls do nothing on Dusk's turn.
        Tap(g, new Controls(-1, 0));
        Tap(g, Fire);
        Assert.Null(g.Selected);
        var ghoul = g.Board.At(7, 5)!;
        CursorTo(g, 7, 5, dusk: true);
        Tap(g, Fire, dusk: true);
        CursorTo(g, 5, 5, dusk: true);
        Tap(g, Fire, dusk: true);
        Until(g, () => g.Turn == Side.Dawn);
        Assert.Equal((5, 5), (ghoul.X, ghoul.Y));
        Assert.Equal(2, g.Round);
        Assert.NotEqual(lumen, g.Board.Lumen);
    }

    [Fact]
    public void Picking_a_piece_can_be_taken_back_and_a_blocked_piece_cannot_be_picked()
    {
        var g = TwoPlayers();
        CursorTo(g, 0, 0);
        Tap(g, Fire);
        Assert.Null(g.Selected);
        CursorTo(g, 1, 2);
        Tap(g, Fire);
        Assert.NotNull(g.Selected);
        Tap(g, Back);
        Assert.Null(g.Selected);
        Assert.Equal(Side.Dawn, g.Turn);
    }

    [Fact]
    public void Moving_onto_an_enemy_starts_a_fight_and_the_winner_holds_the_square()
    {
        var g = TwoPlayers();
        // A gryphon flies straight at a ghoul.
        var gryphon = g.Board.At(1, 0)!;
        var ghoul = g.Board.At(7, 3)!;
        gryphon.X = 5; gryphon.Y = 3;
        CursorTo(g, 5, 3);
        Tap(g, Fire);
        CursorTo(g, 7, 3);
        Tap(g, Fire);
        Until(g, () => g.Phase == Phase.Fight);
        var f = g.Fight!;
        Assert.Same(gryphon, f.Dawn.Piece);
        Assert.Same(ghoul, f.Dusk.Piece);
        Assert.Equal(gryphon.Life + g.Board.Bonus(Side.Dawn, 7, 3), f.Dawn.Life);
        // Let the computer fight for both: a person who never touches the controls.
        f.Dusk.Life = 1;
        for (int i = 0; i < 60 * 60 && g.Phase == Phase.Fight; i++)
        {
            var (fx, fy) = (Math.Sign(f.Dusk.X - f.Dawn.X), Math.Sign(f.Dusk.Y - f.Dawn.Y));
            g.Tick(new Controls(fx, Math.Abs(f.Dusk.Y - f.Dawn.Y) < 10 ? 0 : fy, true), default);
        }
        Until(g, () => g.Turn == Side.Dusk);
        Assert.False(ghoul.Alive);
        Assert.Equal((7, 3), (gryphon.X, gryphon.Y));
        Assert.Equal(0, g.QuietTurns);
    }

    [Fact]
    public void A_blow_lands_only_within_reach_and_a_solid_pillar_stops_missiles()
    {
        var rng = new Random(3);
        var lancer = new Piece { Id = 0, Type = PieceType.All[Kind.Lancer], Side = Side.Dawn, Life = 17 };
        var ogre = new Piece { Id = 1, Type = PieceType.All[Kind.Ogre], Side = Side.Dusk, Life = 27 };
        var f = new Fight(Fight.Champion(lancer, 0), Fight.Champion(ogre, 0), 3, (4, 4), rng, _ => { });
        f.Pillars.Clear();
        f.Update(Fight.Ready + 0.01, default, default);
        f.Dawn.X = 300; f.Dawn.Y = 270;
        f.Dusk.X = 300 + 2 * Fighter.Radius + 40; f.Dusk.Y = 270;
        f.Update(GameEngine.Step, new Controls(1, 0, true), default);
        for (int i = 0; i < 15; i++) f.Update(GameEngine.Step, default, default);
        Assert.Equal(27 - 10, f.Dusk.Life);
        // Too far: the lance swings at air.
        f.Dusk.X = 700;
        for (int i = 0; i < 60; i++) f.Update(GameEngine.Step, new Controls(0, 0, true), default);
        Assert.Equal(27 - 10, f.Dusk.Life);
        // A rock thrown at a solid pillar breaks on it.
        f.Pillars.Add(new Pillar { X = 500, Y = 270, Phase = Math.PI / 2, Rate = 0 });
        Assert.True(f.Pillars[0].Solid(f.Time));
        double life = f.Dawn.Life;
        for (int i = 0; i < 180; i++) f.Update(GameEngine.Step, default, new Controls(-1, 0, true));
        Assert.Equal(life, f.Dawn.Life);
        // Out from behind it, the rocks land.
        f.Pillars.Clear();
        for (int i = 0; i < 180; i++) f.Update(GameEngine.Step, default, new Controls(0, 0, true));
        Assert.True(f.Dawn.Life < life);
    }

    [Fact]
    public void A_burst_burns_everything_near_but_roots_its_maker()
    {
        var sunbird = new Piece { Id = 0, Type = PieceType.All[Kind.Sunbird], Side = Side.Dawn, Life = 22 };
        var ghoul = new Piece { Id = 1, Type = PieceType.All[Kind.Ghoul], Side = Side.Dusk, Life = 12 };
        var f = new Fight(Fight.Champion(sunbird, 0), Fight.Champion(ghoul, 0), 3, (4, 4), new Random(1), _ => { });
        f.Update(Fight.Ready + 0.01, default, default);
        f.Dawn.X = 400; f.Dawn.Y = 270;
        f.Dusk.X = 400 + 120; f.Dusk.Y = 270;
        f.Update(GameEngine.Step, new Controls(0, 0, true), default);
        double x = f.Dawn.X;
        for (int i = 0; i < 20; i++) f.Update(GameEngine.Step, new Controls(1, 0), default);
        Assert.Equal(x, f.Dawn.X);
        Assert.Equal(0, f.Dusk.Life);
        Assert.True(f.Over);
    }

    [Fact]
    public void Wounds_last_but_heal_a_little_each_turn_and_fast_on_a_power_point()
    {
        var b = Board.Start();
        var sentinel = b.At(1, 1)!;
        sentinel.Life = 4;
        var lightbringer = b.At(0, 4)!;
        lightbringer.Life = 15;
        b.EndTurn(Side.Dawn);
        Assert.Equal(5, sentinel.Life);
        Assert.Equal(15 + Board.PowerPointHealing, lightbringer.Life);
        b.EndTurn(Side.Dawn);
        Assert.Equal(lightbringer.Type.Life, lightbringer.Life);
    }

    [Fact]
    public void Healing_teleporting_and_exchanging_are_cast_from_the_leaders_spellbook()
    {
        var g = TwoPlayers();
        var stone = g.Board.At(0, 1)!;
        stone.Life = 5;
        // Fire on the leader twice opens the spellbook.
        CursorTo(g, 0, 4);
        Tap(g, Fire);
        Tap(g, Fire);
        Assert.Equal(Phase.Spellbook, g.Phase);
        while (Spells.All[g.SpellIndex] != Spell.Heal) Tap(g, new Controls(0, 1));
        Tap(g, Fire);
        Assert.Equal(Phase.Target, g.Phase);
        CursorTo(g, 0, 1);
        Tap(g, Fire);
        Until(g, () => g.Turn == Side.Dusk);
        Assert.Equal(stone.Type.Life, stone.Life);
        Assert.Contains(Spell.Heal, g.Board.DawnSpells);
        Assert.False(Spells.CanCast(g.Board, Side.Dawn, Spell.Heal));

        // Dusk teleports an ogre into the open, then (next turn) exchanges two pieces.
        var ogre = g.Board.At(8, 1)!;
        g.BeginSpellFor(Side.Dusk, Spell.Teleport);
        g.Target(8, 1);
        g.Target(5, 6);
        Until(g, () => g.Turn == Side.Dawn);
        Assert.Equal((5, 6), (ogre.X, ogre.Y));
        g.BeginSpellFor(Side.Dawn, Spell.Exchange);
        var a = g.Board.At(1, 2)!;
        g.Target(1, 2);
        g.Target(5, 6);
        Until(g, () => g.Turn == Side.Dusk);
        Assert.Equal((5, 6), (a.X, a.Y));
        Assert.Equal((1, 2), (ogre.X, ogre.Y));
    }

    [Fact]
    public void Spells_never_touch_a_piece_on_a_power_point()
    {
        var b = Board.Start();
        var targets = Spells.FirstTargets(b, Side.Dawn, Spell.Imprison);
        Assert.DoesNotContain((8, 4), targets);
        Assert.Contains((8, 3), targets);
        Assert.DoesNotContain((0, 4), Spells.FirstTargets(b, Side.Dawn, Spell.Exchange));
    }

    [Fact]
    public void An_imprisoned_piece_waits_out_its_sentence()
    {
        var g = TwoPlayers();
        var ghoul = g.Board.At(7, 2)!;
        g.BeginSpellFor(Side.Dawn, Spell.Imprison);
        g.Target(7, 2);
        Until(g, () => g.Turn == Side.Dusk);
        Assert.Equal(Spells.ImprisonTurns, ghoul.Imprisoned);
        Assert.Empty(g.Board.Moves(ghoul));
        Assert.False(g.Pick(ghoul));
        for (int turn = 0; turn < Spells.ImprisonTurns; turn++)
        {
            // Dusk moves something else, then Dawn.
            var other = g.Board.Living(g.Turn).First(p => p.Imprisoned == 0 && g.Board.Moves(p).Any(m => g.Board.At(m.X, m.Y) is null));
            var to = g.Board.Moves(other).First(m => g.Board.At(m.X, m.Y) is null);
            g.Pick(other);
            g.MoveSelected(to.X, to.Y);
            Until(g, () => g.Turn == Side.Dawn);
            other = g.Board.Living(Side.Dawn).First(p => g.Board.Moves(p).Any(m => g.Board.At(m.X, m.Y) is null));
            to = g.Board.Moves(other).First(m => g.Board.At(m.X, m.Y) is null);
            g.Pick(other);
            g.MoveSelected(to.X, to.Y);
            Until(g, () => g.Turn == Side.Dusk);
        }
        Assert.Equal(0, ghoul.Imprisoned);
        Assert.NotEmpty(g.Board.Moves(ghoul));
    }

    [Fact]
    public void Revive_brings_back_the_fallen_beside_the_leader_and_shift_time_turns_the_lumen()
    {
        var g = TwoPlayers();
        var lancer = g.Board.At(0, 2)!;
        lancer.Alive = false;
        g.Board.At(1, 5)!.X = 3; // room beside the leader
        g.BeginSpellFor(Side.Dawn, Spell.Revive);
        Assert.Equal(Phase.Revive, g.Phase);
        Assert.Contains(lancer, g.ReviveChoices);
        while (g.ReviveChoices[g.ReviveIndex] != lancer) Tap(g, new Controls(0, 1));
        Tap(g, Fire);
        Assert.Contains((1, 5), g.Lit);
        g.Target(1, 5);
        Until(g, () => g.Turn == Side.Dusk);
        Assert.True(lancer.Alive);
        Assert.Equal((1, 5), (lancer.X, lancer.Y));
        Assert.Equal(lancer.Type.Life, lancer.Life);

        g.Board.Lumen = 2;
        g.Board.LumenDirection = -1;
        g.BeginSpellFor(Side.Dusk, Spell.ShiftTime);
        Until(g, () => g.Turn == Side.Dawn);
        // Shift Time turned it toward the light, and the round's step went that way.
        Assert.Equal(1, g.Board.LumenDirection);
        Assert.Equal(3, g.Board.Lumen);
    }

    [Fact]
    public void A_summoned_elemental_fights_for_its_caller_and_then_vanishes()
    {
        var g = TwoPlayers();
        var spitter = g.Board.At(8, 0)!;
        g.BeginSpellFor(Side.Dawn, Spell.Elemental);
        g.Target(8, 0);
        Until(g, () => g.Phase == Phase.Fight);
        var f = g.Fight!;
        Assert.Equal(Kind.Elemental, f.Dawn.Type.Kind);
        Assert.True(f.Dawn.Computer);
        Assert.Same(spitter, f.Dusk.Piece);
        f.Dusk.Life = 0.5;
        f.Dawn.Life = 100;
        Until(g, () => g.Turn == Side.Dusk, 60 * 120);
        Assert.False(spitter.Alive);
        Assert.Null(g.Board.At(8, 0));
        Assert.DoesNotContain(g.Board.Pieces, p => p.Kind == Kind.Elemental);
    }

    [Fact]
    public void Holding_all_five_power_points_wins()
    {
        var g = TwoPlayers();
        var b = g.Board;
        var movers = b.Living(Side.Dawn).Where(p => !p.Type.Leader).Take(3).ToList();
        foreach (var p in b.Pieces.Where(p => p.Alive && p.Side == Side.Dusk && Board.IsPowerPoint(p.X, p.Y))) p.X = 6;
        movers[0].X = 4; movers[0].Y = 0;
        movers[1].X = 4; movers[1].Y = 8;
        movers[2].X = 4; movers[2].Y = 3;
        g.Pick(movers[2]);
        g.MoveSelected(4, 4);
        // Dusk's leader was moved off its point: Dawn flies onto the last one.
        var gryphon = b.At(1, 8)!;
        gryphon.X = 5; gryphon.Y = 6;
        Until(g, () => g.Turn == Side.Dusk);
        Assert.Equal(GameState.Playing, g.State);
        var ghoul = b.Living(Side.Dusk).First(p => b.Moves(p).Any(m => b.At(m.X, m.Y) is null && !Board.IsPowerPoint(m.X, m.Y)));
        var to = b.Moves(ghoul).First(m => b.At(m.X, m.Y) is null && !Board.IsPowerPoint(m.X, m.Y));
        g.Pick(ghoul);
        g.MoveSelected(to.X, to.Y);
        Until(g, () => g.Turn == Side.Dawn);
        g.Pick(gryphon);
        g.MoveSelected(8, 4);
        Until(g, () => g.State == GameState.Over);
        Assert.Equal(Side.Dawn, g.Winner);
        Assert.Contains("five power points", g.Verdict);
    }

    [Fact]
    public void Destroying_every_enemy_wins_and_a_long_quiet_is_a_truce()
    {
        var g = TwoPlayers();
        foreach (var p in g.Board.Living(Side.Dusk).Skip(1)) p.Alive = false;
        var last = g.Board.Living(Side.Dusk).Single();
        last.Alive = false;
        var mover = g.Board.At(1, 3)!;
        g.Pick(mover);
        g.MoveSelected(2, 3);
        Until(g, () => g.State == GameState.Over);
        Assert.Equal(Side.Dawn, g.Winner);

        var h = new GameEngine(2) { Silent = true };
        h.StartComputerGame();
        h.SetQuiet(GameEngine.QuietTurnsForDraw - 1);
        h.Board.Pieces.ForEach(p => { if (p.Side == Side.Dusk) p.Alive = p.Type.Leader; });
        for (int i = 0; i < 60 * 30 && h.State == GameState.Playing && h.Phase != Phase.Clash; i++) h.Tick(default, default);
        Assert.Equal(GameState.Over, h.State);
        Assert.Null(h.Winner);
    }

    [Fact]
    public void The_computer_takes_a_fight_it_will_win_and_shuns_one_it_will_lose()
    {
        var b = Board.Start();
        var lightbringer = b.At(0, 4)!;
        // A badly wounded ogre in reach of the Lightbringer.
        var ogre = b.At(8, 1)!;
        ogre.X = 2; ogre.Y = 5;
        ogre.Life = 3;
        // (No spells this time: just moves.)
        foreach (var s in Spells.All) b.DawnSpells.Add(s);
        var plan = new Computer(Difficulty.Hard, new Random(1)).Choose(b, Side.Dawn);
        Assert.IsType<MovePlan>(plan);
        var m = (MovePlan)plan;
        Assert.Equal((2, 5), (m.X, m.Y));

        // Now the only thing in reach is the Shadelord, fresh on its own dark square, and Dawn's piece is a weak archer.
        var c = new Board();
        var archer = c.Add(Kind.Archer, 7, 5);
        c.Add(Kind.Shadelord, 8, 5);
        c.Add(Kind.Sentinel, 0, 0);
        var plan2 = new Computer(Difficulty.Hard, new Random(1)).Choose(c, Side.Dawn);
        Assert.False(plan2 is MovePlan { X: 8, Y: 5 });
    }

    [Fact]
    public void A_hard_fighter_beats_an_easy_one_with_the_same_pieces()
    {
        int hardWins = 0;
        for (int s = 0; s < 24; s++)
        {
            var rng = new Random(s);
            var a = new Piece { Id = 0, Type = PieceType.All[Kind.Archer], Side = Side.Dawn, Life = 11 };
            var b = new Piece { Id = 1, Type = PieceType.All[Kind.Spitter], Side = Side.Dusk, Life = 14 };
            bool hardIsDawn = s % 2 == 0;
            var f = new Fight(Fight.Champion(a, 0), Fight.Champion(b, 0), 3, (4, 4), rng, _ => { });
            var dawn = new Brain(hardIsDawn ? Difficulty.Hard : Difficulty.Easy, rng);
            var dusk = new Brain(hardIsDawn ? Difficulty.Easy : Difficulty.Hard, rng);
            for (int i = 0; i < 60 * 120 && !f.Over; i++)
                f.Update(GameEngine.Step, dawn.Think(f, f.Dawn, GameEngine.Step), dusk.Think(f, f.Dusk, GameEngine.Step));
            var hard = hardIsDawn ? f.Dawn : f.Dusk;
            if (!hard.Dead) hardWins++;
        }
        Assert.True(hardWins >= 15, $"hard won {hardWins} of 24");
    }

    [Fact]
    public void Computer_generals_fight_a_whole_war_to_a_finish()
    {
        int decided = 0;
        for (int seed = 1; seed <= 6; seed++)
        {
            var g = new GameEngine(seed) { Silent = true };
            g.StartComputerGame();
            for (int i = 0; i < 60 * 60 * 30 && g.State == GameState.Playing; i++) g.Tick(default, default);
            Assert.Equal(GameState.Over, g.State);
            if (g.Winner is not null) decided++;
        }
        Assert.True(decided >= 5);
    }

    [Fact]
    public void A_hard_general_beats_an_easy_one()
    {
        int hard = 0;
        for (int seed = 1; seed <= 8; seed++)
        {
            var g = new GameEngine(seed) { Silent = true };
            bool hardDawn = seed % 2 == 0;
            g.StartComputerGame(hardDawn ? Difficulty.Hard : Difficulty.Easy, hardDawn ? Difficulty.Easy : Difficulty.Hard);
            for (int i = 0; i < 60 * 60 * 30 && g.State == GameState.Playing; i++) g.Tick(default, default);
            if (g.Winner == (hardDawn ? Side.Dawn : Side.Dusk)) hard++;
        }
        Assert.True(hard >= 6, $"hard won {hard} of 8");
    }

    [Fact]
    public void The_title_sets_up_the_game_and_idles_into_a_demonstration()
    {
        var g = new GameEngine(1) { Silent = true };
        Assert.Equal(GameState.Title, g.State);
        Assert.Equal("begin", g.TitleRows[g.TitleRow]);
        // Up to the skill row, harder; up to the side row, Dusk.
        Tap(g, new Controls(0, -1));
        Tap(g, new Controls(1, 0));
        Assert.Equal(Difficulty.Hard, g.Difficulty);
        Tap(g, new Controls(0, -1));
        Tap(g, new Controls(1, 0));
        Assert.Equal(Side.Dusk, g.HumanSide);
        g.Tick(default, default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.IsComputer(Side.Dawn));
        Assert.False(g.IsComputer(Side.Dusk));
        // The computer (Dawn) moves by itself.
        Until(g, () => g.Turn == Side.Dusk, 60 * 60);

        var idle = new GameEngine(2) { Silent = true };
        idle.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, idle.State);
        idle.Idle(60 * 20);
        Assert.True(idle.Round > 1 || idle.Phase != Phase.Board);
        idle.Tick(default, default, start: true);
        Assert.Equal(GameState.Title, idle.State);
    }

    [Fact]
    public void Every_piece_has_a_picture_and_every_sound_a_file()
    {
        foreach (var k in Enum.GetValues<Kind>())
        {
            Assert.True(Sprites.All.ContainsKey(k), k.ToString());
            Assert.Equal(16, Sprites.All[k].Length);
            Assert.All(Sprites.All[k], row => Assert.Equal(16, row.Length));
            Assert.All(Sprites.All[k], row => Assert.All(row, ch => Assert.NotEqual(0xFFFF00FFu, Sprites.Colour(ch, 0xFF808080))));
        }
        var dir = Path.Combine(AppContext.BaseDirectory, "Sounds");
        foreach (var s in Enum.GetValues<Sfx>().Where(s => s != Sfx.MusicStop))
            Assert.True(File.Exists(Path.Combine(dir, s + ".wav")), s.ToString());
    }
}
