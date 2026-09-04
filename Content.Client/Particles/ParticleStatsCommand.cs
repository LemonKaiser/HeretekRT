using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Client.Particles;

/// <summary>
/// Prints the most recent particle simulation and rendering measurements.
/// </summary>
[AnyCommand]
public sealed class ParticleStatsCommand : IConsoleCommand
{
    public string Command => "particlestats";
    public string Description => "Prints particle emitter, budget, simulation and rendering statistics.";
    public string Help => $"Usage: {Command}";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var stats = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<ParticleSystem>().Statistics;
        shell.WriteLine(
            $"emitters={stats.ActiveEmitters}; live={stats.LiveParticles}; cost={stats.LiveCost:0.##}; " +
            $"simulated={stats.SimulatedParticles}; emitted={stats.EmittedParticles}; culled={stats.CulledParticles}; " +
            $"drawn={stats.DrawnParticles}; draws={stats.DrawCalls}; " +
            $"simulation={stats.SimulationMilliseconds:0.###}ms; render-prep={stats.RenderPreparationMilliseconds:0.###}ms");
    }
}
