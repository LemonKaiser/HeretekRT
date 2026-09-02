using System.Linq;
using Content.Shared._Forge.Barks;
using Content.Shared.CCVar;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private readonly List<BarkPrototype> _barkVoices = [];

    private void InitializeBarkVoices()
    {
        _barkVoices.Clear();
        _barkVoices.AddRange(_prototypeManager
            .EnumeratePrototypes<BarkPrototype>()
            .Where(bark => bark.RoundStart)
            .OrderBy(bark => bark.Name));

        BarksContainer.Visible = _cfgManager.GetCVar(CCVars.BarksEnabled) && _barkVoices.Count > 0;
        if (!BarksContainer.Visible)
            return;

        BarkVoiceButton.OnItemSelected += args =>
        {
            if (args.Id < 0 || args.Id >= _barkVoices.Count || IsWh40kAppearanceEditingLocked)
                return;

            BarkVoiceButton.SelectId(args.Id);
            SetBarkVoice(_barkVoices[args.Id].ID);
        };

        BarkVoicePlayButton.OnPressed += _ => PlayPreviewBark();
    }

    private void UpdateBarkVoiceControls()
    {
        if (Profile == null || !BarksContainer.Visible)
            return;

        BarkVoiceButton.Clear();
        for (var i = 0; i < _barkVoices.Count; i++)
        {
            var bark = _barkVoices[i];
            BarkVoiceButton.AddItem(bark.Name, i);
        }

        var selectedId = _barkVoices.FindIndex(bark => bark.ID == Profile.BarkVoice);
        if (!BarkVoiceButton.TrySelectId(selectedId) && _barkVoices.Count > 0)
        {
            BarkVoiceButton.SelectId(0);
            SetBarkVoice(_barkVoices[0].ID);
        }
    }

    private void PlayPreviewBark()
    {
        if (Profile?.BarkVoice is not { } barkVoice)
            return;

        _entManager.System<Content.Client._Forge.Barks.BarkSystem>().RequestPreviewBark(barkVoice);
    }

    private void SetBarkVoice(string barkVoice)
    {
        Profile = Profile?.WithBarkVoice(barkVoice);
        SetDirty();
    }
}
