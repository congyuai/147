using System;
using System.Numerics;
using NUnit.Framework;

namespace Dustline.Tests
{
    public sealed class PilotRulesTests
    {
        [Test]
        public void RoomRequiresTwoReadySeatsAndKeepsAnIndependentOutcome()
        {
            var room = new PilotMatch(12);
            Assert.That(room.Join(), Is.Zero);
            Assert.That(room.Ready(0), Is.True);
            room.Step();
            Assert.That(room.Phase, Is.EqualTo(PilotPhase.Lobby));
            Assert.That(room.Join(), Is.EqualTo(1));
            Assert.That(room.Join(), Is.EqualTo(-1));
            Assert.That(room.Ready(1), Is.True);
            room.Step();
            Assert.That(room.Phase, Is.EqualTo(PilotPhase.Raid));
            Assert.That(Vector3.DistanceSquared(room.Snapshot(0).First.Position, room.Snapshot(0).Second.Position), Is.GreaterThan(4));
            room.Leave(0);
            Assert.That(room.Phase, Is.EqualTo(PilotPhase.Raid));
            Assert.That(room.Join(), Is.EqualTo(-1));
            Assert.That(room.Snapshot(0).Result, Is.EqualTo(PilotResult.Disconnected));
            Assert.That(room.Snapshot(1).Result, Is.EqualTo(PilotResult.None));
            room.Step(PilotMatch.RaidSeconds * PilotMatch.Hz);
            Assert.That(room.Phase, Is.EqualTo(PilotPhase.Results));
            Assert.That(room.Snapshot(0).Result, Is.EqualTo(PilotResult.Disconnected));
            Assert.That(room.Snapshot(1).Result, Is.EqualTo(PilotResult.TimeExpired));
            room.Leave(1);
            Assert.That(room.Empty, Is.True);
        }

        [Test]
        public void OneUnreadySeatDoesNotMakeALobbyWaitForever()
        {
            var room = new PilotMatch(10);
            room.Join();
            room.Ready(0);
            room.Step(PilotMatch.LobbySeconds * PilotMatch.Hz);
            Assert.That(room.Phase, Is.EqualTo(PilotPhase.Results));
            Assert.That(room.Snapshot(0).Result, Is.EqualTo(PilotResult.TimeExpired));
            room.Step(30 * PilotMatch.Hz);
            Assert.That(room.Retired, Is.True);
        }

        [Test]
        public void RejectsInvalidAndReplayedInputsAndDoesNotDropFreshActionsDuringATickGap()
        {
            var room = ActiveRoom();
            var bad = new PilotInput { Sequence = 1, Forward = float.NaN };
            Assert.That(room.Submit(0, bad), Is.False);
            bad.Forward = 1;
            bad.Buttons = (PilotButtons)128;
            Assert.That(room.Submit(0, bad), Is.False);
            var trigger = new PilotInput { Sequence = 1, Fire = 1 };
            Assert.That(room.Submit(-1, trigger), Is.False);
            Assert.That(room.Submit(2, trigger), Is.False);
            Assert.That(room.Submit(0, trigger, skippedTicks: 10), Is.True);
            Assert.That(room.Submit(0, trigger, skippedTicks: 10), Is.False);
            room.Step(10);
            Assert.That(room.Snapshot(0).Rounds, Is.EqualTo(19));
            Assert.That(room.Snapshot(1).Rounds, Is.EqualTo(20));
            trigger.Sequence++;
            Assert.That(room.Submit(0, trigger, skippedTicks: 10), Is.True);
            room.Step(10);
            Assert.That(room.Snapshot(0).Rounds, Is.EqualTo(19));
            trigger.Sequence++;
            trigger.Reload = 1;
            Assert.That(room.Submit(0, trigger, skippedTicks: 10), Is.True);
            room.Step(10);
            Assert.That(room.Snapshot(0).Reloading, Is.True);
            for (int i = 0; i < 45; i++) room.Step();
            Assert.That(room.Snapshot(0).Rounds, Is.EqualTo(20));
            Assert.That(room.Snapshot(0).Reserve, Is.EqualTo(59));
        }

