using Content.Shared.Interaction;

namespace Content.Shared.UserInterface;

public sealed partial class ActivatableUISystem
{
    [Dependency] private SharedInteractionSystem _interaction = default!;

    public bool InteractUI(EntityUid user, EntityUid uiEntity, ActivatableUIComponent aui)
    {
        var interactionParticle = false;
        return InteractUI(user, uiEntity, aui, ref interactionParticle);
    }

    private void InteractUIFromVerb(EntityUid user, EntityUid uiEntity, ActivatableUIComponent aui)
    {
        var interactionParticle = false;
        InteractUI(user, uiEntity, aui, ref interactionParticle);
        _interaction.DoContactInteraction(user, uiEntity, null, true, interactionParticles: interactionParticle);
    }
}
