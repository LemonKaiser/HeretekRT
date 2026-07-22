using System.Numerics;
using Content.Shared.CCVar;
using Content.Shared.Item;
using Content.Shared._WH40K.ItemRarity.Components;
using Content.Shared._WH40K.ItemRarity.Prototypes;
using Content.Shared._WH40K.ItemRarity.Systems;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.ItemRarity;

/// <summary>
/// Draws complete effects above entity sprites for all non-hovered rarity items.
/// The hovered item is excluded and handed to the below-entity overlay.
/// </summary>
public sealed class ItemRarityWorldEffectsOverlay : Overlay
{
    private readonly ItemRarityWorldEffectsRenderer _renderer;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    internal ItemRarityWorldEffectsOverlay(ItemRarityWorldEffectsRenderer renderer)
    {
        _renderer = renderer;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _renderer.ShouldDraw(args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        _renderer.Draw(args, hoveredOnly: false);
    }
}

/// <summary>
/// Draws the complete effect of the item currently under the mouse behind
/// entity sprites, keeping the hovered item's silhouette unobstructed.
/// </summary>
public sealed class ItemRarityWorldEffectsHoverOverlay : Overlay
{
    private readonly ItemRarityWorldEffectsRenderer _renderer;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    internal ItemRarityWorldEffectsHoverOverlay(ItemRarityWorldEffectsRenderer renderer)
    {
        _renderer = renderer;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _renderer.ShouldDraw(args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        _renderer.Draw(args, hoveredOnly: true);
    }
}

/// <summary>
/// Shared world-effect renderer used by both overlay stages. Keeping one
/// shader cache is important: an item must not receive two independent
/// animated aura states when the mouse moves over it.
/// </summary>
internal sealed class ItemRarityWorldEffectsRenderer : IDisposable
{
    private const int MaxVisibleEffects = 72;
    private const int CacheLifetimeFrames = 600;
    private const float EffectMergeRadius = 1f;
    private const float EffectMergeRadiusSquared = EffectMergeRadius * EffectMergeRadius;
    private const float QuadWidthInRadii = 2.0f;
    private const float LowerExtentInRadii = 0.82f;
    private const float UpperPaddingInRadii = 0.28f;

    private static readonly ProtoId<ShaderPrototype> AuraShaderId = "ItemRarityWorldGlow";

    private readonly IEntityManager _entityManager;
    private readonly IPrototypeManager _prototypeManager;
    private readonly IConfigurationManager _configuration;
    private readonly SharedItemRaritySystem _itemRaritySystem;
    private readonly SharedTransformSystem _transformSystem;
    private readonly SharedContainerSystem _containerSystem;
    private readonly EntityLookupSystem _lookupSystem;
    private readonly Func<EntityUid?> _hoveredEntity;
    private readonly List<VisibleRarityItem> _visibleCandidates = new(MaxVisibleEffects);
    private readonly List<VisibleRarityItem> _visibleItems = new(MaxVisibleEffects);
    private readonly Dictionary<EntityUid, CachedAura> _auras = new();
    private readonly List<EntityUid> _expiredAuras = new();

    private long _drawFrame;

    public ItemRarityWorldEffectsRenderer(
        IEntityManager entityManager,
        IPrototypeManager prototypeManager,
        IConfigurationManager configuration,
        SharedItemRaritySystem itemRaritySystem,
        Func<EntityUid?> hoveredEntity)
    {
        _entityManager = entityManager;
        _prototypeManager = prototypeManager;
        _configuration = configuration;
        _itemRaritySystem = itemRaritySystem;
        _hoveredEntity = hoveredEntity;
        _transformSystem = entityManager.System<SharedTransformSystem>();
        _containerSystem = entityManager.System<SharedContainerSystem>();
        _lookupSystem = entityManager.System<EntityLookupSystem>();
    }

    public bool ShouldDraw(in OverlayDrawArgs args)
    {
        return GetEffectMode() != EffectMode.Off && args.MapId != MapId.Nullspace;
    }

    public void BeginFrame()
    {
        _drawFrame++;
        if (_drawFrame % 120 == 0)
            PruneShaderCache();
    }

