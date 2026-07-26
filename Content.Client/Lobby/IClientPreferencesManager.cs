using Content.Shared.Preferences;
using Content.Shared._WH40K.CharacterCreation;

namespace Content.Client.Lobby
{
    public interface IClientPreferencesManager
    {
        event Action OnServerDataLoaded;
        event Action OnWh40kProgressChanged;
        event Action<Wh40kOnboardingCompletionStatus> OnWh40kOnboardingCompletionFinished;

        bool ServerDataLoaded => Settings != null;

        GameSettings? Settings { get; }
        PlayerPreferences? Preferences { get; }
        Wh40kPlayerProgressSnapshot Wh40kProgress { get; }
        bool Wh40kOnboardingCompletionPending { get; }
        void Initialize();
        void SelectCharacter(ICharacterProfile profile);
        void SelectCharacter(int slot);
        void UpdateCharacter(ICharacterProfile profile, int slot);
        bool CompleteWh40kOnboarding(HumanoidCharacterProfile profile);
        void CreateCharacter(ICharacterProfile profile);
        void DeleteCharacter(ICharacterProfile profile);
        void DeleteCharacter(int slot);
    }
}
