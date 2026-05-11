using System;
using System.ComponentModel;
using BovineLabs.Timeline.Authoring;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Parenting.Authoring
{
    [Serializable]
    [TrackClipType(typeof(TemporaryDetachClip))]
    [TrackColor(0.1f, 0.6f, 0.6f)]
    [TrackBindingType(typeof(GameObject))]
    [DisplayName("BovineLabs/Timeline/" + nameof(TemporaryDetachTrack))]
    public class TemporaryDetachTrack : DOTSTrack
    {
    }
}