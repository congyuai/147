using System;
using Unity.Networking.Transport;

namespace Dustline
{
    internal sealed class PilotClient : IDisposable
    {
        readonly PilotLink link = new PilotLink();
        readonly byte[] incoming = new byte[PilotWire.MaxPacket], outgoing = new byte[PilotWire.MaxPacket];
        NetworkConnection peer;
        int pendingJoin;
        bool transportReady;
        double receivedAt;
        PilotFrame frame;

        public bool Connected => peer.IsCreated;
        public bool HasFrame { get; private set; }
        public PilotFrame Frame => frame;
        public PilotReject? Rejected { get; private set; }

        public PilotClient(NetworkEndpoint endpoint, string roomCode, string trust, string name, double now)
        {
            if (!PilotWire.ValidCode(roomCode)) throw new ArgumentException("Invalid room code.");
            pendingJoin = PilotWire.Join(outgoing, roomCode);
            receivedAt = now;
            try { peer = link.StartClient(endpoint, trust, name); }
            catch { Dispose(); throw; }
        }

        public void Pump(double now)
        {
            if (!Connected) return;
            link.Pump();
            for (int n = 0; n < 96; n++)
            {
                var kind = link.Receive(incoming, out var from, out int length);
                if (kind == NetworkEvent.Type.Empty) break;
                if (!from.Equals(peer)) continue;
                if (kind == NetworkEvent.Type.Connect) transportReady = true;
                else if (kind == NetworkEvent.Type.Disconnect) { Dispose(); return; }
                else if (kind == NetworkEvent.Type.Data)
                {
                    var data = new ReadOnlySpan<byte>(incoming, 0, length);
                    if (PilotWire.Kind(data) == PilotMessage.Rejected)
                    {
                        Rejected = (PilotReject)data[PilotWire.Header];
                        Dispose();
                        return;
                    }
                    if (!PilotWire.TryFrame(data, out PilotFrame next)) continue;
                    if (HasFrame && next.Tick <= frame.Tick) continue;
                    frame = next;
                    HasFrame = true;
                    receivedAt = now;
                }
            }
            if (transportReady && pendingJoin > 0 && link.Send(peer, outgoing, pendingJoin, control: true))
            {
                Array.Clear(outgoing, 0, pendingJoin);
                pendingJoin = 0;
            }
            if (now - receivedAt > (HasFrame ? 8 : 12)) { Rejected = PilotReject.Timeout; Dispose(); }
        }

        public bool Ready() => CanSend && frame.Phase == PilotPhase.Lobby && link.Send(peer, outgoing, PilotWire.Ready(outgoing), control: true);
        public bool SendInput(in PilotInput input) => CanSend && frame.Phase == PilotPhase.Raid && frame.Result == PilotResult.None
            && link.Send(peer, outgoing, PilotWire.WriteInput(outgoing, input));
        bool CanSend => Connected && HasFrame && pendingJoin == 0;

        public void Dispose()
        {
            Array.Clear(outgoing, 0, outgoing.Length);
            link.Disconnect(peer);
            link.Dispose();
            peer = default;
        }
    }
}
