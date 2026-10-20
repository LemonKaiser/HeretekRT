using Content.Server._WH40K.Bosses;
using Content.Server._WH40K.SectorMap.Components;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    public bool IsKoronusRound => IsGameRuleAdded<KoronusSectorRuleComponent>();

    public bool IsGameRuleAllowedInCurrentMode(EntityUid rule)
    {
        return !IsKoronusRound ||
               HasComp<KoronusSectorRuleComponent>(rule) ||
               HasComp<Wh40kBossEncounterRuleComponent>(rule);
    }
}
