using Content.Client._WH40K.StructureIntegrity;
using Content.Shared._WH40K.StructureIntegrity;

namespace Content.Client.Examine;

public sealed partial class ExamineSystem
{
    private void InitializeStructureIntegrity()
    {
        SubscribeNetworkEvent<Wh40kStructureIntegrityMessage>(OnStructureIntegrity);
    }

    private void OnStructureIntegrity(Wh40kStructureIntegrityMessage msg)
    {
        if (_examineTooltipOpen is not { Visible: true })
            return;

        if (!TryGetEntity(msg.Target, out var target) || target != _examinedEntity)
            return;

        var vBox = _examineTooltipOpen.GetChild(0).GetChild(0);
        if (vBox == null)
            return;

        for (var i = vBox.ChildCount - 1; i >= 0; i--)
        {
            if (vBox.GetChild(i).Name == Wh40kStructureIntegrityPanel.ControlName)
                vBox.RemoveChild(vBox.GetChild(i));
        }

        var integrityPanel = new Wh40kStructureIntegrityPanel(msg.Current, msg.Max);

        // Insert right before ExamineButtonsHBox if present, otherwise at the end.
        var buttons = vBox.FindControl<Robust.Client.UserInterface.Control>("ExamineButtonsHBox");
        vBox.AddChild(integrityPanel);
        if (buttons != null)
            integrityPanel.SetPositionInParent(buttons.GetPositionInParent());
    }
}
