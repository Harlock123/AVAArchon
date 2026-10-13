using AVAArchon.Engine;

namespace AVAArchon.Tests;

/// <summary>
/// The computer judges fights by a table of how often each piece beats each other one. The table is
/// measured here, by brawls between computer fighters; set ARCHON_ODDS=1 to measure it afresh and
/// rewrite src/AVAArchon/Engine/OddsTable.cs (after changing any piece's numbers).
/// </summary>
public sealed class OddsTableTests
{
    private static readonly Kind[] Dawn = [Kind.Lightbringer, Kind.Sentinel, Kind.Archer, Kind.Gryphon, Kind.Stonewarden, Kind.Lancer, Kind.Stormcaller, Kind.Sunbird, Kind.Elemental];
    private static readonly Kind[] Dusk = [Kind.Shadelord, Kind.Ghoul, Kind.Spitter, Kind.Wyvern, Kind.Ogre, Kind.Wraith, Kind.Hydra, Kind.Wailer, Kind.Elemental];

    [Fact]
    public void Every_pairing_of_dawn_and_dusk_has_odds()
    {
        foreach (var a in Dawn)
            foreach (var b in Dusk)
            {
                if (a == Kind.Elemental && b == Kind.Elemental) continue;
                Assert.True(OddsTable.DawnWins.ContainsKey((a, b)), $"{a} vs {b}");
            }
    }

    [Fact]
    public void Measure()
    {
        if (Environment.GetEnvironmentVariable("ARCHON_ODDS") is not "1") return;
        const int fights = 64;
        var sb = new System.Text.StringBuilder();
        sb.Append("// Measured by OddsTableTests.Measure (ARCHON_ODDS=1): how often Dawn's piece beats Dusk's, at full life\n");
        sb.Append("// on neutral ground, from brawls between computer fighters. Regenerate after changing any piece.\n");
        sb.Append("namespace AVAArchon.Engine;\n\ninternal static class OddsTable\n{\n");
        sb.Append("    public static readonly Dictionary<(Kind Dawn, Kind Dusk), double> DawnWins = new()\n    {\n");
        foreach (var a in Dawn)
            foreach (var b in Dusk)
            {
                if (a == Kind.Elemental && b == Kind.Elemental) continue;
                double wins = 0;
                for (int s = 0; s < fights; s++)
                {
                    var f = Ext.Brawl(a, b, 1000 + s * 97 + (int)a * 13 + (int)b);
                    wins += f.Dawn.Dead == f.Dusk.Dead ? 0.5 : f.Dusk.Dead ? 1 : 0;
                }
                sb.Append($"        [(Kind.{a}, Kind.{b})] = {wins / fights:0.###},\n");
            }
        sb.Append("    };\n}\n");
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "AVAArchon.slnx"))) root = Path.GetDirectoryName(root)!;
        File.WriteAllText(Path.Combine(root, "src", "AVAArchon", "Engine", "OddsTable.cs"), sb.ToString());
    }
}
