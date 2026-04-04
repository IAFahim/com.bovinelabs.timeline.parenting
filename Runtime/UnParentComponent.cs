using Unity.Entities;
using Unity.Transforms;

namespace BovineLabs.Timeline.Parenting
{
    public struct DetachFromParentState : IComponentData
    {
        public Entity RuntimeParent;
        public LocalTransform OriginalLocalTransform;
    }
}