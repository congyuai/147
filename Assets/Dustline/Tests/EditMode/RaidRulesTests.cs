using NUnit.Framework;

namespace Dustline.Tests
{
    public sealed class RaidRulesTests
    {
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
