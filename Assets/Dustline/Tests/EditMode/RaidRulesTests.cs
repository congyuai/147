using NUnit.Framework;

namespace Dustline.Tests
{
    public sealed class RaidRulesTests
    {
        [TestCase(30, true, 120.0, 30)]
        [TestCase(90, true, 60.0, 60)]
        [TestCase(90, true, 120.0, 90)]
        [TestCase(120, true, 119.88, 120)]
        [TestCase(-1, true, 144.0, 144)]
        [TestCase(-1, true, 0.0, 60)]
        [TestCase(-1, false, 60.0, -1)]
        [TestCase(75, false, 60.0, 60)]
        public void RaidFrameTargetUsesNativeMobileMaximumForUncapped(int requested, bool mobile, double refresh, int expected)
        {
            Assert.That(RaidFrameRate.Target(requested, mobile, refresh), Is.EqualTo(expected));
        }

        [TestCase(0.85f, false, false)]
        [TestCase(0.86f, false, true)]
        [TestCase(0.73f, true, true)]
        [TestCase(0.71f, true, false)]
        [TestCase(-1f, true, false)]
        public void TouchSprintHasASeparateReleaseThreshold(float forward, bool active, bool expected)
        {
            Assert.That(PlayerInputReader.AutoSprintForward(forward, active), Is.EqualTo(expected));
        }

        [TestCase(55, 0.54f, false, false)]
        [TestCase(55, 0.55f, false, true)]
        [TestCase(70, 0.63f, false, false)]
        [TestCase(70, 0.63f, true, true)]
        [TestCase(99, 0.94f, false, false)]
        [TestCase(150, 1f, false, true)]
        public void SprintSensitivityControlsForwardTrigger(int threshold, float forward, bool active, bool expected)
        {
            Assert.That(PlayerInputReader.AutoSprintForward(forward, active, threshold), Is.EqualTo(expected));
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
