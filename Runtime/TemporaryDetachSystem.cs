using BovineLabs.Timeline.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.Parenting
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation)]
    public partial struct TemporaryDetachSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSystem = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecbWriter = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            state.Dependency = new DetachFromParentJob
            {
                ECB = ecbWriter,
                LocalToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true),
                LocalTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
                ParentLookup = SystemAPI.GetComponentLookup<Parent>(true),
                PostTransformLookup = SystemAPI.GetComponentLookup<PostTransformMatrix>(true)
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new ReattachToParentJob
            {
                ECB = ecbWriter,
                StorageInfoLookup = SystemAPI.GetEntityStorageInfoLookup()
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        [WithNone(typeof(ClipActivePrevious))]
        private partial struct DetachFromParentJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ECB;
            [ReadOnly] public ComponentLookup<LocalToWorld> LocalToWorldLookup;
            [ReadOnly] public ComponentLookup<LocalTransform> LocalTransformLookup;
            [ReadOnly] public ComponentLookup<Parent> ParentLookup;
            [ReadOnly] public ComponentLookup<PostTransformMatrix> PostTransformLookup;

            private void Execute([ChunkIndexInQuery] int chunkIndex, ref DetachFromParentState state,
                in TrackBinding binding)
            {
                var target = binding.Value;

                if (!ParentLookup.TryGetComponent(target, out var parent))
                {
                    state.RuntimeParent = Entity.Null;
                    return;
                }

                if (!LocalTransformLookup.TryGetComponent(target, out var originalLT))
                {
                    state.RuntimeParent = Entity.Null;
                    return;
                }

                state.RuntimeParent = parent.Value;
                state.OriginalLocalTransform = originalLT;

                if (LocalToWorldLookup.TryGetComponent(target, out var targetLtw))
                {
                    // A non-uniform scale / shear lives in PostTransformMatrix (LocalToWorld = local * PTM).
                    // FromMatrix collapses non-uniform scale to a single max-axis value, so strip the PTM
                    // before decomposing and leave the PTM untouched — the detached world pose, including
                    // non-uniform scale, is preserved instead of snapping to a wrong uniform size.
                    var rigidWorld = targetLtw.Value;
                    if (PostTransformLookup.TryGetComponent(target, out var ptm) &&
                        math.abs(math.determinant(ptm.Value)) > 1e-12f)
                    {
                        // Only strip an INVERTIBLE PostTransformMatrix. A degenerate (zero/near-zero axis)
                        // scale gives a singular matrix whose inverse is inf/NaN; falling back to the raw
                        // LocalToWorld keeps a finite (max-axis-collapsed) pose instead of teleporting to NaN.
                        var candidate = math.mul(targetLtw.Value, math.inverse(ptm.Value));
                        if (math.all(math.isfinite(candidate.c0)) && math.all(math.isfinite(candidate.c1)) &&
                            math.all(math.isfinite(candidate.c2)) && math.all(math.isfinite(candidate.c3)))
                            rigidWorld = candidate;
                    }

                    ECB.SetComponent(chunkIndex, target, LocalTransform.FromMatrix(rigidWorld));
                }

                ECB.RemoveComponent<Parent>(chunkIndex, target);
            }
        }

        [BurstCompile]
        [WithNone(typeof(ClipActive))]
        [WithAll(typeof(ClipActivePrevious))]
        private partial struct ReattachToParentJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ECB;
            [ReadOnly] public EntityStorageInfoLookup StorageInfoLookup;

            private void Execute([ChunkIndexInQuery] int chunkIndex, ref DetachFromParentState state,
                in TrackBinding binding)
            {
                var target = binding.Value;

                if (state.RuntimeParent == Entity.Null)
                {
                    return;
                }

                if (StorageInfoLookup.Exists(target) && StorageInfoLookup.Exists(state.RuntimeParent))
                {
                    ECB.SetComponent(chunkIndex, target, state.OriginalLocalTransform);
                    ECB.AddComponent(chunkIndex, target, new Parent { Value = state.RuntimeParent });
                }

                state.RuntimeParent = Entity.Null;
            }
        }
    }
}