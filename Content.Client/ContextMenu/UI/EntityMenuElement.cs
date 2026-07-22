using System.Linq;
using Content.Client.Administration.Managers;
using Content.Client.Administration.Systems;
using Content.Client._WH40K.ItemRarity;
using Content.Client.UserInterface;
using Content.Shared.Administration;
using Content.Shared.IdentityManagement;
using Content.Shared._WH40K.ItemRarity.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client.ContextMenu.UI
{
    public sealed partial class EntityMenuElement : ContextMenuElement, IEntityControl
    {
        [Dependency] private IClientAdminManager _adminManager = default!;
        [Dependency] private IEntityManager _entityManager = default!;
        [Dependency] private IPlayerManager _playerManager = default!;
        [Dependency] private IPrototypeManager _prototypeManager = default!;

        private AdminSystem _adminSystem;
        private SharedItemRaritySystem ItemRaritySystem => _entityManager.System<SharedItemRaritySystem>();

        /// <summary>
        ///     The entity that can be accessed by interacting with this element.
        /// </summary>
        public EntityUid? Entity;

        /// <summary>
        ///     How many entities are accessible through this element's sub-menus.
        /// </summary>
        public int Count { get; private set; }

        public EntityMenuElement(EntityUid? entity = null)
        {
            IoCManager.InjectDependencies(this);

            _adminSystem = _entityManager.System<AdminSystem>();

            Entity = entity;
            if (Entity == null)
                return;

            Count = 1;
            UpdateEntity();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Entity = null;
            Count = 0;
        }

        private string? SearchPlayerName(EntityUid entity)
        {
            var netEntity = _entityManager.GetNetEntity(entity);
            return _adminSystem.PlayerList.FirstOrDefault(player => player.NetEntity == netEntity)?.Username;
        }

        /// <summary>
        ///     Update the entity count
        /// </summary>
        public void UpdateCount()
        {
            if (SubMenu == null)
                return;

            Count = 0;
            foreach (var subElement in SubMenu.MenuBody.Children)
            {
                if (subElement is EntityMenuElement entityElement)
                    Count += entityElement.Count;
            }

            IconLabel.Visible = Count > 1;
            if (IconLabel.Visible)
                IconLabel.Text = Count.ToString();
        }

        private string GetEntityDescriptionAdmin(EntityUid entity)
        {
            var representation = _entityManager.ToPrettyString(entity);

            var name = GetNameWithRarity(entity, representation.Name);
            var prototype = representation.Prototype;
            var playerName = representation.Session?.Name ?? SearchPlayerName(entity);
            var deleted = representation.Deleted;

            return $"{name} ({_entityManager.GetNetEntity(entity).ToString()}{(prototype != null ? $", {prototype}" : "")}{(playerName != null ? $", {playerName}" : "")}){(deleted ? "D" : "")}";
        }

        private string GetEntityDescription(EntityUid entity)
        {
            if (_adminManager.HasFlag(AdminFlags.Admin | AdminFlags.Debug))
            {
                return GetEntityDescriptionAdmin(entity);
            }

            var name = Identity.Name(entity, _entityManager, _playerManager.LocalEntity!);
            return GetNameWithRarity(entity, name);
        }

        private string GetNameWithRarity(EntityUid entity, string? name)
        {
            name ??= string.Empty;

            return ItemRarityTooltip.GetNameWithRarity(
                       ItemRaritySystem,
                       _prototypeManager,
                       entity,
                       name)
                   ?? name;
        }

        /// <summary>
        ///     Update the icon and text of this element based on the given entity or this element's own entity if none
        ///     is provided.
        /// </summary>
        public void UpdateEntity(EntityUid? entity = null)
        {
            entity ??= Entity;

            // check whether entity is null, invalid, or has been deleted.
            // _entityManager.Deleted() implicitly checks all of these.
            if (_entityManager.Deleted(entity))
            {
                Icon.SetEntity(null);
                Text = string.Empty;
            }
            else
            {
                Icon.SetEntity(entity);
                Text = GetEntityDescription(entity.Value);
            }
        }

        EntityUid? IEntityControl.UiEntity => Entity;
    }
}
