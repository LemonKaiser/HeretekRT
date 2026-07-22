using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Localization;

namespace Content.Client.Voting.UI
{
    /// <summary>
    ///     LITERALLY just a button that opens the vote call menu.
    ///     Automatically disables itself if the client cannot call votes.
    /// </summary>
    public sealed partial class VoteCallMenuButton : Button
    {
        [Dependency] private IVoteManager _voteManager = default!;
        private VoteCallMenu? _menu;

        public VoteCallMenuButton()
        {
            IoCManager.InjectDependencies(this);

            Text = Loc.GetString("ui-vote-menu-button");
            OnPressed += OnOnPressed;
        }

        private void OnOnPressed(ButtonEventArgs obj)
        {
            if (_menu is { Disposed: false })
            {
                if (_menu.IsOpen)
                {
                    _menu.Close();
                    return;
                }
            }
            else
            {
                _menu = new VoteCallMenu();
            }

            _menu.OpenCentered();
        }

        protected override void EnteredTree()
        {
            base.EnteredTree();

            UpdateCanCall(_voteManager.CanCallVote);
            _voteManager.CanCallVoteChanged -= UpdateCanCall;
        }

        protected override void ExitedTree()
        {
            base.ExitedTree();

            _voteManager.CanCallVoteChanged += UpdateCanCall;
        }

        private void UpdateCanCall(bool canCall)
        {
            Disabled = !canCall;
        }
    }
}
