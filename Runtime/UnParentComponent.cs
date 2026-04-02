using Unity.Entities;
using Unity.Transforms;

namespace BovineLabs.Timeline.Parenting
{
    public struct UnParentComponent : IComponentData
    {
        public Entity LastParent;
        public LocalTransform OriginalLocalTransform;
    }
}
