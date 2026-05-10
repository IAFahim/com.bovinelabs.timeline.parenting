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
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var ecbWriter = ecb.AsParallelWriter();
            
            state.Dependency = new DetachFromParentJob
            {
                ECB = ecbWriter,
                LocalToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true),
                LocalTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
                ParentLookup = SystemAPI.GetComponentLookup<Parent>(true)
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new ReattachToParentJob
            {
                ECB = ecbWriter
            }.ScheduleParallel(state.Dependency);

            state.Dependency.Complete();
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
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

            private void Execute([ChunkIndexInQuery] int chunkIndex, in DetachFromParentState state, in TrackBinding binding)
            {
                var target = binding.Value;

                if (state.RuntimeParent != Entity.Null)
                {
                    ECB.SetComponent(chunkIndex, target, state.OriginalLocalTransform);
                    ECB.AddComponent(chunkIndex, target, new Parent { Value = state.RuntimeParent });
                }
            }
        }
    }
}