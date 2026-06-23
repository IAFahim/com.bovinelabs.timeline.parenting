using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.Parenting.Tests
{
    public class DetachTransformTests
    {
        private static readonly Entity Parent = new Entity { Index = 7, Version = 3 };

        [Test]
        public void TryPlanDetach_HasParentAndLocalTransform_PlansRuntimeParentAndTransform()
        {
            var original = LocalTransform.FromPositionRotationScale(
                new float3(1f, 2f, 3f), quaternion.RotateY(0.5f), 4f);

            var planned = DetachTransform.TryPlanDetach(true, Parent, true, original,
                out var plannedParent, out var plannedLocalTransform);

            Assert.IsTrue(planned);
            Assert.AreEqual(Parent, plannedParent);
            Assert.AreEqual(original, plannedLocalTransform);
        }

        [Test]
        public void TryPlanDetach_NoParent_FailsWithNullPlan()
        {
            var planned = DetachTransform.TryPlanDetach(false, Parent, true, LocalTransform.Identity,
                out var plannedParent, out var plannedLocalTransform);

            Assert.IsFalse(planned);
            Assert.AreEqual(Entity.Null, plannedParent);
            Assert.AreEqual(default(LocalTransform), plannedLocalTransform);
        }

        [Test]
        public void TryPlanDetach_NoLocalTransform_FailsWithNullPlan()
        {
            var planned = DetachTransform.TryPlanDetach(true, Parent, false, LocalTransform.Identity,
                out var plannedParent, out var plannedLocalTransform);

            Assert.IsFalse(planned);
            Assert.AreEqual(Entity.Null, plannedParent);
            Assert.AreEqual(default(LocalTransform), plannedLocalTransform);
        }

        [Test]
        public void TryPlanDetach_NeitherParentNorLocalTransform_FailsWithNullPlan()
        {
            var planned = DetachTransform.TryPlanDetach(false, Parent, false, LocalTransform.Identity,
                out var plannedParent, out var plannedLocalTransform);

            Assert.IsFalse(planned);
            Assert.AreEqual(Entity.Null, plannedParent);
            Assert.AreEqual(default(LocalTransform), plannedLocalTransform);
        }

        [Test]
        public void ResolveDetachLocalTransform_NoPostTransform_UsesTargetWorld()
        {
            var world = float4x4.TRS(new float3(5f, 6f, 7f), quaternion.RotateX(0.3f), 1f);

            var result = DetachTransform.ResolveDetachLocalTransform(world, false, float4x4.identity);

            var expected = LocalTransform.FromMatrix(world);
            AssertApproximately(expected, result);
        }

        [Test]
        public void ResolveDetachLocalTransform_SingularPostTransform_KeepsTargetWorld()
        {
            var world = float4x4.TRS(new float3(2f, 0f, -1f), quaternion.RotateZ(0.2f), 1f);
            var singular = float4x4.Scale(0f, 1f, 1f);

            var result = DetachTransform.ResolveDetachLocalTransform(world, true, singular);

            var expected = LocalTransform.FromMatrix(world);
            AssertApproximately(expected, result);
        }

        [Test]
        public void ResolveDetachLocalTransform_IdentityPostTransform_EqualsTargetWorld()
        {
            var world = float4x4.TRS(new float3(1f, -2f, 3f), quaternion.RotateY(0.9f), 1f);

            var result = DetachTransform.ResolveDetachLocalTransform(world, true, float4x4.identity);

            var expected = LocalTransform.FromMatrix(world);
            AssertApproximately(expected, result);
        }

        [Test]
        public void ResolveDetachLocalTransform_InvertiblePostTransform_StripsPostTransform()
        {
            var rigid = float4x4.TRS(new float3(4f, 5f, 6f), quaternion.RotateX(0.4f), 1f);
            var postTransform = float4x4.Scale(2f, 3f, 4f);
            var world = math.mul(rigid, postTransform);

            var result = DetachTransform.ResolveDetachLocalTransform(world, true, postTransform);

            var expected = LocalTransform.FromMatrix(rigid);
            AssertApproximately(expected, result);
        }

        [Test]
        public void ShouldReattach_NullRuntimeParent_ReturnsFalse()
        {
            Assert.IsFalse(DetachTransform.ShouldReattach(Entity.Null, true, true));
        }

        [Test]
        public void ShouldReattach_TargetMissing_ReturnsFalse()
        {
            Assert.IsFalse(DetachTransform.ShouldReattach(Parent, false, true));
        }

        [Test]
        public void ShouldReattach_ParentMissing_ReturnsFalse()
        {
            Assert.IsFalse(DetachTransform.ShouldReattach(Parent, true, false));
        }

        [Test]
        public void ShouldReattach_ParentSetAndBothExist_ReturnsTrue()
        {
            Assert.IsTrue(DetachTransform.ShouldReattach(Parent, true, true));
        }

        private static void AssertApproximately(LocalTransform expected, LocalTransform actual)
        {
            Assert.AreEqual(expected.Position.x, actual.Position.x, 1e-4f);
            Assert.AreEqual(expected.Position.y, actual.Position.y, 1e-4f);
            Assert.AreEqual(expected.Position.z, actual.Position.z, 1e-4f);
            Assert.AreEqual(expected.Scale, actual.Scale, 1e-4f);
            Assert.Greater(math.abs(math.dot(expected.Rotation.value, actual.Rotation.value)), 1f - 1e-4f);
        }
    }
}
