using System.Linq;

namespace Content.Server._WH40K.Factions.Capture;

internal sealed class FactionCaptureProgress
{
    public string? Owner { get; private set; }
    public string? Challenger { get; private set; }
    public TimeSpan Elapsed { get; private set; }
    public bool Contested { get; private set; }

    public void Reset(bool clearOwner)
    {
        if (clearOwner)
            Owner = null;

        Challenger = null;
        Elapsed = TimeSpan.Zero;
        Contested = false;
    }

    public void Sample(HashSet<string> factions, TimeSpan elapsed, TimeSpan required)
    {
        var wasContested = Contested;
        Contested = factions.Count > 1;
        if (Contested)
        {
            // Keep progress only while the original challenger still has a presence in the circle.
            if (Challenger != null && !factions.Contains(Challenger))
            {
                Challenger = null;
                Elapsed = TimeSpan.Zero;
            }
            return;
        }

        if (factions.Count == 0)
        {
            Reset(clearOwner: false);
            return;
        }

        var side = factions.First();
        if (side == Owner)
        {
            Reset(clearOwner: false);
            return;
        }

        if (Challenger != side)
        {
            Challenger = side;
            Elapsed = TimeSpan.Zero;
            return;
        }

        if (!wasContested)
            Elapsed += elapsed;
        if (Elapsed < required)
            return;

        Owner = side;
        Reset(clearOwner: false);
    }
}

internal sealed class FactionCaptureStreaks
{
    private readonly Dictionary<string, int> _tiers = new();
    private readonly HashSet<string> _winners = new();

    public int Project(string? faction, int position = 1) =>
        Reward(Math.Min(3, (faction == null ? 0 : _tiers.GetValueOrDefault(faction)) + position));

    public int Award(string faction)
    {
        var tier = Math.Min(3, _tiers.GetValueOrDefault(faction) + 1);
        _tiers[faction] = tier;
        _winners.Add(faction);
        return Reward(tier);
    }

    public void CloseWindow()
    {
        foreach (var previous in _tiers.Keys.ToArray())
        {
            if (!_winners.Contains(previous))
                _tiers.Remove(previous);
        }
        _winners.Clear();
    }

    public void Reset()
    {
        _tiers.Clear();
        _winners.Clear();
    }

    private static int Reward(int tier) => 25000 * (tier + 1);
}
