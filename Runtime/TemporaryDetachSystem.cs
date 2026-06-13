using BovineLabs.Timeline.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
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
                ParentLookup = SystemAPI.GetComponentLookup<Parent>(true)
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
                    var worldTransform = LocalTransform.FromMatrix(targetLtw.Value);
                    ECB.SetComponent(chunkIndex, target, worldTransform);
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