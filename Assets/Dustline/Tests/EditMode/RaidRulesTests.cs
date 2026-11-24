using NUnit.Framework;
using UnityEngine;

namespace Dustline.Tests
{
    public sealed class RaidRulesTests
    {
        [TestCase(30, true, 120.0, 30)]
        [TestCase(90, true, 60.0, 90)]
        [TestCase(120, true, 60.0, 120)]
        [TestCase(120, true, 0.0, 120)]
        [TestCase(120, true, double.NaN, 120)]
        [TestCase(90, true, 120.0, 90)]
        [TestCase(120, true, 119.88, 120)]
        [TestCase(-1, true, 144.0, 144)]
        [TestCase(-1, true, 0.0, 60)]
        [TestCase(-1, false, 60.0, -1)]
        [TestCase(75, false, 60.0, 60)]
        public void FixedFrameTargetsIgnoreTransientRefreshAndMobileUncappedFollowsIt(int requested, bool mobile, double refresh, int expected)
        {
            Assert.That(RaidFrameRate.Target(requested, mobile, refresh), Is.EqualTo(expected));
        }

        [TestCase(0f, 0f, false, 0f)]
        [TestCase(0f, .12f, false, 0f)]
        [TestCase(0f, .13f, false, 1f)]
        [TestCase(.8f, .6f, false, 1f)]
        [TestCase(0f, 1.5f, false, 1f)]
        [TestCase(0f, 0f, true, 1f)]
        public void TouchWalkingUsesDirectionOnlyOutsideTheDeadZone(float x, float y, bool locked, float magnitude)
        {
            Vector2 raw = new Vector2(x, y);
            Vector2 move = PlayerInputReader.TouchWalk(raw, locked);
            Assert.That(move.magnitude, Is.EqualTo(magnitude).Within(.0001f));
            if (magnitude > 0f && !locked) Assert.That(Vector2.Dot(move, raw.normalized), Is.EqualTo(1f).Within(.0001f));
            if (locked) Assert.That(move, Is.EqualTo(Vector2.up));
        }

        [TestCase(LeanMode.Toggle)]
        [TestCase(LeanMode.Hybrid)]
        public void LeanIsExclusiveAndTappingTheLatchedSideAgainCenters(LeanMode mode)
        {
            var lean = new LeanGesture();
            lean.Set(-1, true, mode, 1.0);
            Assert.That(lean.Active(mode), Is.EqualTo(-1));
            lean.Set(-1, false, mode, 1.1);
            Assert.That(lean.Active(mode), Is.EqualTo(-1));
            lean.Set(1, true, mode, 1.2);
            lean.Set(-1, false, mode, 1.25);
            Assert.That(lean.Active(mode), Is.EqualTo(1));
            lean.Set(1, false, mode, 1.3);
            Assert.That(lean.Active(mode), Is.EqualTo(1));
            lean.Set(1, true, mode, 1.4);
            lean.Set(1, false, mode, 1.5);
            Assert.That(lean.Active(mode), Is.Zero);
        }

        [Test]
        public void HybridLongLeanAndCancellationReturnToCenter()
        {
            var lean = new LeanGesture();
            lean.Set(-1, true, LeanMode.Hybrid, 2.0);
            lean.Set(-1, false, LeanMode.Hybrid, 2.1);
            lean.Set(1, true, LeanMode.Hybrid, 3.0);
            Assert.That(lean.Active(LeanMode.Hybrid), Is.EqualTo(1));
            lean.Set(1, false, LeanMode.Hybrid, 3.4);
            Assert.That(lean.Active(LeanMode.Hybrid), Is.Zero);
            lean.Set(-1, true, LeanMode.Hybrid, 4.0);
            lean.Cancel(-1);
            lean.Set(-1, false, LeanMode.Hybrid, 4.05);
            Assert.That(lean.Active(LeanMode.Hybrid), Is.Zero);
        }

