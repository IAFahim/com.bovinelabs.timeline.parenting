using BovineLabs.Timeline.Core;
using BovineLabs.Timeline.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace BovineLabs.Timeline.Parenting
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    public partial struct TemporaryDetachSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
            
            state.Dependency = new DetachFromParentJob
            {
                ECB = ecb,
                LocalToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true),
                LocalTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
                ParentLookup = SystemAPI.GetComponentLookup<Parent>(true)
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new ReattachToParentJob
            {
                ECB = ecb
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

            private void Execute([ChunkIndexInQuery] int chunkIndex, ref DetachFromParentState state, in TrackBinding binding)
            {
                var target = binding.Value;
                if (!ParentLookup.TryGetComponent(target, out var parent))
                {
                    state.RuntimeParent = Entity.Null;
                    return;
                }
                state.RuntimeParent = parent.Value;
                if (LocalTransformLookup.TryGetComponent(target, out var originalLT))
                {
                    state.OriginalLocalTransform = originalLT;
                }
                if (LocalToWorldLookup.TryGetComponent(target, out var targetLtw))
                {
                    targetLtw.Value.ExtractLocalTransform(out var worldTransform);
                    ECB.SetComponent(chunkIndex, target, worldTransform);
                }
                ECB.RemoveComponent<Parent>(chunkIndex, target);
                ECB.RemoveComponent<PreviousParent>(chunkIndex, target);
            }
        }

        [BurstCompile]
        [WithNone(typeof(ClipActive))]
        [WithAll(typeof(ClipActivePrevious))]
        private partial struct ReattachToParentJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ECB;

            private void Execute([ChunkIndexInQuery] int chunkIndex, in DetachFromParentState state, in TrackBinding binding)
            {
                var target = binding.Value;
                ECB.SetComponent(chunkIndex, target, state.OriginalLocalTransform);
                ECB.AddComponent(chunkIndex, target, new Parent { Value = state.RuntimeParent });
            }
        }
    }
}