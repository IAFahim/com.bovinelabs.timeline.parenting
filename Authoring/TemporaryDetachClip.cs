using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.Parenting.Builders;
using Unity.Entities;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Parenting.Authoring
{
    public class TemporaryDetachClip : DOTSClip, ITimelineClipAsset
    {
        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.Looping;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            var builder = new TemporaryDetachBuilder();
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);
            base.Bake(clipEntity, context);
        }
    }
}