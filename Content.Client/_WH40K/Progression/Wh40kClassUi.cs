using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared._WH40K.ClassProgression;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Progression;

public sealed partial class Wh40kClassUi : UIFragment
{
    private Wh40kClassUiModel _model = new();
    private Wh40kClassUiFragment? _fragment;
    private Wh40kClassTreeWindow? _window;
    private BoundUserInterface? _userInterface;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        CloseTreeWindow();
        _model = new Wh40kClassUiModel();
        _userInterface = userInterface;
        _fragment = new Wh40kClassUiFragment();
        _fragment.OpenTreeRequested += OpenTreeWindow;
        _fragment.Detached += CloseTreeWindow;
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
    }

    public override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (message is not Wh40kClassSnapshotBuiMessage snapshotMessage)
            return;

        _model.ApplyServerResponse(snapshotMessage.Status, snapshotMessage.Snapshot);
        UpdateConsumers();
    }

    private void OpenTreeWindow(string? specializationId)
    {
        if (_window == null)
        {
            var window = new Wh40kClassTreeWindow();
            window.SkillSelected += skillId =>
            {
                if (_model.SelectSkill(skillId))
                    UpdateConsumers();
            };
            window.PurchaseConfirmed += PurchaseSkill;
            window.OnClose += () => _window = null;
            _window = window;
        }

        UpdateConsumers();
        if (!_window.IsOpen)
            _window.OpenCentered();
        else
            _window.MoveToFront();
        _window.FocusSpecializationOnOpen(specializationId);
    }

    private void PurchaseSkill(string skillId)
    {
        if (_userInterface == null || !_model.BeginPurchase(skillId))
            return;

        UpdateConsumers();
        _userInterface.SendMessage(new CartridgeUiMessage(
            new Wh40kClassUiMessage(
                Wh40kClassUiAction.Purchase,
                skillId,
                _model.Snapshot!.Tree.Revision)));
    }

    private void UpdateConsumers()
    {
        _fragment?.UpdateSnapshot(_model.Snapshot, _model.Status);
        _fragment?.SetPending(_model.PurchasePending);
        _window?.UpdateSnapshot(_model.Snapshot, _model.Status, _model.SelectedSkillId);
        _window?.SetPending(_model.PurchasePending);
    }

    private void CloseTreeWindow()
    {
        if (_window?.IsOpen == true)
            _window.Close();
        _window = null;
    }
}
