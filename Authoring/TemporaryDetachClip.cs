using BovineLabs.Timeline.Authoring;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Parenting.Authoring
{
    public class TemporaryDetachClip : DOTSClip, ITimelineClipAsset
    {
        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.Looping;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            // We do NOT guess the parent here anymore. 
            // We just add an empty state to hold the runtime data.
            context.Baker.AddComponent(clipEntity, new DetachFromParentState
            {
                RuntimeParent = Entity.Null,
                OriginalLocalTransform = LocalTransform.Identity
            });

            base.Bake(clipEntity, context);
        }
    }
}