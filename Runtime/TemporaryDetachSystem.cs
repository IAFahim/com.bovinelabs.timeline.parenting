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
        private const int ExitSortKeyOffset = 1 << 24;

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSystem = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecbWriter = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
            var storageInfoLookup = SystemAPI.GetEntityStorageInfoLookup();

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
                StorageInfoLookup = storageInfoLookup
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new AttachCleanupJob { ECB = ecbWriter }.ScheduleParallel(state.Dependency);
            state.Dependency = new SyncCleanupJob().ScheduleParallel(state.Dependency);
            state.Dependency = new GatherDestroyedJob
            {
                ECB = ecbWriter,
                StorageInfoLookup = storageInfoLookup
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

                var hasParent = ParentLookup.TryGetComponent(target, out var parent);
                var hasLocalTransform = LocalTransformLookup.TryGetComponent(target, out var originalLT);

                if (!DetachTransform.TryPlanDetach(hasParent, parent.Value, hasLocalTransform, originalLT,
                        out var plannedParent, out var plannedLocalTransform))
                {
                    state.RuntimeParent = Entity.Null;
                    return;
                }

                state.RuntimeParent = plannedParent;
                state.OriginalLocalTransform = plannedLocalTransform;

                if (LocalToWorldLookup.TryGetComponent(target, out var targetLtw))
                {
                    var hasPostTransform = PostTransformLookup.TryGetComponent(target, out var ptm);
                    var detachLocalTransform = DetachTransform.ResolveDetachLocalTransform(targetLtw.Value,
                        hasPostTransform, ptm.Value);
                    ECB.SetComponent(indexInQuery, target, detachLocalTransform);
                }

                ECB.RemoveComponent<Parent>(indexInQuery, target);
            }
        }

        [BurstCompile]
        [WithNone(typeof(ClipActive))]
        [WithAll(typeof(ClipActivePrevious))]
        private partial struct ReattachToParentJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ECB;
            [ReadOnly] public EntityStorageInfoLookup StorageInfoLookup;

            private void Execute([EntityIndexInQuery] int indexInQuery, ref DetachFromParentState state,
                in TrackBinding binding)
            {
                var target = binding.Value;

                if (state.RuntimeParent == Entity.Null) return;

                var sortKey = indexInQuery + ExitSortKeyOffset;

                if (DetachTransform.ShouldReattach(state.RuntimeParent, StorageInfoLookup.Exists(target),
                        StorageInfoLookup.Exists(state.RuntimeParent)))
                {
                    ECB.SetComponent(sortKey, target, state.OriginalLocalTransform);
                    ECB.AddComponent(sortKey, target, new Parent { Value = state.RuntimeParent });
                }

                state.RuntimeParent = Entity.Null;
            }
        }

        private struct DetachCleanup : ICleanupComponentData
        {
            public Entity Target;
            public Entity RuntimeParent;
            public LocalTransform OriginalLocalTransform;
        }

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

        [BurstCompile]
        [WithNone(typeof(DetachFromParentState))]
        private partial struct GatherDestroyedJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ECB;
            [ReadOnly] public EntityStorageInfoLookup StorageInfoLookup;

            private void Execute([EntityIndexInQuery] int indexInQuery, Entity clipEntity, in DetachCleanup cleanup)
            {
                if (DetachTransform.ShouldReattach(cleanup.RuntimeParent, StorageInfoLookup.Exists(cleanup.Target),
                        StorageInfoLookup.Exists(cleanup.RuntimeParent)))
                {
                    var sortKey = indexInQuery + ExitSortKeyOffset;
                    ECB.SetComponent(sortKey, cleanup.Target, cleanup.OriginalLocalTransform);
                    ECB.AddComponent(sortKey, cleanup.Target, new Parent { Value = cleanup.RuntimeParent });
                }

                ECB.RemoveComponent<DetachCleanup>(indexInQuery, clipEntity);
            }
        }
    }
}