    public void Draw(in OverlayDrawArgs args, bool hoveredOnly)
    {
        var mode = GetEffectMode();
        if (mode == EffectMode.Off)
            return;

        CollectVisibleItems(args);
        var hoveredEntity = _hoveredEntity();

        foreach (var item in _visibleItems)
        {
            // A selected hovered item moves as a whole to the below-entity
            // overlay. All other selected effects stay in the world overlay.
            if ((item.Entity == hoveredEntity) != hoveredOnly)
                continue;

            var profile = GetProfile(item.Rarity.Tier);
            if (!profile.Visible)
                continue;

            DrawAura(args, item, profile, mode);
        }

        args.WorldHandle.UseShader(null);
    }

    private void CollectVisibleItems(in OverlayDrawArgs args)
    {
        _visibleCandidates.Clear();
        _visibleItems.Clear();
        var hoveredEntity = _hoveredEntity();

        foreach (var uid in _lookupSystem.GetEntitiesIntersecting(args.MapId, args.WorldBounds))
        {
            if (TryGetVisibleItem(uid, args.MapId, out var item))
                _visibleCandidates.Add(item);
        }

        // The most valuable item represents a dense loot pile. Entity UID is
        // the stable tie-breaker, so the visible aura does not flicker with
        // EntityLookup iteration order.
        _visibleCandidates.Sort(static (left, right) =>
        {
            var result = right.Rarity.Tier.CompareTo(left.Rarity.Tier);
            return result != 0 ? result : left.Entity.CompareTo(right.Entity);
        });

        // Hovering temporarily promotes that item inside its local group. It
        // still replaces the previous representative rather than adding a
        // second effect, and can therefore move cleanly to the lower overlay.
        if (hoveredEntity is { } hovered)
        {
            for (var i = 1; i < _visibleCandidates.Count; i++)
            {
                if (_visibleCandidates[i].Entity != hovered)
                    continue;

                var hoveredItem = _visibleCandidates[i];
                _visibleCandidates.RemoveAt(i);
                _visibleCandidates.Insert(0, hoveredItem);
                break;
            }
        }

        foreach (var candidate in _visibleCandidates)
        {
            var suppressed = false;

            foreach (var selected in _visibleItems)
            {
                // One world unit is one SS14 tile. Selected effects are kept
                // farther than one tile apart, while suppressed effects return
                // automatically as soon as their item is moved away.
                if (Vector2.DistanceSquared(candidate.Position, selected.Position) > EffectMergeRadiusSquared)
                    continue;

                suppressed = true;
                break;
            }

            if (suppressed)
                continue;

            _visibleItems.Add(candidate);
            if (_visibleItems.Count >= MaxVisibleEffects)
                break;
        }
    }

    private bool TryGetVisibleItem(EntityUid uid, MapId mapId, out VisibleRarityItem item)
    {
        item = default;

        if (!_entityManager.TryGetComponent<ItemComponent>(uid, out _) ||
            !_entityManager.TryGetComponent<TransformComponent>(uid, out var transform) ||
            transform.MapID != mapId ||
            !IsDirectlyInWorld(transform) ||
            _containerSystem.IsEntityInContainer(uid) ||
            _entityManager.TryGetComponent<ItemRarityComponent>(uid, out var rarityComponent) &&
            rarityComponent.WorldEffectSuppressed ||
            !_itemRaritySystem.TryGetRarity(uid, out var rarityId) ||
            !_prototypeManager.TryIndex(rarityId, out ItemRarityPrototype? rarity))
        {
            return false;
        }

        // The resolver treats every unmarked item as Stamped. Only an
        // explicitly stamped item receives its subtle grey aura; otherwise
        // every loose object on the station would become a shader draw.
        if (rarity.Tier == 1 && !_entityManager.HasComponent<ItemRarityComponent>(uid))
            return false;

        item = new VisibleRarityItem(
            uid,
            _transformSystem.GetWorldPosition(transform),
            rarity);
        return true;
    }

    /// <summary>
    /// A world aura is valid only while the item is a direct child of the map
    /// or grid. A held/equipped item can briefly retain the same MapID during
    /// prediction, so checking MapID or container state alone is not enough.
    /// </summary>
    internal static bool IsDirectlyInWorld(TransformComponent transform)
    {
        return transform.ParentUid == transform.MapUid || transform.ParentUid == transform.GridUid;
    }

