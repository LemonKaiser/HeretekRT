using System.Numerics;
using Content.Client.Weapons.Ranged.ItemStatus;
using Content.Client.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.BorgModules;

/// <summary>
/// Draws the item status ammo counter of a <see cref="CustomSpriteAmmoCounterComponent"/> gun using custom sprites.
/// </summary>
public sealed partial class CustomSpriteAmmoCounterSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CustomSpriteAmmoCounterComponent, GunSystem.AmmoCounterControlEvent>(OnControl, after: [typeof(GunSystem)]);
        SubscribeLocalEvent<CustomSpriteAmmoCounterComponent, GunSystem.UpdateAmmoCounterEvent>(OnAmmoCountUpdate);
    }

    private void OnControl(EntityUid uid, CustomSpriteAmmoCounterComponent comp, GunSystem.AmmoCounterControlEvent args)
    {
        if (comp.LoadedAmmoSprite == SpriteSpecifier.Invalid || comp.SpentAmmoSprite == SpriteSpecifier.Invalid)
            return;

        var loaded = _sprite.GetFrame(comp.LoadedAmmoSprite, TimeSpan.Zero);
        var spent = _sprite.GetFrame(comp.SpentAmmoSprite, TimeSpan.Zero);
        args.Control = new CustomIconStatusControl(loaded, spent, Math.Max(1, comp.NumberOfRows));
    }

    private void OnAmmoCountUpdate(EntityUid uid, CustomSpriteAmmoCounterComponent comp, GunSystem.UpdateAmmoCounterEvent args)
    {
        if (args.Control is not CustomIconStatusControl control)
            return;

        var ev = new GetAmmoCountEvent();
        RaiseLocalEvent(uid, ref ev);
        control.Update(ev.Count, ev.Capacity);
    }

    private sealed class CustomIconStatusControl : Control
    {
        private readonly CustomBulletRenderer _renderer;

        public CustomIconStatusControl(Texture loaded, Texture spent, int rows)
        {
            MinHeight = 15;
            HorizontalExpand = true;
            VerticalAlignment = VAlignment.Center;

            AddChild(_renderer = new CustomBulletRenderer(loaded, spent, rows)
            {
                HorizontalAlignment = HAlignment.Right,
                VerticalAlignment = VAlignment.Bottom,
            });
        }

        public void Update(int count, int capacity)
        {
            _renderer.Capacity = capacity;
            _renderer.Count = count;
        }
    }

    private sealed class CustomBulletRenderer : BaseBulletRenderer
    {
        private const int Separation = 2;

        private readonly Texture _loaded;
        private readonly Texture _spent;

        public CustomBulletRenderer(Texture loaded, Texture spent, int rows)
        {
            _loaded = loaded;
            _spent = spent;
            Rows = rows;
            Parameters = MakeParameters(_loaded.Width + Separation);
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            var itemSeparation = _loaded.Width + Separation;
            var perRow = (int) Math.Ceiling((double) Capacity / Rows);

            if (perRow > 1)
            {
                itemSeparation = (int) (Size.X - _loaded.Width) / (perRow - 1);
                itemSeparation = Math.Clamp(itemSeparation, 1, _loaded.Width + Separation);
            }

            Parameters = MakeParameters(itemSeparation);
            base.Draw(handle);
        }

        protected override void DrawItem(DrawingHandleScreen handle, Vector2 renderPos, bool spent, bool altColor)
        {
            handle.DrawTexture(spent ? _spent : _loaded, renderPos);
        }

        private LayoutParameters MakeParameters(int itemSeparation)
        {
            return new LayoutParameters
            {
                ItemWidth = _loaded.Width,
                ItemHeight = _loaded.Height,
                ItemSeparation = itemSeparation,
                MinCountPerRow = 3,
                VerticalSeparation = Separation,
            };
        }
    }
}
