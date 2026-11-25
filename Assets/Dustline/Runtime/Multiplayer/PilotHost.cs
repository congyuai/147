using System;
using System.Text;
using Unity.Networking.Transport;

namespace Dustline
{
    internal sealed class PilotHost : IDisposable
    {
        const double TickInterval = 1.0 / PilotMatch.Hz;
        readonly PilotLink link = new PilotLink();
        readonly Peer[] peers = new Peer[4];
        readonly byte[] code;
        readonly byte[] incoming = new byte[PilotWire.MaxPacket], outgoing = new byte[PilotWire.MaxPacket];
        PilotMatch room = NewRoom();
        double nextTick;

        struct Peer
        {
            public NetworkConnection Connection;
            public int Seat;
            public double Deadline;
            public PilotReject Rejection;
            public bool RejectionQueued;
            public bool Closing => Rejection != 0;
        }

        public PilotHost(NetworkEndpoint endpoint, string roomCode, string certificate, string key, double now)
        {
            if (!PilotWire.ValidCode(roomCode)) throw new ArgumentException("Use a random 16–64 character room code.");
            code = Encoding.ASCII.GetBytes(roomCode);
            nextTick = now;
            try { link.StartHost(endpoint, certificate, key); }
            catch { Array.Clear(code, 0, code.Length); throw; }
        }

        public ushort Port => link.LocalPort;
        public ulong SentBytes { get; private set; }
        public ulong Steps { get; private set; }
        public ulong Skipped { get; private set; }
        public PilotPhase Phase => room.Phase;

        static PilotMatch NewRoom() => new PilotMatch(Guid.NewGuid().GetHashCode());

        public void Pump(double now)
        {
            bool due = now >= nextTick;
            uint missed = due ? (uint)Math.Min(PilotMatch.RaidSeconds * PilotMatch.Hz, (now - nextTick) / TickInterval) : 0;
            link.Pump();
            for (int n = 0; n < 64; n++)
            {
                var connection = link.Accept();
                if (!connection.IsCreated) break;
                int slot = Array.FindIndex(peers, peer => !peer.Connection.IsCreated);
                if (slot < 0) { link.Disconnect(connection); continue; }
                peers[slot] = new Peer { Connection = connection, Seat = -1, Deadline = now + 5 };
            }
            for (int n = 0; n < 128; n++)
            {
                var type = link.Receive(incoming, out var connection, out int length);
                if (type == NetworkEvent.Type.Empty) break;
                int slot = 0;
                while (slot < peers.Length && (!peers[slot].Connection.IsCreated || !peers[slot].Connection.Equals(connection))) slot++;
                if (slot == peers.Length) continue;
                if (type == NetworkEvent.Type.Disconnect) Drop(slot, false);
                else if (type == NetworkEvent.Type.Data && !peers[slot].Closing) Handle(slot, length, now, missed);
            }
            for (int i = 0; i < peers.Length; i++)
            {
                ref Peer peer = ref peers[i];
                if (!peer.Connection.IsCreated) continue;
                if (peer.Closing)
                {
                    if (now >= peer.Deadline) Drop(i, true);
                    else if (!peer.RejectionQueued) QueueRejection(ref peer);
                }
                else if (peer.Seat < 0 && now >= peer.Deadline) Reject(i, PilotReject.Timeout, now);
            }
            if (!due) return;
            nextTick += (missed + 1) * TickInterval;
            if (nextTick <= now) nextTick = now + TickInterval;
            Skipped += missed;
            room.Step(missed);
            Steps++;
            if (room.Retired)
                for (int i = 0; i < peers.Length; i++)
                    if (peers[i].Seat >= 0 && peers[i].Connection.IsCreated) Drop(i, true);
            foreach (var peer in peers)
                if (peer.Connection.IsCreated && peer.Seat >= 0 && !peer.Closing)
                    Send(peer.Connection, PilotWire.WriteFrame(outgoing, room.Snapshot(peer.Seat)));
        }

        void Handle(int slot, int length, double now, uint missed)
        {
            var packet = new ReadOnlySpan<byte>(incoming, 0, length);
            var kind = PilotWire.Kind(packet);
            ref Peer peer = ref peers[slot];
            if (peer.Seat < 0)
            {
                bool valid = kind == PilotMessage.Join && PilotWire.MatchCode(packet, code);
                Array.Clear(incoming, 0, length);
                if (now >= peer.Deadline) { Reject(slot, PilotReject.Timeout, now); return; }
                if (!valid) { Reject(slot, kind == PilotMessage.Join ? PilotReject.Code : PilotReject.Protocol, now); return; }
                peer.Seat = room.Join(missed);
                if (peer.Seat < 0) Reject(slot, PilotReject.Unavailable, now);
                return;
            }
            if (kind == PilotMessage.Ready) room.Ready(peer.Seat);
            else if (kind == PilotMessage.Input && PilotWire.TryInput(packet, out var input)) room.Submit(peer.Seat, input, missed);
        }

        void Reject(int slot, PilotReject reason, double now)
        {
            ref Peer peer = ref peers[slot];
            peer.Rejection = reason;
            peer.Deadline = now + 1;
            QueueRejection(ref peer);
        }

        void QueueRejection(ref Peer peer) => peer.RejectionQueued = Send(peer.Connection,
            PilotWire.Reject(outgoing, peer.Rejection), control: true);

        bool Send(NetworkConnection to, int size, bool control = false)
        {
            if (!link.Send(to, outgoing, size, control)) return false;
            SentBytes += (uint)size;
            return true;
        }

        void Drop(int slot, bool disconnect)
        {
            var peer = peers[slot];
            if (peer.Seat >= 0) room.Leave(peer.Seat);
            if (disconnect) link.Disconnect(peer.Connection);
            peers[slot] = default;
            if (room.Empty && room.Phase != PilotPhase.Lobby) room = NewRoom();
        }

        public void Dispose()
        {
            Array.Clear(code, 0, code.Length);
            Array.Clear(incoming, 0, incoming.Length);
            for (int i = 0; i < peers.Length; i++)
                if (peers[i].Connection.IsCreated) Drop(i, true);
            link.Dispose();
        }
    }
}