    private void DrawAura(
        in OverlayDrawArgs args,
        VisibleRarityItem item,
        WorldEffectProfile profile,
        EffectMode mode)
    {
        var aura = GetOrCreateAura(item.Entity);
        aura.LastSeenFrame = _drawFrame;

        var shader = aura.Shader;
        var powerMultiplier = mode == EffectMode.Reduced ? 0.72f : 1f;
        var orbCount = mode == EffectMode.Full ? profile.Orbs : MathF.Min(profile.Orbs, 2f);
        var lowerExtent = profile.Radius * LowerExtentInRadii;
        var upperExtent = profile.PlumeHeight + profile.Radius * UpperPaddingInRadii;
        var totalHeight = lowerExtent + upperExtent;

        shader.SetParameter("auraScale", new Vector2(
            QuadWidthInRadii,
            totalHeight / profile.Radius));
        // auraBase stores the upper portion of the quad; the shader derives
        // the item's exact centre and keeps positive local Y pointing upward.
        shader.SetParameter("auraBase", upperExtent / totalHeight);
        shader.SetParameter("auraPlume", profile.PlumeHeight / profile.Radius);
        shader.SetParameter("auraTier", (float) item.Rarity.Tier);
        shader.SetParameter("auraSeed", item.Entity.Id % 4096 * 0.071f);
        shader.SetParameter("auraMotion", mode == EffectMode.Full ? 1f : 0f);
        shader.SetParameter("auraPower", profile.Power * powerMultiplier);
        shader.SetParameter("auraOrbCount", orbCount);
        shader.SetParameter("auraColor", item.Rarity.Color.WithAlpha(1f));
        shader.SetParameter("auraAccent", item.Rarity.AccentColor.WithAlpha(1f));

        args.WorldHandle.UseShader(shader);

        // World drawing is right-handed (+Y is up). The compact quad covers a
        // soft lower pool and a narrow rising beam above the item.
        var drawCenter = item.Position + Vector2.UnitY * ((upperExtent - lowerExtent) * 0.5f);
        args.WorldHandle.DrawRect(
            Box2.CenteredAround(
                drawCenter,
                new Vector2(profile.Radius * QuadWidthInRadii, totalHeight)),
            Color.White);
    }

    private CachedAura GetOrCreateAura(EntityUid uid)
    {
        if (_auras.TryGetValue(uid, out var aura))
            return aura;

        aura = new CachedAura(_prototypeManager.Index(AuraShaderId).InstanceUnique());
        _auras.Add(uid, aura);
        return aura;
    }

    private void PruneShaderCache()
    {
        _expiredAuras.Clear();

        foreach (var (uid, aura) in _auras)
        {
            if (!_entityManager.EntityExists(uid) || _drawFrame - aura.LastSeenFrame > CacheLifetimeFrames)
                _expiredAuras.Add(uid);
        }

        foreach (var uid in _expiredAuras)
        {
            _auras[uid].Shader.Dispose();
            _auras.Remove(uid);
        }
    }

    public void Dispose()
    {
        foreach (var aura in _auras.Values)
            aura.Shader.Dispose();

        _auras.Clear();
    }

    private static WorldEffectProfile GetProfile(byte tier)
    {
        return tier switch
        {
            // The beam is deliberately narrow but tall enough to read as a
            // loot marker instead of a square glow around the sprite.
            1 => new WorldEffectProfile(0.40f, 0.55f, 0.16f, 2f, true),
            2 => new WorldEffectProfile(0.44f, 0.66f, 0.20f, 3f, true),
            3 => new WorldEffectProfile(0.48f, 0.78f, 0.24f, 4f, true),
            4 => new WorldEffectProfile(0.53f, 0.90f, 0.28f, 5f, true),
            5 => new WorldEffectProfile(0.58f, 1.03f, 0.32f, 6f, true),
            6 => new WorldEffectProfile(0.63f, 1.16f, 0.36f, 7f, true),
            _ => new WorldEffectProfile(0f, 0f, 0f, 0f, false),
        };
    }

    private EffectMode GetEffectMode()
    {
        return Math.Clamp(_configuration.GetCVar(CCVars.ItemRarityWorldEffects), 0, 2) switch
        {
            0 => EffectMode.Off,
            1 => EffectMode.Reduced,
            _ => EffectMode.Full,
        };
    }

    private readonly record struct VisibleRarityItem(
        EntityUid Entity,
        Vector2 Position,
        ItemRarityPrototype Rarity);

    private readonly record struct WorldEffectProfile(
        float Radius,
        float PlumeHeight,
        float Power,
        float Orbs,
        bool Visible);

    private sealed class CachedAura(ShaderInstance shader)
    {
        public readonly ShaderInstance Shader = shader;
        public long LastSeenFrame;
    }

    private enum EffectMode
    {
        Off,
        Reduced,
        Full,
    }
}