        [Test]
        public void ActionsQueuedBeforeAStallDoNotExecuteOnRecovery()
        {
            var room = ActiveRoom();
            var initial = room.Snapshot(0).First.Position;
            var input = new PilotInput { Fire = 1, Forward = 1 };
            Send(room, 0, ref input);
            room.Step(10);
            var stalled = room.Snapshot(0);
            Assert.That(stalled.First.Position, Is.EqualTo(initial));
            Assert.That(stalled.Rounds, Is.EqualTo(20));
            input.Forward = 0;
            Send(room, 0, ref input);
            room.Step();
            Assert.That(room.Snapshot(0).Rounds, Is.EqualTo(20));
            input.Fire++;
            Send(room, 0, ref input);
            room.Step();
            Assert.That(room.Snapshot(0).Rounds, Is.EqualTo(19));
        }

        [Test]
        public void TwoSearchesOnlyClaimOneCrateAndLeavingRangeCancelsProgress()
        {
            var room = ActiveRoom();
            var a = new PilotInput();
            var b = new PilotInput();
            var target = PilotMap.Crates[0].Center;
            Move(room, 0, ref a, new Vector3(-5.2f, 0, -22.3f));
            Move(room, 1, ref b, new Vector3(-4.8f, 0, -22.3f));
            Aim(room, 0, ref a, target);
            Aim(room, 1, ref b, target);
            a.Search = 1;
            Send(room, 0, ref a);
            room.Step();
            Assert.That(room.Snapshot(0).SearchTicks, Is.EqualTo(1));
            a.Forward = -1;
            Send(room, 0, ref a);
            room.Step();
            Assert.That(room.Snapshot(0).SearchTicks, Is.Zero);
            Assert.That(room.Snapshot(0).Claimed, Is.Zero);
            Move(room, 0, ref a, new Vector3(-5.2f, 0, -22.3f));
            Aim(room, 0, ref a, target);
            a.Search++;
            b.Search++;
            for (int i = 0; i < PilotMatch.SearchTime + 5; i++)
            {
                Send(room, 0, ref a);
                Send(room, 1, ref b);
                room.Step();
            }
            var first = room.Snapshot(0);
            var second = room.Snapshot(1);
            Assert.That(first.Claimed, Is.EqualTo(1));
            Assert.That(second.Claimed, Is.EqualTo(first.Claimed));
            Assert.That(first.Cargo ^ second.Cargo, Is.EqualTo(1));
            Assert.That(first.Cargo & second.Cargo, Is.Zero);
        }

        [Test]
        public void StaleInputPausesSearchAndFreshInputRechecksWhereThePlayerLooks()
        {
            var room = ActiveRoom();
            var input = new PilotInput();
            var target = PilotMap.Crates[0].Center;
            Move(room, 0, ref input, new Vector3(-5, 0, -22.3f));
            Aim(room, 0, ref input, target);
            input.Search++;
            Send(room, 0, ref input);
            room.Step();
            room.Step(10);
            byte held = room.Snapshot(0).SearchTicks;
            Assert.That(held, Is.EqualTo(1));
            input.Buttons = PilotButtons.Paused | PilotButtons.Fire;
            input.Forward = 1;
            input.Fire++;
            input.Reload++;
            var beforePause = room.Snapshot(0);
            for (int i = 0; i < PilotMatch.SearchTime + 2; i++) { Send(room, 0, ref input); room.Step(); }
            var paused = room.Snapshot(0);
            Assert.That(paused.First.Position, Is.EqualTo(beforePause.First.Position));
            Assert.That(paused.SearchTicks, Is.EqualTo(held));
            Assert.That(paused.Rounds, Is.EqualTo(beforePause.Rounds));
            Assert.That(paused.Reloading, Is.False);
            input.Buttons = PilotButtons.None;
            input.Forward = 0;
            Send(room, 0, ref input);
            room.Step();
            Assert.That(room.Snapshot(0).SearchTicks, Is.EqualTo(held + 1));
            input.Yaw = 180;
            for (int i = 0; i < 4; i++) { Send(room, 0, ref input); room.Step(); }
            Assert.That(room.Snapshot(0).SearchTicks, Is.Zero);
            Assert.That(room.Snapshot(0).Claimed, Is.Zero);
        }

