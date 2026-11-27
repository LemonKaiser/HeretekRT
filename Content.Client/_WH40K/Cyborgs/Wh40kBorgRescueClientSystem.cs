using System.Numerics;
using Content.Shared.Buckle.Components;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client._WH40K.Cyborgs;

/// <summary>
/// Dynamically adjusts draw depth and vertical offset of patients buckled onto a cyborg chassis
/// according to the cyborg's facing direction, and restores visuals cleanly on unbuckle.
/// </summary>
public sealed class Wh40kBorgRescueClientSystem : EntitySystem
{
    private const float BackViewPatientLift = 0.4f;
    private const float SideViewPatientLift = 0.2f;

    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private readonly HashSet<EntityUid> _renderedPatients = new();
    private readonly HashSet<EntityUid> _currentPatients = new();
    private readonly Dictionary<EntityUid, int> _originalDrawDepths = new();
    private readonly Dictionary<EntityUid, Vector2> _lastSpriteOffsets = new();
    private readonly Dictionary<EntityUid, Vector2> _appliedSpriteOffsets = new();

    public override void FrameUpdate(float frameTime)
    {
        _currentPatients.Clear();

        var query = EntityQueryEnumerator<BorgChassisComponent, StrapComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out _, out var strap, out var strapSprite))
        {
            var direction = (_transform.GetWorldRotation(uid) + _eye.CurrentEye.Rotation).GetCardinalDir();
            var patientLift = direction switch
            {
                Direction.North => BackViewPatientLift,
                Direction.East or Direction.West => SideViewPatientLift,
                _ => 0f,
            };
            var drawDepth = strapSprite.DrawDepth + (direction switch
            {
                Direction.North => 2,
                Direction.South => -1,
                _ => 1,
            });

            foreach (var patient in strap.BuckledEntities)
            {
                if (!TryComp<BuckleComponent>(patient, out var buckle)
                    || !TryComp<SpriteComponent>(patient, out var patientSprite))
                    continue;

                _originalDrawDepths.TryAdd(patient, buckle.OriginalDrawDepth ?? patientSprite.DrawDepth);

                var appliedOffset = new Vector2(0f, patientLift);
                var originalOffset = patientSprite.Offset;
                if (_lastSpriteOffsets.TryGetValue(patient, out var lastOffset)
                    && lastOffset == patientSprite.Offset
                    && _appliedSpriteOffsets.TryGetValue(patient, out var lastApplied))
                {
                    originalOffset -= lastApplied;
                }

                var spriteOffset = originalOffset + appliedOffset;
                if (patientSprite.Offset != spriteOffset)
                    _sprite.SetOffset((patient, patientSprite), spriteOffset);

                _lastSpriteOffsets[patient] = spriteOffset;
                _appliedSpriteOffsets[patient] = appliedOffset;

                if (patientSprite.DrawDepth != drawDepth)
                    _sprite.SetDrawDepth((patient, patientSprite), drawDepth);

                _currentPatients.Add(patient);
            }
        }

        foreach (var patient in _renderedPatients)
        {
            if (_currentPatients.Contains(patient))
                continue;

            RestorePatientVisuals(patient);
        }

        _renderedPatients.Clear();
        foreach (var patient in _currentPatients)
            _renderedPatients.Add(patient);
    }

    private void RestorePatientVisuals(EntityUid patient)
    {
        if (!_originalDrawDepths.TryGetValue(patient, out var original))
            return;

        if (TryComp<SpriteComponent>(patient, out var patientSprite))
        {
            _sprite.SetDrawDepth((patient, patientSprite), original);
            if (_lastSpriteOffsets.TryGetValue(patient, out var lastOffset)
                && patientSprite.Offset == lastOffset
                && _appliedSpriteOffsets.TryGetValue(patient, out var appliedOffset))
            {
                _sprite.SetOffset((patient, patientSprite), patientSprite.Offset - appliedOffset);
            }
        }

        _originalDrawDepths.Remove(patient);
        _lastSpriteOffsets.Remove(patient);
        _appliedSpriteOffsets.Remove(patient);
    }
}
