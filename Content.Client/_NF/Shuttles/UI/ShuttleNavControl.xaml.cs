// New Frontiers - This file is licensed under AGPLv3
// Copyright (c) 2024 New Frontiers Contributors
// See AGPLv3.txt for details.
using Content.Client.Graphics;
using Content.Shared._NF.Shuttles.Events;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Physics.Components;
using System.Numerics;
using System.Runtime.InteropServices;
using Content.Shared._Mono.Company;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client.Shuttles.UI
{
    public partial class ShuttleNavControl // Mono
    {
        private readonly Dictionary<Color, List<Vector2>> _blipVerticesByColor = new();

        public InertiaDampeningMode DampeningMode { get; set; }

        /// <summary>
        /// Whether the shuttle is currently in FTL. This is used to disable the Park button
        /// while in FTL to prevent parking while traveling.
        /// </summary>
        public bool InFtl { get; set; }

        private void NfUpdateState(NavInterfaceState state)
        {

            if (!EntManager.GetCoordinates(state.Coordinates).HasValue ||
                !EntManager.TryGetComponent(EntManager.GetCoordinates(state.Coordinates).GetValueOrDefault().EntityId,out TransformComponent? transform) ||
                !EntManager.TryGetComponent(transform.GridUid, out PhysicsComponent? physicsComponent))
            {
                return;
            }

            DampeningMode = state.DampeningMode;

            // Check if the entity has an FTLComponent which indicates it's in FTL
            if (transform.GridUid != null)
            {
                InFtl = EntManager.HasComponent<FTLComponent>(transform.GridUid);
            }
            else
            {
                InFtl = false;
            }
        }

        // New Frontiers - Maximum IFF Distance - checks distance to object, draws if closer than max range
        // This code is licensed under AGPLv3. See AGPLv3.txt
        private bool NfCheckShouldDrawIffRangeCondition(bool shouldDrawIff, Vector2 distance)
        {
            if (shouldDrawIff && MaximumIFFDistance >= 0.0f)
            {
                if (distance.Length() > MaximumIFFDistance)
                {
                    shouldDrawIff = false;
                }
            }

            return shouldDrawIff;
        }

        private static void NfAddBlipToList(List<BlipData> blipDataList, bool isOutsideRadarCircle, Vector2 uiPosition, int uiXCentre, int uiYCentre, Color color)
        {
            blipDataList.Add(new BlipData
            {
                IsOutsideRadarCircle = isOutsideRadarCircle,
                UiPosition = uiPosition,
                VectorToPosition = uiPosition - new Vector2(uiXCentre, uiYCentre),
                Color = color
            });
        }

        private static void NfAddBlipToList(List<BlipData> blipDataList, bool isOutsideRadarCircle, Vector2 uiPosition, int uiXCentre, int uiYCentre, Color color, EntityUid gridUid = default)
        {
            // Check if the entity has a company component and use that color if available
            Color blipColor = color;

            if (gridUid != default &&
                IoCManager.Resolve<IEntityManager>().TryGetComponent(gridUid, out Shared._Mono.Company.CompanyComponent? companyComp) &&
                !string.IsNullOrEmpty(companyComp.CompanyName))
            {
                var prototypeManager = IoCManager.Resolve<IPrototypeManager>();
                if (prototypeManager.TryIndex<CompanyPrototype>(companyComp.CompanyName, out var prototype) && prototype != null)
                {
                    blipColor = prototype.Color;
                }
            }

            blipDataList.Add(new BlipData
            {
                IsOutsideRadarCircle = isOutsideRadarCircle,
                UiPosition = uiPosition,
                VectorToPosition = uiPosition - new Vector2(uiXCentre, uiYCentre),
                Color = blipColor
            });
        }

        /**
         * Frontier - Adds blip style triangles that are on ships or pointing towards ships on the edges of the radar.
         * Draws blips at the BlipData's uiPosition and uses VectorToPosition to rotate to point towards ships.
         */
        private void NfDrawBlips(DrawingHandleBase handle, List<BlipData> blipDataList)
        {
            foreach (var vertices in _blipVerticesByColor.Values)
            {
                vertices.Clear();
            }

            var halfWidth = RadarBlipSize * 0.5f * UIScale;
            var thirdHeight = RadarBlipSize / 3f * UIScale;
            var twoThirdsHeight = RadarBlipSize * 2f / 3f * UIScale;

            foreach (var blipData in blipDataList)
            {
                var first = new Vector2(-halfWidth, -thirdHeight);
                var second = new Vector2(halfWidth, -thirdHeight);
                var third = new Vector2(0f, twoThirdsHeight);

                if (blipData.IsOutsideRadarCircle)
                {
                    var angle = (float) Math.Atan2(blipData.VectorToPosition.Y, blipData.VectorToPosition.X) + -1.6f;
                    var cos = (float) Math.Cos(angle);
                    var sin = (float) Math.Sin(angle);
                    first = RotateBlipVertex(first, cos, sin);
                    second = RotateBlipVertex(second, cos, sin);
                    third = RotateBlipVertex(third, cos, sin);
                }

                if (!_blipVerticesByColor.TryGetValue(blipData.Color, out var vertices))
                {
                    vertices = new List<Vector2>(12);
                    _blipVerticesByColor.Add(blipData.Color, vertices);
                }

                var center = blipData.UiPosition * UIScale;
                vertices.Add(center + first);
                vertices.Add(center + second);
                vertices.Add(center + third);
            }

            // One draw call for every color we have
            foreach (var (color, vertices) in _blipVerticesByColor)
            {
                handle.DrawPrimitivesBatched(DrawPrimitiveTopology.TriangleList, CollectionsMarshal.AsSpan(vertices), color);
            }
        }

        private static Vector2 RotateBlipVertex(Vector2 vertex, float cos, float sin)
        {
            return new Vector2(vertex.X * cos - vertex.Y * sin, vertex.X * sin + vertex.Y * cos);
        }
    }
}