        [Test]
        public void SameMapSupportsAFullCoopRoundAndEachPlayerExtractsOnce()
        {
            var room = ActiveRoom();
            var a = new PilotInput();
            var b = new PilotInput();
            Move(room, 0, ref a, new Vector3(0, 0, -11));
            FireAtGuard(room, ref a, 2);
            FireAtGuard(room, ref a, 3);
            Assert.That(room.Snapshot(0).GuardA.Health, Is.Zero);
            Assert.That(room.Snapshot(0).GuardB.Health, Is.Zero);
            Move(room, 0, ref a, new Vector3(0, 0, 35));
            var entered = room.Snapshot(0).ExtractionTicks;
            Assert.That(entered, Is.GreaterThan(0));
            for (int i = 0; i < 20; i++) room.Step();
            byte paused = room.Snapshot(0).ExtractionTicks;
            Assert.That(paused, Is.InRange(entered, entered + 5));
            a.Buttons = PilotButtons.Paused;
            for (int i = 0; i < 10; i++) { Send(room, 0, ref a); room.Step(); }
            Assert.That(room.Snapshot(0).ExtractionTicks, Is.EqualTo(paused));
            Assert.That(room.Snapshot(0).Result, Is.EqualTo(PilotResult.None));
            Move(room, 0, ref a, new Vector3(0, 0, 30));
            Assert.That(room.Snapshot(0).ExtractionTicks, Is.Zero);
            Move(room, 0, ref a, PilotMap.Exit);
            WaitForExit(room, 0, ref a);
            Assert.That(room.Phase, Is.EqualTo(PilotPhase.Raid));
            Assert.That(room.Snapshot(0).Result, Is.EqualTo(PilotResult.Extracted));
            Assert.That(room.Snapshot(1).Result, Is.EqualTo(PilotResult.None));
            a.Sequence++;
            Assert.That(room.Submit(0, a), Is.False);
            Move(room, 1, ref b, new Vector3(0, 0, -31));
            Move(room, 1, ref b, PilotMap.Exit);
            WaitForExit(room, 1, ref b);
            Assert.That(room.Phase, Is.EqualTo(PilotPhase.Results));
            Assert.That(room.Snapshot(1).Result, Is.EqualTo(PilotResult.Extracted));
        }

        [Test]
        public void ExhaustedSprintRequiresStaminaAndAReleaseBeforeRunningAgain()
        {
            var room = ActiveRoom();
            var input = new PilotInput();
            Move(room, 0, ref input, new Vector3(-34, 0, -35));
            input.Yaw = 90;
            for (int i = 0; i < 2; i++) { Send(room, 0, ref input); room.Step(); }
            input.Forward = 1;
            input.Buttons = PilotButtons.Sprint;
            for (int i = 0; i < 200; i++) { Send(room, 0, ref input); room.Step(); }
            var exhausted = room.Snapshot(0);
            Assert.That(exhausted.Stamina, Is.Zero);
            Assert.That(exhausted.SprintExhausted, Is.True);
            input.Forward = 0;
            for (int i = 0; i < 45; i++) { Send(room, 0, ref input); room.Step(); }
            Assert.That(room.Snapshot(0).Stamina, Is.GreaterThanOrEqualTo(25));
            Assert.That(room.Snapshot(0).SprintExhausted, Is.True);
            input.Buttons = PilotButtons.None;
            Send(room, 0, ref input);
            room.Step();
            Assert.That(room.Snapshot(0).SprintExhausted, Is.False);
            input.Forward = 1;
            input.Buttons = PilotButtons.Sprint;
            Send(room, 0, ref input);
            room.Step();
            Assert.That(room.Snapshot(0).First.Running, Is.True);
        }

        [Test]
        public void MapBlocksMovementAndOccludesRaysUsingTheSameCover()
        {
            var origin = new Vector3(15, 0, -23);
            Vector3 stop = PilotMap.Move(origin, new Vector3(0, 0, 25));
            Assert.That(stop.Z, Is.EqualTo(-18f - PilotMap.PlayerRadius).Within(.0001f));
            Assert.That(PilotMap.Obstacle(origin + Vector3.UnitY * 1.58f, Vector3.UnitZ, 75), Is.EqualTo(5f).Within(.0001f));
            Assert.That(PilotMap.Move(PilotMap.Spawns[0], new Vector3(0, 0, -500)).Z,
                Is.EqualTo(-PilotMap.HalfDepth + PilotMap.PlayerRadius).Within(.0001f));
        }

