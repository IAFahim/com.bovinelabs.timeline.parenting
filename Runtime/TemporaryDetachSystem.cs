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

            // Track each detach clip entity's captured parent/pose in an ICleanupComponentData. If the clip entity is
            // destroyed while a detach is still active (director/sub-scene torn down or graph rebuilt before the
            // falling edge fires), ReattachToParentJob never runs and the target stays orphaned at its frozen world
            // pose forever. The cleanup component survives the destroy as a zombie, letting GatherDestroyedJob undo
            // the detach on the still-live target. Mirrors TimelineEssenceStatSystem's cleanup recovery.
            state.Dependency = new AttachCleanupJob { ECB = ecbWriter }.ScheduleParallel(state.Dependency);
            state.Dependency = new SyncCleanupJob().ScheduleParallel(state.Dependency);
            state.Dependency = new GatherDestroyedJob
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

            private void Execute([EntityIndexInQuery] int indexInQuery, ref DetachFromParentState state,
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

                    ECB.SetComponent(indexInQuery, target, LocalTransform.FromMatrix(rigidWorld));
                }

                ECB.RemoveComponent<Parent>(indexInQuery, target);
            }
        }

        [BurstCompile]
        [WithNone(typeof(ClipActive))]
        [WithAll(typeof(ClipActivePrevious))]
        private partial struct ReattachToParentJob : IJobEntity
        {
            // DetachFromParentJob and ReattachToParentJob write into the SAME ECB parallel writer but iterate
            // different queries, so their sortKeys overlap. When clip A reattaches and clip B detaches the same
            // target entity in the same frame, equal sortKeys make the final Parent order-undefined. Offset the
            // reattach sortKey into a disjoint, higher range so reattach commands deterministically sort after the
            // detach commands of that frame. Mirrors EntityLinkParentSystem.ExitJob.ExitSortKeyOffset.
            public const int ExitSortKeyOffset = 1 << 24;

            public EntityCommandBuffer.ParallelWriter ECB;
            [ReadOnly] public EntityStorageInfoLookup StorageInfoLookup;

            private void Execute([EntityIndexInQuery] int indexInQuery, ref DetachFromParentState state,
                in TrackBinding binding)
            {
                var target = binding.Value;

                if (state.RuntimeParent == Entity.Null)
                {
                    return;
                }

                var sortKey = indexInQuery + ExitSortKeyOffset;

                if (StorageInfoLookup.Exists(target) && StorageInfoLookup.Exists(state.RuntimeParent))
                {
                    ECB.SetComponent(sortKey, target, state.OriginalLocalTransform);
                    ECB.AddComponent(sortKey, target, new Parent { Value = state.RuntimeParent });
                }

                state.RuntimeParent = Entity.Null;
            }
        }

        // Lingers on a destroyed detach clip entity as a zombie so the orphaned reparent can be undone from the
        // still-live target. Mirrors DetachFromParentState (RuntimeParent == Entity.Null when nothing is detached).
        private struct DetachCleanup : ICleanupComponentData
        {
            public Entity Target;
            public Entity RuntimeParent;
            public LocalTransform OriginalLocalTransform;
        }

        // Attaches the cleanup marker to every detach clip entity once, seeding it with the current capture so the
        // undo is correct even if the entity is destroyed the same frame the marker is added.
        [BurstCompile]
        [WithNone(typeof(DetachCleanup))]
        private partial struct AttachCleanupJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ECB;

            private void Execute([EntityIndexInQuery] int sortKey, Entity clipEntity, in TrackBinding binding,
                in DetachFromParentState state)
            {
                ECB.AddComponent(sortKey, clipEntity, new DetachCleanup
                {
                    Target = binding.Value,
                    RuntimeParent = state.RuntimeParent,
                    OriginalLocalTransform = state.OriginalLocalTransform
                });
            }
        }

        // Keeps the zombie-surviving marker in sync with the live capture every frame. After a normal reattach
        // clears state.RuntimeParent, this propagates the cleared value so GatherDestroyedJob becomes a no-op.
        [BurstCompile]
        private partial struct SyncCleanupJob : IJobEntity
        {
            private void Execute(in TrackBinding binding, in DetachFromParentState state, ref DetachCleanup cleanup)
            {
                cleanup.Target = binding.Value;
                cleanup.RuntimeParent = state.RuntimeParent;
                cleanup.OriginalLocalTransform = state.OriginalLocalTransform;
            }
        }

        // Runs on clip entities destroyed while a detach was still active: the cleanup marker outlives the entity's
        // IComponentData, so DetachFromParentState is gone. Re-add Parent + restore the captured pose on the still-
        // live target (guarded by Exists), then release the zombie.
        [BurstCompile]
        [WithNone(typeof(DetachFromParentState))]
        private partial struct GatherDestroyedJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ECB;
            [ReadOnly] public EntityStorageInfoLookup StorageInfoLookup;

            private void Execute([EntityIndexInQuery] int indexInQuery, Entity clipEntity, in DetachCleanup cleanup)
            {
                if (cleanup.RuntimeParent != Entity.Null &&
                    StorageInfoLookup.Exists(cleanup.Target) && StorageInfoLookup.Exists(cleanup.RuntimeParent))
                {
                    var sortKey = indexInQuery + ReattachToParentJob.ExitSortKeyOffset;
                    ECB.SetComponent(sortKey, cleanup.Target, cleanup.OriginalLocalTransform);
                    ECB.AddComponent(sortKey, cleanup.Target, new Parent { Value = cleanup.RuntimeParent });
                }

                ECB.RemoveComponent<DetachCleanup>(indexInQuery, clipEntity);
            }
        }
    }
}