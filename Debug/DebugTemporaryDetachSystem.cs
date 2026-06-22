#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Quill;
using BovineLabs.Timeline.Core.Debug;
using BovineLabs.Timeline.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace BovineLabs.Timeline.Parenting.Debug
{
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct DebugTemporaryDetachSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DrawSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!TimelineDebugUtility.TryGetDrawer<DebugTemporaryDetachSystem>(
                    ref state, true, out var drawer, out var viewer, out var hasViewer))
                return;

            state.Dependency = new DebugDrawJob
            {
                Drawer = drawer,
                Viewer = viewer,
                HasViewer = hasViewer,
                LtwLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true)
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct DebugDrawJob : IJobEntity
        {
            public Drawer Drawer;
            public float3 Viewer;
            public bool HasViewer;
            [ReadOnly] public ComponentLookup<LocalToWorld> LtwLookup;

            private void Execute(in DetachFromParentState state, in TrackBinding binding)
            {
                if (state.RuntimeParent == Entity.Null) return;

                var target = binding.Value;
                var parent = state.RuntimeParent;

                if (
                    !LtwLookup.TryGetComponent(target, out var targetLtw) ||
                    !LtwLookup.TryGetComponent(parent, out var parentLtw)
                ) return;

                var tier = TimelineDebugTier.Resolve(targetLtw.Position, Viewer, HasViewer);

                Drawer.Line(targetLtw.Position, parentLtw.Position, Color.cyan);
                Drawer.Point(targetLtw.Position, 0.05f, Color.cyan);

                if (tier >= DebugTier.Mid)
                {
                    Drawer.Point(parentLtw.Position, 0.05f, TimelineDebugColors.OwnerLink);
                    var text = new FixedString32Bytes("Detached");
                    Drawer.Text32(targetLtw.Position + new float3(0, 0.2f, 0), text, Color.cyan, 12f);
                }

                if (tier == DebugTier.Close)
                {
                    var readout = new FixedString128Bytes();
                    readout.Append((FixedString32Bytes)"parent #");
                    readout.Append(parent.Index);
                    readout.Append((FixedString32Bytes)"  dist ");
                    readout.Append(math.distance(targetLtw.Position, parentLtw.Position));
                    readout.Append((FixedString32Bytes)"m");
                    Drawer.Text128(targetLtw.Position + new float3(0, 0.45f, 0), readout,
                        TimelineDebugColors.Label, 11f);
                }
            }
        }
    }
}
#endif