using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.Interaction;

[CVarDefs]
public static class Wh40kInteractionCVars
{
    public static readonly CVarDef<bool> InteractionParticlesEnabled =
        CVarDef.Create("particles.interaction_enabled", true, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float> InHandInteractionParticleScale =
        CVarDef.Create("particles.inhand_interaction_scale", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
