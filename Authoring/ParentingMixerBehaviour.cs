using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Parenting.Authoring
{
    public class ParentingMixerBehaviour : PlayableBehaviour
    {
        private bool isUnparented;

        // We store the original local transform to mimic your ECS behavior (UnParentComponent.OriginalLocalTransform)
        private Vector3 originalLocalPosition;
        private Quaternion originalLocalRotation;
        private Vector3 originalLocalScale;
        private Transform originalParent;
        private Transform targetTransform;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var go = playerData as GameObject;
            if (go == null) return;

            var target = go.transform;

            var inputCount = playable.GetInputCount();
            var shouldBeUnparented = false;

            // Check if the playhead is currently over any active UnParentingClip
            for (var i = 0; i < inputCount; i++)
            {
                var inputWeight = playable.GetInputWeight(i);
                if (inputWeight > 0f)
                {
                    shouldBeUnparented = true;
                    break;
                }
            }

            // Handle State Change: Unparent
            if (shouldBeUnparented && !isUnparented)
            {
                targetTransform = target;
                originalParent = target.parent;
                originalLocalPosition = target.localPosition;
                originalLocalRotation = target.localRotation;
                originalLocalScale = target.localScale;

                // Unparent it while keeping its current world position
                target.SetParent(null, true);
                isUnparented = true;
            }
            // Handle State Change: Reparent
            else if (!shouldBeUnparented && isUnparented)
            {
                RestoreParent();
            }
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            // Safety catch to ensure the object is properly restored when the timeline stops
            RestoreParent();
        }

        private void RestoreParent()
        {
            if (isUnparented && targetTransform != null)
            {
                // False ensures we can manually re-apply the original local transform exactly as it was
                targetTransform.SetParent(originalParent, false);

                // Snap back to the exact relative position it had before the clip
                targetTransform.localPosition = originalLocalPosition;
                targetTransform.localRotation = originalLocalRotation;
                targetTransform.localScale = originalLocalScale;

                isUnparented = false;
            }
        }
    }
}
