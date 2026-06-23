using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.Parenting
{
    [BurstCompile]
    internal static class DetachTransform
    {
        private const float DeterminantThreshold = 1e-12f;

        internal static bool TryPlanDetach(bool hasParent, Entity runtimeParent, bool hasLocalTransform,
            LocalTransform originalLocalTransform, out Entity plannedParent, out LocalTransform plannedLocalTransform)
        {
            if (!hasParent || !hasLocalTransform)
            {
                plannedParent = Entity.Null;
                plannedLocalTransform = default;
                return false;
            }

            plannedParent = runtimeParent;
            plannedLocalTransform = originalLocalTransform;
            return true;
        }

        internal static LocalTransform ResolveDetachLocalTransform(float4x4 targetWorld, bool hasPostTransform,
            float4x4 postTransform)
        {
            var rigidWorld = targetWorld;
            if (hasPostTransform && math.abs(math.determinant(postTransform)) > DeterminantThreshold)
            {
                var candidate = math.mul(targetWorld, math.inverse(postTransform));
                if (IsFinite(candidate))
                {
                    rigidWorld = candidate;
                }
            }

            return LocalTransform.FromMatrix(rigidWorld);
        }

        internal static bool ShouldReattach(Entity runtimeParent, bool targetExists, bool parentExists)
        {
            return runtimeParent != Entity.Null && targetExists && parentExists;
        }

        private static bool IsFinite(float4x4 m)
        {
            return math.all(math.isfinite(m.c0)) && math.all(math.isfinite(m.c1)) &&
                math.all(math.isfinite(m.c2)) && math.all(math.isfinite(m.c3));
        }
    }
}
