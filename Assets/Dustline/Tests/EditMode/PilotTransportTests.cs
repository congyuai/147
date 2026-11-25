using System;
using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using Unity.Networking.Transport;
using UnityEngine.TestTools;

namespace Dustline.Tests
{
    public sealed class PilotTransportTests
    {
        const string code = "pilot_fixture_only_27";
        const string otherCode = "pilot_fixture_wrong_27";

        [UnityTest]
        public IEnumerator TwoLoopbackClientsShareARoomAndTheRemainingSeatKeepsPlaying()
        {
            var clock = Stopwatch.StartNew();
            using var host = new PilotHost(NetworkEndpoint.LoopbackIpv4, code, null, null, Now());
            Assert.That(host.Port, Is.GreaterThan(0));
            var endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(host.Port);
            using var a = new PilotClient(endpoint, code, null, null, Now());
            yield return PumpUntil(() => a.HasFrame, a);
            Assert.That(a.Frame.Seat, Is.Zero);
            Assert.That(a.Ready(), Is.True);
            yield return PumpUntil(() => (a.Frame.Ready & 1) != 0, a);
            Assert.That(host.Phase, Is.EqualTo(PilotPhase.Lobby));

            using (var denied = new PilotClient(endpoint, otherCode, null, null, Now()))
            {
                yield return PumpUntil(() => !denied.Connected, denied, a);
                Assert.That(denied.Rejected, Is.EqualTo(PilotReject.Code));
                Assert.That(denied.HasFrame, Is.False);
            }
            using var b = new PilotClient(endpoint, code, null, null, Now());
            yield return PumpUntil(() => b.HasFrame && a.Frame.Connected == 3, a, b);
            Assert.That(b.Frame.Seat, Is.EqualTo(1));

            using (var third = new PilotClient(endpoint, code, null, null, Now()))
            {
                yield return PumpUntil(() => !third.Connected, third, a, b);
                Assert.That(third.Rejected, Is.EqualTo(PilotReject.Unavailable));
                Assert.That(third.HasFrame, Is.False);
            }
            Assert.That(b.Ready(), Is.True);
            yield return PumpUntil(() => a.Frame.Phase == PilotPhase.Raid && b.Frame.Phase == PilotPhase.Raid, a, b);
            Assert.That(b.Frame.First.Position, Is.EqualTo(a.Frame.First.Position));

            float initialZ = a.Frame.First.Position.Z;
            var input = new PilotInput { Sequence = 1, Forward = 1 };
            Assert.That(a.SendInput(input), Is.True);
            yield return PumpUntil(() => b.Frame.First.Position.Z > initialZ + .1f && a.Frame.InputAck == input.Sequence, a, b);
            Assert.That(b.Frame.Second.Position, Is.EqualTo(PilotMap.Spawns[1]));

            a.Dispose();
            yield return PumpUntil(() => b.Frame.Connected == 2, b);
            Assert.That(b.Frame.First.Visible, Is.False);
            Assert.That(b.Frame.Second.Visible, Is.True);
            Assert.That(b.Frame.Result, Is.EqualTo(PilotResult.None));
            Assert.That(host.Phase, Is.EqualTo(PilotPhase.Raid));
            float before = b.Frame.Second.Position.Z;
            Assert.That(b.SendInput(input), Is.True);
            yield return PumpUntil(() => b.Frame.Second.Position.Z > before + .1f && b.Frame.InputAck == input.Sequence, b);

            b.Dispose();
            yield return PumpUntil(() => host.Phase == PilotPhase.Lobby, a, b);
            using var next = new PilotClient(endpoint, code, null, null, Now());
            yield return PumpUntil(() => next.HasFrame, next);
            Assert.That(next.Frame.Phase, Is.EqualTo(PilotPhase.Lobby));
            Assert.That(next.Frame.Claimed, Is.Zero);
            Assert.That(next.Frame.Ready, Is.Zero);

            double Now() => clock.Elapsed.TotalSeconds;

            IEnumerator PumpUntil(Func<bool> predicate, params PilotClient[] clients)
            {
                double deadline = Now() + 6;
                while (!predicate() && Now() < deadline)
                {
                    double now = Now();
                    host.Pump(now);
                    foreach (var client in clients) client.Pump(now);
                    yield return null;
                }
                Assert.That(predicate(), Is.True, "The local Unity Transport test reached its deadline.");
            }
        }

        [Test]
        public void EndpointSelectionOnlyAcceptsExplicitLoopbackOrPrivateIpv4()
        {
            Assert.That(PilotAddress.TryParse("127.0.0.1", "7777", out _, out bool local), Is.True);
            Assert.That(local, Is.True);
            Assert.That(PilotAddress.TryParse("100.64.3.2", "7777", out _, out local), Is.True);
            Assert.That(local, Is.False);
            Assert.That(PilotAddress.TryParse("0.0.0.0", "7777", out _, out _), Is.False);
            Assert.That(PilotAddress.TryParse("203.0.113.1", "7777", out _, out _), Is.False);
            Assert.That(PilotAddress.TryParse("127.0.0.1", "0", out _, out _), Is.False);
            Assert.That(PilotAddress.TryParse("https://localhost", "7777", out _, out _), Is.False);
        }
    }
}
