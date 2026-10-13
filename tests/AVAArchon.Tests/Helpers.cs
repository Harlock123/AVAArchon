using AVAArchon.Engine;

namespace AVAArchon.Tests;

internal static class Ext
{
    public static void Idle(this GameEngine g, int ticks)
    {
        for (int i = 0; i < ticks; i++) g.Tick(default, default);
    }

    /// <summary>Fights a whole arena battle between two computer brains; returns the fight.</summary>
    public static Fight Brawl(Kind dawn, Kind dusk, int seed, Difficulty skill = Difficulty.Normal, int dawnBonus = 0, int duskBonus = 0)
    {
        var rng = new Random(seed);
        // (A summoned elemental fights for whichever side called it.)
        var a = new Piece { Id = 0, Type = PieceType.All[dawn], Side = Side.Dawn, Life = PieceType.All[dawn].Life };
        var b = new Piece { Id = 1, Type = PieceType.All[dusk], Side = Side.Dusk, Life = PieceType.All[dusk].Life };
        var f = new Fight(Fight.Champion(a, dawnBonus), Fight.Champion(b, duskBonus), 3, (4, 4), rng, _ => { });
        var ba = new Brain(skill, rng);
        var bb = new Brain(skill, rng);
        for (int i = 0; i < 60 * 120 && !f.Over; i++)
            f.Update(GameEngine.Step, ba.Think(f, f.Dawn, GameEngine.Step), bb.Think(f, f.Dusk, GameEngine.Step));
        return f;
    }
}