        [Test]
        public void SpreadIncreasesAcrossPosturesAndProjectsToTheCameraPlane()
        {
            const float hip = .020f;
            var crouch = FieldHandling.Posture(true, false, false, 0f);
            var prone = FieldHandling.Posture(true, false, false, 1.25f);
            float low = FieldHandling.Spread(hip, 1f, crouch, 0f);
            Assert.That(crouch, Is.EqualTo(ShotPosture.Low));
            Assert.That(FieldHandling.Spread(hip, 1f, prone, 1f), Is.EqualTo(low));
            Assert.That(FieldHandling.Posture(true, true, false, 0f), Is.EqualTo(ShotPosture.Airborne));
            Assert.That(FieldHandling.Posture(false, false, false, 0f), Is.EqualTo(ShotPosture.Standing));
            Assert.That(FieldHandling.Posture(false, false, false, .2f), Is.EqualTo(ShotPosture.Walking));
            Assert.That(FieldHandling.Posture(false, false, true, 5f), Is.EqualTo(ShotPosture.Running));
            var order = new[] { ShotPosture.Low, ShotPosture.Standing, ShotPosture.Walking, ShotPosture.Running, ShotPosture.Airborne };
            var viewport = new Rect(19f, 27f, 1600f, 800f);
            var projection = Matrix4x4.identity;
            projection.m00 = .75f;
            projection.m11 = 1.5f;
            float previousMax = 0;
            foreach (var posture in order)
            {
                float min = FieldHandling.Spread(hip, 1f, posture, 0f);
                float max = FieldHandling.Spread(hip, 1f, posture, 1f);
                Assert.That(min, Is.GreaterThan(previousMax));
                var radius = FieldHandling.ScreenRadius(max, viewport, projection);
                Assert.That(radius.x, Is.EqualTo(600f * max).Within(.0001f));
                Assert.That(radius.y, Is.EqualTo(radius.x).Within(.0001f));
                Assert.That(FieldHandling.Spread(.002f, 1f, posture, 1f), Is.LessThan(min));
                previousMax = max;
            }
        }

        [Test]
        public void AutomaticRecoilBuildsAcrossShotsThenRecoversAfterRelease()
        {
            var recoil = new ViewRecoil();
            const float interval = .13f;
            for (int shot = 0; shot < 8; shot++)
            {
                float before = recoil.Offset.x;
                recoil.Add(shot * interval, interval, 1.2f);
                Assert.That(recoil.Offset.x - before, Is.EqualTo(1.2f).Within(.0001f));
                recoil.Step((shot + 1) * interval, interval);
                Assert.That(recoil.Offset.x - before, Is.EqualTo(1.2f).Within(.0001f));
                Assert.That(Mathf.Abs(recoil.Offset.y), Is.LessThanOrEqualTo(2f));
            }
            for (int frame = 0; frame < 90; frame++) recoil.Step(1.04f + frame * .02f, .02f);
            Assert.That(recoil.Offset, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void ExtractionRequiresContinuousPresence()
        {
            var timer = new ExtractionTimer(8);
            Assert.That(timer.Step(true, true, 7.5f), Is.False);
            Assert.That(timer.Step(false, true, 1), Is.False);
            Assert.That(timer.Elapsed, Is.Zero);
            Assert.That(timer.Step(true, true, 7), Is.False);
            Assert.That(timer.Step(true, true, 1), Is.True);
            Assert.That(timer.Remaining, Is.Zero);
        }

        [Test]
        public void DeadPlayerCannotExtract()
        {
            var timer = new ExtractionTimer(8);
            timer.Step(true, true, 7.9f);
            Assert.That(timer.Step(true, false, 5), Is.False);
            Assert.That(timer.Elapsed, Is.Zero);
        }

        [Test]
        public void MagazineCannotFireDuringReloadOrWhenEmpty()
        {
            var magazine = new Magazine(20, 1);
            Assert.That(magazine.Fire(), Is.True);
            Assert.That(magazine.Fire(), Is.False);
            Assert.That(magazine.BeginReload(30, 2), Is.True);
            Assert.That(magazine.Fire(), Is.False);
            Assert.That(magazine.Step(1.9f, 30), Is.Zero);
            Assert.That(magazine.Step(0.2f, 30), Is.EqualTo(20));
            Assert.That(magazine.Rounds, Is.EqualTo(20));
            Assert.That(magazine.Reloading, Is.False);
            Assert.That(magazine.Step(5, 30), Is.Zero);
        }

        [Test]
        public void ReloadUsesReserveAtCompletionAndKeepsExistingRounds()
        {
            var magazine = new Magazine(20, 5);
            Assert.That(magazine.BeginReload(50, 2), Is.True);
            Assert.That(magazine.BeginReload(50, 2), Is.False);
            Assert.That(magazine.Step(3, 3), Is.EqualTo(3));
            Assert.That(magazine.Rounds, Is.EqualTo(8));
            Assert.That(magazine.BeginReload(0, 2), Is.False);
        }
    }
}
