using System.Linq;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared.Preferences;
using Robust.Client;
using Robust.Client.Player;
using Robust.Shared.Network;
using Robust.Shared.Utility;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby
{
    /// <summary>
    ///     Receives <see cref="PlayerPreferences" /> and <see cref="GameSettings" /> from the server during the initial
    ///     connection.
    ///     Stores preferences on the server through <see cref="SelectCharacter" /> and <see cref="UpdateCharacter" />.
    /// </summary>
    public sealed partial class ClientPreferencesManager : IClientPreferencesManager
    {
        [Dependency] private IClientNetManager _netManager = default!;
        [Dependency] private IBaseClient _baseClient = default!;
        [Dependency] private IPlayerManager _playerManager = default!;

        public event Action? OnServerDataLoaded;
        public event Action? OnWh40kProgressChanged;
        public event Action<Wh40kOnboardingCompletionStatus>? OnWh40kOnboardingCompletionFinished;

        public GameSettings Settings { get; private set; } = default!;
        public PlayerPreferences Preferences { get; private set; } = default!;
        public Wh40kPlayerProgressSnapshot Wh40kProgress { get; private set; } = Wh40kPlayerProgressSnapshot.Unknown;
        public bool Wh40kOnboardingCompletionPending { get; private set; }

        public void Initialize()
        {
            _netManager.RegisterNetMessage<MsgPreferencesAndSettings>(HandlePreferencesAndSettings);
            _netManager.RegisterNetMessage<MsgUpdateCharacter>();
            _netManager.RegisterNetMessage<MsgSelectCharacter>();
            _netManager.RegisterNetMessage<MsgDeleteCharacter>();
            _netManager.RegisterNetMessage<MsgCompleteWh40kOnboarding>();
            _netManager.RegisterNetMessage<MsgWh40kOnboardingCompleted>(HandleWh40kOnboardingCompleted);

            _baseClient.RunLevelChanged += BaseClientOnRunLevelChanged;
        }

        private void BaseClientOnRunLevelChanged(object? sender, RunLevelChangedEventArgs e)
        {
            if (e.NewLevel == ClientRunLevel.Initialize)
            {
                Settings = default!;
                Preferences = default!;
                Wh40kProgress = Wh40kPlayerProgressSnapshot.Unknown;
                Wh40kOnboardingCompletionPending = false;
            }
        }

        public void SelectCharacter(ICharacterProfile profile)
        {
            SelectCharacter(Preferences.IndexOfCharacter(profile));
        }

        public void SelectCharacter(int slot)
        {
            if (!Wh40kProgress.CanUseLegacyPersonalization &&
                slot != Wh40kProgress.OnboardingProfileSlot)
            {
                return;
            }

            Preferences = new PlayerPreferences(Preferences.Characters, slot, Preferences.AdminOOCColor);
            var msg = new MsgSelectCharacter
            {
                SelectedCharacterIndex = slot
            };
            _netManager.ClientSendMessage(msg);
        }

        public void UpdateCharacter(ICharacterProfile profile, int slot)
        {
            if (!Wh40kProgress.CanUseLegacyPersonalization)
                return;

            var collection = IoCManager.Instance!;

            // Verify company exists if this is a humanoid profile
            if (profile is HumanoidCharacterProfile humanoidProfile)
            {
                var protoManager = IoCManager.Resolve<IPrototypeManager>();
                if (!string.IsNullOrEmpty(humanoidProfile.Company) &&
                    humanoidProfile.Company != "None" &&
                    !protoManager.HasIndex<CompanyPrototype>(humanoidProfile.Company))
                {
                    profile = humanoidProfile.WithCompany("None");
                }
            }

            profile.EnsureValid(_playerManager.LocalSession!, collection);
            var characters = new Dictionary<int, ICharacterProfile>(Preferences.Characters) {[slot] = profile};
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor);
            var msg = new MsgUpdateCharacter
            {
                Profile = profile,
                Slot = slot
            };
            _netManager.ClientSendMessage(msg);
        }

        public bool CompleteWh40kOnboarding(HumanoidCharacterProfile profile)
        {
            if (Wh40kOnboardingCompletionPending ||
                !Wh40kProgress.IsKnown ||
                Wh40kProgress.OnboardingStatus != Wh40kOnboardingStatus.Required)
            {
                return false;
            }

            Wh40kOnboardingCompletionPending = true;
            _netManager.ClientSendMessage(new MsgCompleteWh40kOnboarding { Profile = profile });
            return true;
        }

        public void CreateCharacter(ICharacterProfile profile)
        {
            if (!Wh40kProgress.CanUseLegacyPersonalization)
                return;

            var characters = new Dictionary<int, ICharacterProfile>(Preferences.Characters);
            var lowest = Enumerable.Range(0, Settings.MaxCharacterSlots)
                .Except(characters.Keys)
                .FirstOrNull();

            if (lowest == null)
            {
                throw new InvalidOperationException("Out of character slots!");
            }

            var l = lowest.Value;
            characters.Add(l, profile);
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor);

            UpdateCharacter(profile, l);
        }

        public void DeleteCharacter(ICharacterProfile profile)
        {
            DeleteCharacter(Preferences.IndexOfCharacter(profile));
        }

        public void DeleteCharacter(int slot)
        {
            if (!Wh40kProgress.CanUseLegacyPersonalization)
                return;

            var characters = Preferences.Characters.Where(p => p.Key != slot);
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor);
            var msg = new MsgDeleteCharacter
            {
                Slot = slot
            };
            _netManager.ClientSendMessage(msg);
        }

        private void HandlePreferencesAndSettings(MsgPreferencesAndSettings message)
        {
            Preferences = message.Preferences;
            Settings = message.Settings;
            Wh40kProgress = message.Wh40kProgress;

            // Check if any character profiles have invalid companies and fix them
            if (Preferences != null)
            {
                var protoManager = IoCManager.Resolve<IPrototypeManager>();
                var needsUpdate = false;
                var characters = new Dictionary<int, ICharacterProfile>();

                foreach (var (slot, profile) in Preferences.Characters)
                {
                    var updatedProfile = profile;

                    if (profile is HumanoidCharacterProfile humanoidProfile &&
                        !string.IsNullOrEmpty(humanoidProfile.Company) &&
                        humanoidProfile.Company != "None" &&
                        !protoManager.HasIndex<CompanyPrototype>(humanoidProfile.Company))
                    {
                        updatedProfile = humanoidProfile.WithCompany("None");
                        needsUpdate = true;
                    }

                    characters[slot] = updatedProfile;
                }

                if (needsUpdate && Wh40kProgress.CanUseLegacyPersonalization)
                {
                    Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor);

                    // Update the selected character on the server if needed
                    var selectedIndex = Preferences.SelectedCharacterIndex;
                    if (characters.TryGetValue(selectedIndex, out var selectedProfile))
                    {
                        var msg = new MsgUpdateCharacter
                        {
                            Profile = selectedProfile,
                            Slot = selectedIndex
                        };
                        _netManager.ClientSendMessage(msg);
                    }
                }
            }

            OnWh40kProgressChanged?.Invoke();
            OnServerDataLoaded?.Invoke();
        }

        private void HandleWh40kOnboardingCompleted(MsgWh40kOnboardingCompleted message)
        {
            Wh40kOnboardingCompletionPending = false;

            if (message.Status == Wh40kOnboardingCompletionStatus.Success &&
                message.Profile != null &&
                message.ProfileSlot >= 0 &&
                Preferences != null)
            {
                var characters = new Dictionary<int, ICharacterProfile>(Preferences.Characters)
                {
                    [message.ProfileSlot] = message.Profile,
                };
                Preferences = new PlayerPreferences(characters, message.ProfileSlot, Preferences.AdminOOCColor);
                Wh40kProgress = message.Progress;
                OnWh40kProgressChanged?.Invoke();
            }

            OnWh40kOnboardingCompletionFinished?.Invoke(message.Status);
        }
    }
}
