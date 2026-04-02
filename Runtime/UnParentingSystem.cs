using BovineLabs.Timeline.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.Parenting;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace BovineLabs.Timeline.Parenting
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    public partial struct UnParentingSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UnParentComponent>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            var localToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true);
            var localTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var parentLookup = SystemAPI.GetComponentLookup<Parent>(true);

            var unparentJob = new UnparentJob
            {
                ECB = ecb,
                LocalToWorldLookup = localToWorldLookup,
                LocalTransformLookup = localTransformLookup,
                ParentLookup = parentLookup
            };
            state.Dependency = unparentJob.ScheduleParallel(state.Dependency);

            var reparentJob = new ReparentJob
            {
                ECB = ecb
            };
            state.Dependency = reparentJob.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        [WithNone(typeof(ClipActivePrevious))]
        private partial struct UnparentJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ECB;
            [ReadOnly] public ComponentLookup<LocalToWorld> LocalToWorldLookup;
            [ReadOnly] public ComponentLookup<LocalTransform> LocalTransformLookup;
            [ReadOnly] public ComponentLookup<Parent> ParentLookup;

            private void Execute([ChunkIndexInQuery] int chunkIndex, ref UnParentComponent unparent,
                in TrackBinding binding)
            {
                var target = binding.Value;

                if (!ParentLookup.HasComponent(target)) return;

                if (LocalTransformLookup.TryGetComponent(target, out var originalLT))
                    unparent.OriginalLocalTransform = originalLT;

                if (LocalToWorldLookup.TryGetComponent(target, out var targetLtw))
                {
                    targetLtw.Value.ExtractLocalTransform(out var localTransform);
                    ECB.SetComponent(chunkIndex, target, localTransform);
                }

                ECB.RemoveComponent<Parent>(chunkIndex, target);
                ECB.RemoveComponent<PreviousParent>(chunkIndex, target);
            }
        }

        [BurstCompile]
        [WithNone(typeof(ClipActive))]
        [WithAll(typeof(ClipActivePrevious))]
        private partial struct ReparentJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ECB;

            private void Execute([ChunkIndexInQuery] int chunkIndex, in UnParentComponent unparent,
                in TrackBinding binding)
            {
                var target = binding.Value;
                var parent = unparent.LastParent;

                if (parent == Entity.Null) return;

                ECB.SetComponent(chunkIndex, target, unparent.OriginalLocalTransform);

                ECB.AddComponent(chunkIndex, target, new Parent { Value = parent });
            }
        }
    }
}
