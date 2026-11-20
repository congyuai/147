using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Dustline.Tests
{
    public sealed class SurfaceAudioTests
    {
        [Test]
        public void StepCadenceUsesDistanceAndDiscardsAirborneProgress()
        {
            var steps = new StepCadence();
            Assert.That(steps.Advance(0.5f, true, 1), Is.False);
            Assert.That(steps.Advance(0, true, 1), Is.False);
            Assert.That(steps.Advance(8, false, 1), Is.False);
            Assert.That(steps.Advance(0.75f, true, 1), Is.False);
            Assert.That(steps.Advance(0.5f, true, 1), Is.True);
            Assert.That(steps.Advance(0.75f, true, 1), Is.True);
        }

        [UnityTest]
        public IEnumerator FootstepQueryReadsTheUpperSurfaceAndIgnoresTriggers()
        {
            var testRoot = new GameObject("Footstep test root");
            testRoot.transform.position = new Vector3(1000, 0, 1000);
            try
            {
                Floor(testRoot.transform, "Base", Vector3.down * 0.5f, new Vector3(15, 1, 5));
                var steel = Floor(testRoot.transform, "Plate", new Vector3(-2, 0.025f, 0), new Vector3(1, 0.05f, 3));
                steel.gameObject.AddComponent<FootstepSurface>().Kind = SurfaceKind.Metal;
                var wood = Floor(testRoot.transform, "Board", new Vector3(2, 0.025f, 0), new Vector3(1, 0.05f, 3));
                wood.gameObject.AddComponent<FootstepSurface>().Kind = SurfaceKind.Wood;
                Floor(testRoot.transform, "Trigger", new Vector3(2, 0.12f, 0), new Vector3(0.8f, 0.04f, 1)).isTrigger = true;
                Physics.SyncTransforms();
                Assert.That(FootstepSurface.Underfoot(testRoot.transform.position + Vector3.left * 2), Is.EqualTo(SurfaceKind.Metal));
                Assert.That(FootstepSurface.Underfoot(testRoot.transform.position + Vector3.right * 2), Is.EqualTo(SurfaceKind.Wood));
                Assert.That(FootstepSurface.Underfoot(testRoot.transform.position), Is.EqualTo(SurfaceKind.Concrete));
            }
            finally { Object.Destroy(testRoot); }
            yield return null;
        }

        static BoxCollider Floor(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var floor = new GameObject(name);
            floor.transform.SetParent(parent, false);
            floor.transform.localPosition = position;
            var collider = floor.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }
    }
}
