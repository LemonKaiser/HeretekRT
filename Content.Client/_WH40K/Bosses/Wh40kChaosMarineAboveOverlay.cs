namespace Content.Client._WH40K.Bosses;

public sealed class Wh40kChaosMarineAboveOverlay(IEntityManager entities) : Wh40kChaosMarineOverlay(entities, true);

public sealed class Wh40kChaosMarineBelowOverlay(IEntityManager entities) : Wh40kChaosMarineOverlay(entities, false);
