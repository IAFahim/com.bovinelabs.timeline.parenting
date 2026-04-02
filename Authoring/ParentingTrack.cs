using BovineLabs.Timeline.Authoring;
using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Parenting.Authoring
{
    [Serializable]
    [TrackClipType(typeof(UnParentingClip))]
    [TrackColor(0.1f, 0.6f, 0.6f)]
    [TrackBindingType(typeof(GameObject))]
    [DisplayName("BovineLabs/Timeline/Transform/Parenting (DOTS)")]
    public class ParentingTrack : DOTSTrack
    {
    }
}