        [Test]
        public void PacketCodecRoundTripsAndRejectsWrongSizesAndVersions()
        {
            var bytes = new byte[PilotWire.MaxPacket + 1];
            var input = new PilotInput { Sequence = 7, Fire = 4, Right = -.25f, Forward = .5f, Yaw = 123, Pitch = -14, Buttons = PilotButtons.Aim };
            int n = PilotWire.WriteInput(bytes, input);
            Assert.That(n, Is.EqualTo(PilotWire.InputBytes));
            Assert.That(PilotWire.TryInput(bytes.AsSpan(0, n), out var decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(input));
            Assert.That(PilotWire.TryInput(bytes.AsSpan(0, n - 1), out _), Is.False);
            Assert.That(PilotWire.TryInput(bytes.AsSpan(0, n + 1), out _), Is.False);
            bytes[2]++;
            Assert.That(PilotWire.TryInput(bytes.AsSpan(0, n), out _), Is.False);
            input.Pitch = float.PositiveInfinity;
            n = PilotWire.WriteInput(bytes, input);
            Assert.That(PilotWire.TryInput(bytes.AsSpan(0, n), out _), Is.False);
            var frame = ActiveRoom().Snapshot(1);
            frame.SprintExhausted = true;
            n = PilotWire.WriteFrame(bytes, frame);
            Assert.That(n, Is.EqualTo(PilotWire.FrameBytes));
            Assert.That(PilotWire.TryFrame(bytes.AsSpan(0, n), out var snapshot), Is.True);
            Assert.That(snapshot, Is.EqualTo(frame));
            Assert.That(PilotWire.TryFrame(bytes.AsSpan(0, n + 1), out _), Is.False);
            frame.Seat = 2;
            n = PilotWire.WriteFrame(bytes, frame);
            Assert.That(PilotWire.TryFrame(bytes.AsSpan(0, n), out _), Is.False);
        }

        static PilotMatch ActiveRoom()
        {
            var room = new PilotMatch(12);
            for (int i = 0; i < 2; i++) { Assert.That(room.Join(), Is.EqualTo(i)); Assert.That(room.Ready(i), Is.True); }
            room.Step();
            return room;
        }

        static void Send(PilotMatch room, int seat, ref PilotInput input)
        {
            input.Sequence++;
            Assert.That(room.Submit(seat, input), Is.True);
        }

        static void Move(PilotMatch room, int seat, ref PilotInput input, Vector3 target)
        {
            input.Yaw = input.Pitch = 0;
            input.Buttons = PilotButtons.None;
            for (int i = 0; i < 550; i++)
            {
                Vector3 delta = (target - room.Snapshot(seat).Actor(seat).Position) * (PilotMatch.Hz / 3.8f);
                if (delta.LengthSquared() < .0064f) { input.Right = input.Forward = 0; return; }
                if (delta.LengthSquared() > 1) delta = Vector3.Normalize(delta);
                input.Right = delta.X;
                input.Forward = delta.Z;
                Send(room, seat, ref input);
                room.Step();
            }
            Assert.Fail("The route to " + target + " did not finish.");
        }

        static void Aim(PilotMatch room, int seat, ref PilotInput input, Vector3 target)
        {
            Vector3 ray = target - room.Snapshot(seat).Actor(seat).Position - Vector3.UnitY * 1.58f;
            input.Yaw = PilotMap.Yaw(ray);
            input.Pitch = (float)(-Math.Atan2(ray.Y, PilotMap.Plane(ray).Length()) * 180 / Math.PI);
        }

        static void FireAtGuard(PilotMatch room, ref PilotInput input, int guard)
        {
            input.Right = input.Forward = 0;
            input.Buttons = PilotButtons.Aim | PilotButtons.Fire;
            for (int i = 0; i < 240; i++)
            {
                var body = room.Snapshot(0).Actor(guard);
                if (body.Health == 0) return;
                Aim(room, 0, ref input, body.Position + Vector3.UnitY * .9f);
                Send(room, 0, ref input);
                room.Step();
            }
            Assert.Fail("Guard " + guard + " never went down.");
        }

        static void WaitForExit(PilotMatch room, int seat, ref PilotInput input)
        {
            for (int i = 0; i <= PilotMatch.ExtractionTime; i++)
            {
                if (room.Snapshot(seat).Result == PilotResult.Extracted) return;
                Send(room, seat, ref input);
                room.Step();
            }
            Assert.Fail("Seat " + seat + " did not extract.");
        }
    }
}
