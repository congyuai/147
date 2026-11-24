using NUnit.Framework;
using UnityEngine;

namespace Dustline.Tests
{
    public sealed class HarborSwimmingTests
    {
        const float Sea = -1.4f, Height = 1.8f;

        [TestCase(-334f, -44f, 2.55f)]
        [TestCase(-40f, -17f, 5.55f)]
        [TestCase(221f, -45f, 3.75f)]
        public void BridgeDeckAndWaterBeneathHaveDistinctModes(float x, float z, float deck)
        {
            var water = new HarborSwimming();
            water.Update(true, new Vector3(x, deck, z), Height, true);
            Assert.That(water.Mode, Is.EqualTo(HarborWaterMode.Dry));
            water.Update(true, new Vector3(x, Sea + .1f, z), Height, false);
            Assert.That(water.Mode, Is.EqualTo(HarborWaterMode.Dry));
            water.Update(true, new Vector3(x, Sea - .18f, z), Height, false);
            Assert.That(water.IsWading, Is.True);
            water.Update(true, new Vector3(x, Sea - 1.2f, z), Height, false);
            Assert.That(water.Mode, Is.EqualTo(HarborWaterMode.Swimming));
            water.Update(true, new Vector3(x, Sea - 2.2f, z), Height, false);
            Assert.That(water.IsSwimming && water.IsSubmerged, Is.True);
            water.Update(false, new Vector3(x, Sea - 2.2f, z), Height, false);
            Assert.That(water.Mode, Is.EqualTo(HarborWaterMode.Dry));
        }

        [TestCase(-1)]
        [TestCase(1)]
        public void ShallowBankLimitsSwimmingDespiteFootDepth(int side)
        {
            var water = new HarborSwimming();
            var feet = new Vector3(-137f, Sea - 1.3f, -35f);
            water.Update(true, feet, Height, false);
            Assert.That(water.IsSwimming, Is.True);
            feet.z += side * 17.5f;
            water.Update(true, feet, Height, true);
            Assert.That(water.IsWading, Is.True);
            Assert.That(water.VerticalVelocity(feet, Height, .02f, true, false, -2f), Is.EqualTo(-2f));
            feet.z = -35f + side * 26f;
            feet.y = Sea + 1f;
            water.Update(true, feet, Height, true);
            Assert.That(water.Mode, Is.EqualTo(HarborWaterMode.Dry));
        }

        [Test]
        public void DiveAndRiseCrossEyeLevelAndReleaseSettlesNearTheSurface()
        {
            const float dt = .02f;
            var water = new HarborSwimming();
            var feet = new Vector3(-137f, Sea - 1.25f, -35f);
            float prior = 0;
            Step(36, false, true);
            Assert.That(water.IsSubmerged, Is.True);
            Step(120, true, false);
            Assert.That(water.Mode, Is.EqualTo(HarborWaterMode.Swimming));
            float raised = feet.y;
            Step(120, false, false);
            Assert.That(water.Mode, Is.EqualTo(HarborWaterMode.Swimming));
            Assert.That(feet.y, Is.LessThan(raised - .08f));
            Assert.That(feet.y, Is.InRange(Sea - Height * .80f, Sea - Height * .56f));
            Assert.That(Mathf.Abs(prior), Is.LessThan(.02f));

            void Step(int count, bool up, bool down)
            {
                for (int i = 0; i < count; i++)
                {
                    water.Update(true, feet, Height, false);
                    Assert.That(water.IsSwimming, Is.True);
                    prior = water.VerticalVelocity(feet, Height, dt, up, down, prior);
                    feet.y += prior * dt;
                }
                water.Update(true, feet, Height, false);
            }
        }

        [Test]
        public void ShorelineRastersUseDistinct2KKeysFromTheOriginalCache()
        {
            Assert.That(PortRaster.Resolution, Is.EqualTo(2048));
            Assert.That(PortRaster.Chart, Is.Not.EqualTo("Dustline/Maps/NorthCape_2k"));
            Assert.That(PortRaster.Ground, Is.Not.EqualTo("Dustline/Maps/NorthCapeGround_2k"));
            Assert.That(PortRaster.Chart, Is.Not.EqualTo(PortRaster.Ground));
        }
    }
}
