using Robust.Shared.Configuration;

namespace Content.Shared._Mono.CCVar;

/// <summary>
/// Contains CVars used by Mono.
/// </summary>
[CVarDefs]
public sealed partial class MonoCVars
{
    #region Cleanup

    /// <summary>
    ///     Master switch for all Mono automatic cleanup systems.
    /// </summary>
    public static readonly CVarDef<bool> CleanupEnabled =
        CVarDef.Create("mono.cleanup.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Evaluate and report cleanup candidates without deleting them.
    /// </summary>
    public static readonly CVarDef<bool> CleanupDryRun =
        CVarDef.Create("mono.cleanup.dry_run", false, CVar.SERVERONLY);

    /// <summary>
    ///     Whether to enable cleanup debug mode, making it run much more often.
    /// </summary>
    public static readonly CVarDef<bool> CleanupDebug =
        CVarDef.Create("mono.cleanup.debug", false, CVar.SERVERONLY);

    /// <summary>
    ///     Whether to log every single entity cleanup deletes.
    /// </summary>
    public static readonly CVarDef<bool> CleanupLog =
        CVarDef.Create("mono.cleanup.log", false, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum number of candidates a cleanup system may validate in one tick.
    /// </summary>
    public static readonly CVarDef<int> CleanupMaxChecksPerTick =
        CVarDef.Create("mono.cleanup.max_checks_per_tick", 64, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum number of entities a cleanup system may delete in one tick.
    /// </summary>
    public static readonly CVarDef<int> CleanupMaxDeletesPerTick =
        CVarDef.Create("mono.cleanup.max_deletes_per_tick", 8, CVar.SERVERONLY);

    /// <summary>
    ///     Wall-clock budget, in milliseconds, for each cleanup system per tick.
    /// </summary>
    public static readonly CVarDef<float> CleanupMaximumProcessTimeMs =
        CVarDef.Create("mono.cleanup.maximum_process_time_ms", 1.0f, CVar.SERVERONLY);

    /// <summary>
    ///     Don't delete non-grids at most this close to a grid.
    /// </summary>
    public static readonly CVarDef<float> CleanupMaxGridDistance =
        CVarDef.Create("mono.cleanup.max_grid_distance", 30.0f, CVar.SERVERONLY);

    /// <summary>
    ///     How far away from any players can a mob be until it gets cleaned up.
    /// </summary>
    public static readonly CVarDef<float> MobCleanupDistance =
        CVarDef.Create("mono.cleanup.mob.distance", 1280.0f, CVar.SERVERONLY);

    /// <summary>
    ///     How long an abandoned space NPC must remain eligible before cleanup.
    /// </summary>
    public static readonly CVarDef<float> MobCleanupDuration =
        CVarDef.Create("mono.cleanup.mob.duration", 10f * 60f, CVar.SERVERONLY);

    /// <summary>
    ///     How long a dead, never-player-controlled NPC must remain abandoned before cleanup.
    /// </summary>
    public static readonly CVarDef<float> MobCorpseCleanupDuration =
        CVarDef.Create("mono.cleanup.mob.corpse_duration", 60f * 60f, CVar.SERVERONLY);

    /// <summary>
    ///     Player exclusion radius for dead NPC cleanup.
    /// </summary>
    public static readonly CVarDef<float> MobCorpseCleanupDistance =
        CVarDef.Create("mono.cleanup.mob.corpse_distance", 30f, CVar.SERVERONLY);

    /// <summary>
    ///     How far away from any players can a grid be until it gets cleaned up.
    /// </summary>
    public static readonly CVarDef<float> GridCleanupDistance =
        CVarDef.Create("mono.cleanup.grid.distance", 628.0f, CVar.SERVERONLY);

    /// <summary>
    ///     How much can a grid at most be worth for it to be cleaned up.
    /// </summary>
    public static readonly CVarDef<float> GridCleanupMaxValue =
        CVarDef.Create("mono.cleanup.grid.max_value", 30000.0f, CVar.SERVERONLY);

    /// <summary>
    ///     At most how many tiles for a grid to have for it to be cleaned up more aggressively.
    /// </summary>
    public static readonly CVarDef<int> GridCleanupAggressiveTiles =
        CVarDef.Create("mono.grid_cleanup_aggressive_tiles", 10, CVar.SERVERONLY);

    /// <summary>
    ///     Duration, in seconds, for how long a grid has to fulfill cleanup conditions to get cleaned up.
    /// </summary>
    public static readonly CVarDef<float> GridCleanupDuration =
        CVarDef.Create("mono.grid_cleanup_duration", 60f * 30f, CVar.SERVERONLY);

    /// <summary>
    ///     How far away from any players does a spaced entity have to be in order to get cleaned up.
    /// </summary>
    public static readonly CVarDef<float> SpaceCleanupDistance =
        CVarDef.Create("mono.cleanup.space.distance", 628f, CVar.SERVERONLY);

    /// <summary>
    ///     How long a general loose object must remain abandoned in space before periodic cleanup.
    /// </summary>
    public static readonly CVarDef<float> SpaceCleanupDuration =
        CVarDef.Create("mono.cleanup.space.duration", 15f * 60f, CVar.SERVERONLY);

    /// <summary>
    ///     How much can a spaced entity at most be worth for it to be cleaned up.
    /// </summary>
    public static readonly CVarDef<float> SpaceCleanupMaxValue =
        CVarDef.Create("mono.cleanup.space.max_value", 3000.0f, CVar.SERVERONLY);

    /// <summary>
    ///     How long explicit trash must remain unused before periodic cleanup.
    /// </summary>
    public static readonly CVarDef<float> GarbageCleanupDuration =
        CVarDef.Create("mono.cleanup.garbage.duration", 30f * 60f, CVar.SERVERONLY);

    /// <summary>
    ///     Longer grace period for explicit trash on station, claimed and deeded grids.
    /// </summary>
    public static readonly CVarDef<float> GarbageCleanupProtectedGridDuration =
        CVarDef.Create("mono.cleanup.garbage.protected_grid_duration", 60f * 60f, CVar.SERVERONLY);

    /// <summary>
    ///     Grace period for explicit trash floating directly in open space.
    /// </summary>
    public static readonly CVarDef<float> GarbageCleanupSpaceDuration =
        CVarDef.Create("mono.cleanup.garbage.space_duration", 10f * 60f, CVar.SERVERONLY);

    /// <summary>
    ///     Explicit trash is never cleaned while a player is within this range.
    /// </summary>
    public static readonly CVarDef<float> GarbageCleanupPlayerDistance =
        CVarDef.Create("mono.cleanup.garbage.player_distance", 30f, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum recursive value of an explicit trash entity eligible for cleanup.
    /// </summary>
    public static readonly CVarDef<float> GarbageCleanupMaxValue =
        CVarDef.Create("mono.cleanup.garbage.max_value", 100f, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum number of cleanable decals retained on one grid.
    /// </summary>
    public static readonly CVarDef<int> DecalCleanupMaxPerGrid =
        CVarDef.Create("mono.cleanup.decal.max_per_grid", 2000, CVar.SERVERONLY);

    /// <summary>
    ///     After a shuttle impact, how aggressively to sweep. Makes sweep more willing to delete items close to grids or players.
    /// </summary>
    public static readonly CVarDef<float> ImpactSweepAggression =
        CVarDef.Create("mono.cleanup.impact.aggression", 0.1f, CVar.SERVERONLY);

    /// <summary>
    ///     After a shuttle impact, in how much after the impact to perform the sweep.
    /// </summary>
    public static readonly CVarDef<float> ImpactSweepDelay =
        CVarDef.Create("mono.cleanup.impact.delay", 5.0f, CVar.SERVERONLY);

    /// <summary>
    ///     After a shuttle impact, in how much of a radius to immediately sweep for loose items.
    /// </summary>
    public static readonly CVarDef<float> ImpactSweepRadius =
        CVarDef.Create("mono.cleanup.impact.radius", 60.0f, CVar.SERVERONLY);

    #endregion

    /// <summary>
    ///     Whether to play radio static/noise sounds when receiving radio messages on headsets.
    /// </summary>
    public static readonly CVarDef<bool> RadioNoiseEnabled =
        CVarDef.Create("mono.radio_noise_enabled", true, CVar.ARCHIVE | CVar.CLIENTONLY);


    #region Audio

    /// <summary>
    /// HULLROT: Wether or not to play combat music when combatmode is on.
    /// </summary>
    public static readonly CVarDef<bool> CombatMusicEnabled =
        CVarDef.Create("mono.combat_music.enabled", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// HULLROT: Combat mode music volume.
    /// </summary>
    public static readonly CVarDef<float> CombatMusicVolume =
        CVarDef.Create("mono.combat_music_volume", 1.5f, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// HULLROT: Time needed with combatmode on to turn on combat music.
    /// </summary>
    public static readonly CVarDef<int> CombatMusicWindUpTime =
        CVarDef.Create("mono.combat_music_windup_time", 3, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// HULLROT: Time needed with combatmode off to turn off combat music.
    /// </summary>
    public static readonly CVarDef<int> CombatMusicWindDownTime =
        CVarDef.Create("mono.combat_music_winddown_time", 30, CVar.ARCHIVE | CVar.CLIENTONLY);


    /// <summary>
    ///     Whether to render sounds with echo when they are in 'large' open, rooved areas.
    /// </summary>
    /// <seealso cref="AreaEchoSystem"/>
    public static readonly CVarDef<bool> AreaEchoEnabled =
        CVarDef.Create("mono.area_echo.enabled", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    ///     If false, area echos calculate with 4 directions (NSEW).
    ///         Otherwise, area echos calculate with all 8 directions.
    /// </summary>
    /// <seealso cref="AreaEchoSystem"/>
    public static readonly CVarDef<bool> AreaEchoHighResolution =
        CVarDef.Create("mono.area_echo.alldirections", false, CVar.ARCHIVE | CVar.CLIENTONLY);


    /// <summary>
    ///     How many times a ray can bounce off a surface for an echo calculation.
    /// </summary>
    /// <seealso cref="AreaEchoSystem"/>
    public static readonly CVarDef<int> AreaEchoReflectionCount =
        CVarDef.Create("mono.area_echo.max_reflections", 1, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    ///     Distantial interval, in tiles, in the rays used to calculate the roofs of an open area for echos,
    ///         or the ray's distance to space, at which the tile at that point of the ray is processed.
    ///
    ///     The lower this is, the more 'predictable' and computationally heavy the echoes are.
    /// </summary>
    /// <seealso cref="AreaEchoSystem"/>
    public static readonly CVarDef<float> AreaEchoStepFidelity =
        CVarDef.Create("mono.area_echo.step_fidelity", 5f, CVar.CLIENTONLY);

    /// <summary>
    ///     Interval between updates for every audio entity.
    /// </summary>
    /// <seealso cref="AreaEchoSystem"/>
    public static readonly CVarDef<TimeSpan> AreaEchoRecalculationInterval =
        CVarDef.Create("mono.area_echo.recalculation_interval", TimeSpan.FromSeconds(15), CVar.ARCHIVE | CVar.CLIENTONLY);

    #endregion

    #region Detection

    /// <summary>
    ///     Multiplier of grid thermal detection radius.
    /// </summary>
    public static readonly CVarDef<float> ThermalDetectionMultiplier =
        CVarDef.Create("mono.detection.thermal_multiplier", 2f, CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    ///     Multiplier of grid visual detection radius.
    /// </summary>
    public static readonly CVarDef<float> VisualDetectionMultiplier =
        CVarDef.Create("mono.detection.visual_multiplier", 16f, CVar.ARCHIVE | CVar.REPLICATED);

    #endregion

    #region Projectile Raycasting

    /// <summary>
    ///     Speed threshold for projectiles to be calculated by raycast instead of normal collision.
    /// </summary>
    public static readonly CVarDef<float> ProjectileRaycastSpeedThreshold =
        CVarDef.Create("mono.projectile.raycast_speed_threshold", 75f, CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    ///     Do we automatically adapt our raycast threshold based off the set tickrate?
    ///     I.e. half the tickrate would mean a halved speed threshold.
    ///     Should probably be disabled for replays, if we ever have them.
    /// </summary>
    public static readonly CVarDef<bool> ProjectileAdaptiveRaycastThreshold =
        CVarDef.Create("mono.projectile.adaptive_raycast_threshold", true, CVar.ARCHIVE | CVar.REPLICATED);

    #endregion

    #region Misc

    public static readonly CVarDef<bool> CompanyWhitelist =
        CVarDef.Create("mono.company_whitelist", true, CVar.ARCHIVE | CVar.REPLICATED);

    #endregion

    #region Bank

    /// <summary>
    ///     Threshold before the IRS comes into effect.
    /// </summary>
    public static readonly CVarDef<float> DepositThreshold =
        CVarDef.Create("mono.deposit.threshold", 2000000f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     How exponential taxes are. When I set this to 5, it broke the integer limit, so probably don't mess with it.
    /// </summary>
    public static readonly CVarDef<float> DepositHighExp =
        CVarDef.Create("mono.deposit.high_exp", 2f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Whether to enable depositing cash. Good for admin events or sandbox.
    /// </summary>
    public static readonly CVarDef<bool> DepositEnabled =
        CVarDef.Create("mono.deposit.enabled", true, CVar.SERVER | CVar.REPLICATED);

    #endregion
}
