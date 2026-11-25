using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Dustline
{
    internal enum PilotMessage : byte { Invalid, Join, Ready, Input, Frame, Rejected }
    internal enum PilotReject : byte { Code = 1, Unavailable, Protocol, Timeout }

    internal static class PilotWire
    {
        public const int Version = 1, MinCode = 16, MaxCode = 64;
        public const int Header = 4, InputBytes = Header + 33, FrameBytes = Header + 23 + 4 * 26, MaxPacket = FrameBytes;

        public static bool ValidCode(string code, int minimum = MinCode)
        {
            if (code == null || code.Length < minimum || code.Length > MaxCode) return false;
            foreach (var c in code)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
                    return false;
            return true;
        }

        public static PilotMessage Kind(ReadOnlySpan<byte> data)
        {
            if (data.Length < Header || data.Length > MaxPacket || data[0] != 'D' || data[1] != 'P' || data[2] != Version)
                return PilotMessage.Invalid;
            return (PilotMessage)data[3] switch
            {
                PilotMessage.Join when data.Length > Header && data.Length <= Header + MaxCode => PilotMessage.Join,
                PilotMessage.Ready when data.Length == Header => PilotMessage.Ready,
                PilotMessage.Input when data.Length == InputBytes => PilotMessage.Input,
                PilotMessage.Frame when data.Length == FrameBytes => PilotMessage.Frame,
                PilotMessage.Rejected when data.Length == Header + 1 && data[Header] >= (byte)PilotReject.Code
                    && data[Header] <= (byte)PilotReject.Timeout => PilotMessage.Rejected,
                _ => PilotMessage.Invalid
            };
        }

        public static bool MatchCode(ReadOnlySpan<byte> packet, byte[] expected) => Kind(packet) == PilotMessage.Join
            && CryptographicOperations.FixedTimeEquals(packet.Slice(Header), expected);

        public static int Join(byte[] output, string code)
        {
            Start(output, PilotMessage.Join);
            return Header + Encoding.ASCII.GetBytes(code, 0, code.Length, output, Header);
        }

        public static int Ready(byte[] output)
        {
            Start(output, PilotMessage.Ready);
            return Header;
        }

        public static int Reject(byte[] output, PilotReject reason)
        {
            Start(output, PilotMessage.Rejected);
            output[Header] = (byte)reason;
            return Header + 1;
        }

        public static int WriteInput(byte[] output, in PilotInput input)
        {
            Start(output, PilotMessage.Input);
            var w = new Writer(output);
            w.UInt(input.Sequence); w.UInt(input.Fire); w.UInt(input.Reload); w.UInt(input.Search);
            w.Float(input.Right); w.Float(input.Forward); w.Float(input.Yaw); w.Float(input.Pitch); w.Byte((byte)input.Buttons);
            return w.Position;
        }

        public static bool TryInput(ReadOnlySpan<byte> packet, out PilotInput input)
        {
            input = default;
            if (Kind(packet) != PilotMessage.Input) return false;
            var r = new Reader(packet);
            input = new PilotInput
            {
                Sequence = r.UInt(), Fire = r.UInt(), Reload = r.UInt(), Search = r.UInt(), Right = r.Float(), Forward = r.Float(),
                Yaw = r.Float(), Pitch = r.Float(), Buttons = (PilotButtons)r.Byte()
            };
            return input.Valid;
        }

        public static int WriteFrame(byte[] output, in PilotFrame frame)
        {
            Start(output, PilotMessage.Frame);
            var w = new Writer(output);
            w.UInt(frame.Tick); w.UInt(frame.InputAck); w.Byte((byte)frame.Phase); w.Byte((byte)frame.Result);
            w.Byte(frame.Seat); w.Byte(frame.Connected); w.Byte(frame.Ready); w.Byte(frame.Claimed); w.Byte(frame.Cargo);
            w.Byte(frame.Rounds); w.Byte(frame.Reserve); w.Byte(frame.Stamina); w.Byte(frame.SearchTicks); w.Byte(frame.ExtractionTicks);
            w.Byte((byte)((frame.Reloading ? 1 : 0) | (frame.SprintExhausted ? 2 : 0))); w.UShort(frame.Seconds);
            w.Actor(frame.First); w.Actor(frame.Second); w.Actor(frame.GuardA); w.Actor(frame.GuardB);
            return w.Position;
        }

        public static bool TryFrame(ReadOnlySpan<byte> packet, out PilotFrame frame)
        {
            frame = default;
            if (Kind(packet) != PilotMessage.Frame) return false;
            var r = new Reader(packet);
            frame = new PilotFrame
            {
                Tick = r.UInt(), InputAck = r.UInt(), Phase = (PilotPhase)r.Byte(), Result = (PilotResult)r.Byte(),
                Seat = r.Byte(), Connected = r.Byte(), Ready = r.Byte(), Claimed = r.Byte(), Cargo = r.Byte(), Rounds = r.Byte(),
                Reserve = r.Byte(), Stamina = r.Byte(), SearchTicks = r.Byte(), ExtractionTicks = r.Byte()
            };
            byte flags = r.Byte();
            frame.Reloading = (flags & 1) != 0;
            frame.SprintExhausted = (flags & 2) != 0;
            frame.Seconds = r.UShort();
            frame.First = r.Actor(); frame.Second = r.Actor(); frame.GuardA = r.Actor(); frame.GuardB = r.Actor();
            return flags <= 3 && frame.Seat < 2 && frame.Connected <= 3 && frame.Ready <= 3 && (frame.Connected & 1 << frame.Seat) != 0
                && frame.Claimed <= 15 && (frame.Cargo & ~frame.Claimed) == 0 && frame.Stamina <= 100 && frame.Rounds <= 20
                && frame.Reserve <= 60 && frame.SearchTicks < PilotMatch.SearchTime && frame.ExtractionTicks <= PilotMatch.ExtractionTime
                && frame.Seconds <= PilotMatch.RaidSeconds && (byte)frame.Phase <= (byte)PilotPhase.Results
                && (byte)frame.Result <= (byte)PilotResult.TimeExpired
                && ValidActor(frame.First) && ValidActor(frame.Second) && ValidActor(frame.GuardA) && ValidActor(frame.GuardB);
        }

        static bool ValidActor(in PilotActorFrame actor) => actor.Health <= 100 && actor.Yaw >= -180 && actor.Yaw <= 180
            && actor.Speed >= 0 && actor.Speed <= 7 && actor.Position.X >= -PilotMap.HalfWidth && actor.Position.X <= PilotMap.HalfWidth
            && actor.Position.Z >= -PilotMap.HalfDepth && actor.Position.Z <= PilotMap.HalfDepth && actor.Position.Y == 0;

        static void Start(byte[] output, PilotMessage type)
        {
            output[0] = (byte)'D'; output[1] = (byte)'P'; output[2] = Version; output[3] = (byte)type;
        }

        ref struct Writer
        {
            readonly Span<byte> buffer;
            public int Position;
            public Writer(Span<byte> output) { buffer = output; Position = Header; }
            public void Byte(byte value) => buffer[Position++] = value;
            public void UShort(ushort value) { BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(Position), value); Position += 2; }
            public void UInt(uint value) { BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(Position), value); Position += 4; }
            public void Float(float value) => UInt(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
            public void Actor(in PilotActorFrame actor)
            {
                Float(actor.Position.X); Float(actor.Position.Y); Float(actor.Position.Z); Float(actor.Yaw); Float(actor.Speed);
                UInt(actor.Shots); Byte(actor.Health); Byte((byte)((actor.Visible ? 1 : 0) | (actor.Crouched ? 2 : 0) | (actor.Running ? 4 : 0)));
            }
        }

        ref struct Reader
        {
            readonly ReadOnlySpan<byte> buffer;
            int position;
            public Reader(ReadOnlySpan<byte> input) { buffer = input; position = Header; }
            public byte Byte() => buffer[position++];
            public ushort UShort() { ushort value = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(position)); position += 2; return value; }
            public uint UInt() { uint value = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(position)); position += 4; return value; }
            public float Float() => BitConverter.Int32BitsToSingle(unchecked((int)UInt()));
            public PilotActorFrame Actor()
            {
                var actor = new PilotActorFrame { Position = new Vector3(Float(), Float(), Float()), Yaw = Float(), Speed = Float(), Shots = UInt(), Health = Byte() };
                byte flags = Byte();
                actor.Visible = (flags & 1) != 0; actor.Crouched = (flags & 2) != 0; actor.Running = (flags & 4) != 0;
                return actor;
            }
        }
    }
}
