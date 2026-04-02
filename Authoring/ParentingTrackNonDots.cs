using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Parenting.Authoring
{
    [TrackColor(0.1f, 0.6f, 0.6f)]
    [TrackBindingType(typeof(GameObject))]
    [TrackClipType(typeof(UnParentingClipNonDots))]
    [DisplayName("BovineLabs/Timeline/Transform/Parenting (Non-DOTS)")]
    public class ParentingTrackNonDots : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<ParentingMixerBehaviour>.Create(graph, inputCount);
        }
    }
}
