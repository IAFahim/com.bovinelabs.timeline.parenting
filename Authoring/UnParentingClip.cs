using Unity.Transforms;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.Parenting;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Parenting.Authoring
{
    public class UnParentingClip : DOTSClip, ITimelineClipAsset
    {
        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.Looping;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            var parent = (context.Director.GetGenericBinding(context.Track) as GameObject).transform.parent;
            context.Baker.AddComponent(clipEntity, new UnParentComponent
            {
                LastParent = context.Baker.GetEntity(parent, TransformUsageFlags.None),
                OriginalLocalTransform = LocalTransform.Identity
            });
            base.Bake(clipEntity, context);
        }
    }
}
