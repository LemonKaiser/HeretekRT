using System;
using System.Linq;
using Content.Server.Verbs;
using Content.Server._WH40K.Localizations;
using Content.Shared.Examine;
using Content.Shared.Verbs;
using Content.Shared.Localizations;
using Content.Shared._Mono.Speech;
using JetBrains.Annotations;
using Robust.Shared.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.Server.Examine
{
    [UsedImplicitly]
    public sealed partial class ExamineSystem : ExamineSystemShared
    {
        [Dependency] private VerbSystem _verbSystem = default!;
        [Dependency] private WH40KPlayerCultureManager _playerCulture = default!;
        [Dependency] private WH40KEntityLocalizationCache _entityLocalization = default!;
        [Dependency] private ILocalizationManager _localization = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeNetworkEvent<ExamineSystemMessages.RequestExamineInfoMessage>(ExamineInfoRequest);
        }

        public override void SendExamineTooltip(EntityUid player, EntityUid target, FormattedMessage message, bool getVerbs, bool centerAtCursor)
        {
            if (!TryComp<ActorComponent>(player, out var actor))
                return;

            var session = actor.PlayerSession;
            if (!_playerCulture.IsCultureReady(session))
                return;

            using var culture = _playerCulture.CreateScope(session);
            var cultureName = _playerCulture.GetCulture(session);

            SortedSet<Verb>? verbs = null;
            if (getVerbs)
                verbs = _verbSystem.GetLocalVerbs(target, player, typeof(ExamineVerb));

            var ev = new ExamineSystemMessages.ExamineInfoResponseMessage(
                GetNetEntity(target), 0, message, verbs?.ToList(), centerAtCursor,
                cultureName: cultureName
            );

            RaiseNetworkEvent(ev, session.Channel);
        }

        private void ExamineInfoRequest(ExamineSystemMessages.RequestExamineInfoMessage request, EntitySessionEventArgs eventArgs)
        {
            var player = eventArgs.SenderSession;
            var session = eventArgs.SenderSession;
            var channel = player.Channel;
            var entity = GetEntity(request.NetEntity);

            if (!_playerCulture.IsCultureReady(session))
                return;

            // Examine text and verb identity are localized per requesting player. Keep the
            // complete request in that player's culture, including failure responses.
            using var culture = _playerCulture.CreateScope(player, request.CultureName);
            var cultureName = _playerCulture.GetCulture(player);

            if (session.AttachedEntity is not {Valid: true} playerEnt
                || !EntityManager.EntityExists(entity))
            {
                RaiseNetworkEvent(new ExamineSystemMessages.ExamineInfoResponseMessage(
                    request.NetEntity, request.Id, GetEntityNotFoundMessage(), cultureName: cultureName), channel);
                return;
            }

            if (!CanExamine(playerEnt, entity))
            {
                RaiseNetworkEvent(new ExamineSystemMessages.ExamineInfoResponseMessage(
                    request.NetEntity, request.Id, GetEntityOutOfRangeMessage(), knowTarget: false,
                    cultureName: cultureName), channel);
                return;
            }

            // The examine was successful, so trigger contextual speech.
            var speechEvent = new SpeechTriggerEvent(SpeechTrigger.Examined);
            RaiseLocalEvent(entity, ref speechEvent);

            SortedSet<Verb>? verbs = null;
            if (request.GetVerbs)
                verbs = _verbSystem.GetLocalVerbs(entity, playerEnt, typeof(ExamineVerb));

            var text = GetExamineText(entity, player.AttachedEntity);
            RaiseNetworkEvent(new ExamineSystemMessages.ExamineInfoResponseMessage(
                request.NetEntity, request.Id, text, verbs?.ToList(), cultureName: cultureName), channel);
        }

        private FormattedMessage GetEntityNotFoundMessage()
        {
            var message = new FormattedMessage();
            message.AddText(Loc.GetString("examine-system-entity-does-not-exist"));
            return message;
        }

        private FormattedMessage GetEntityOutOfRangeMessage()
        {
            var message = new FormattedMessage();
            message.AddText(Loc.GetString("examine-system-cant-see-entity"));
            return message;
        }

        protected override string GetEntityDescription(EntityUid entity, MetaDataComponent metadata)
        {
            var prototype = metadata.EntityPrototype;
            if (prototype == null)
                return metadata.EntityDescription;

            // Metadata is populated once when an entity is spawned. If it contains
            // either supported prototype translation, it is the prototype default
            // and can safely be replaced for this player's culture. Runtime/custom
            // descriptions remain untouched.
            try
            {
                var current = _entityLocalization.Get(_localization.DefaultCulture?.Name, prototype.ID).Desc;
                var russian = _entityLocalization.Get(ContentLocalizationManager.DefaultCultureName, prototype.ID).Desc;
                var english = _entityLocalization.Get(ContentLocalizationManager.FallbackCultureName, prototype.ID).Desc;

                return metadata.EntityDescription == russian || metadata.EntityDescription == english
                    ? current
                    : metadata.EntityDescription;
            }
            catch (Exception e)
            {
                Log.Warning($"Unable to resolve localized description for prototype '{prototype.ID}': {e}");
                return metadata.EntityDescription;
            }
        }
    }
}
