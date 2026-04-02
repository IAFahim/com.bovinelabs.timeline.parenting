using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Parenting.Authoring
{
    [Serializable]
    public class UnParentingBehaviour : PlayableBehaviour
    {
    }

    [Serializable]
    public class UnParentingClipNonDots : PlayableAsset, ITimelineClipAsset
    {
        public UnParentingBehaviour template = new();

        // Matches your ECS version's clip capabilities
        public ClipCaps clipCaps => ClipCaps.Looping;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<UnParentingBehaviour>.Create(graph, template);
        }
    }
}
