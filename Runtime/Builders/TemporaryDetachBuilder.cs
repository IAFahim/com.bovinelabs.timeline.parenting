using BovineLabs.Core.EntityCommands;
using Unity.Entities;
using Unity.Transforms;

namespace BovineLabs.Timeline.Parenting.Builders
{
    public struct TemporaryDetachBuilder
    {
        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new DetachFromParentState
            {
                RuntimeParent = Entity.Null,
                OriginalLocalTransform = LocalTransform.Identity
            });
        }
    }
}
