using System;
using System.Numerics;

namespace Dustline
{
    internal enum PilotPhase : byte { Lobby, Raid, Results }
    internal enum PilotResult : byte { None, Extracted, Downed, Disconnected, TimeExpired }

    [Flags]
    internal enum PilotButtons : byte { None = 0, Fire = 1, Aim = 2, Sprint = 4, Crouch = 8, Quiet = 16, Paused = 32 }

    internal struct PilotInput
    {
        public uint Sequence, Fire, Reload, Search;
        public float Right, Forward, Yaw, Pitch;
        public PilotButtons Buttons;

        public bool Has(PilotButtons button) => (Buttons & button) != 0;

        public bool Valid => Sequence != 0 && Right >= -1f && Right <= 1f && Forward >= -1f && Forward <= 1f
            && Yaw >= -180f && Yaw <= 180f && Pitch >= -85f && Pitch <= 85f
            && (Buttons & ~(PilotButtons.Fire | PilotButtons.Aim | PilotButtons.Sprint | PilotButtons.Crouch | PilotButtons.Quiet | PilotButtons.Paused)) == 0;
    }

    internal struct PilotActorFrame
    {
        public Vector3 Position;
        public float Yaw, Speed;
        public uint Shots;
        public byte Health;
        public bool Visible, Crouched, Running;
    }

    internal struct PilotFrame
    {
        public uint Tick, InputAck;
        public PilotPhase Phase;
        public PilotResult Result;
        public byte Seat, Connected, Ready, Claimed, Cargo, Rounds, Reserve, Stamina, SearchTicks, ExtractionTicks;
        public ushort Seconds;
        public bool Reloading, SprintExhausted;
        public PilotActorFrame First, Second, GuardA, GuardB;

        public PilotActorFrame Actor(int i) => i switch { 0 => First, 1 => Second, 2 => GuardA, _ => GuardB };
    }

    internal sealed class PilotMatch
    {
        public const int Hz = 20, LobbySeconds = 90, RaidSeconds = 480, SearchTime = 30, ExtractionTime = 160;
        public const float StepSeconds = 1f / Hz, ReloadSeconds = 1.9f;
        const uint StaleInput = 6;
        readonly PilotSeat[] seats = { new PilotSeat(), new PilotSeat() };
        readonly PilotGuard[] guards = { new PilotGuard(), new PilotGuard() };
        readonly Random random;
        uint tick, deadline;
        byte claimed;

        public PilotMatch(int seed) => random = new Random(seed);
        public PilotPhase Phase { get; private set; }
        public bool Empty => !seats[0].Connected && !seats[1].Connected;
        public bool Retired => Phase == PilotPhase.Results && tick >= deadline;

        public int Join(uint skippedTicks = 0)
        {
            if (Phase != PilotPhase.Lobby || !Empty && tick + skippedTicks >= deadline) return -1;
            for (int i = 0; i < seats.Length; i++)
            {
                if (seats[i].Connected) continue;
                if (Empty) deadline = tick + skippedTicks + LobbySeconds * Hz;
                seats[i] = new PilotSeat { Connected = true };
                return i;
            }
            return -1;
        }

        public bool Ready(int seat)
        {
            if (Phase != PilotPhase.Lobby || !Connected(seat)) return false;
            seats[seat].Ready = true;
            return true;
        }

        public void Leave(int seat)
        {
            if (!Connected(seat)) return;
            PilotSeat player = seats[seat];
            player.Connected = false;
            player.Ready = false;
            if (Phase == PilotPhase.Raid && player.Result == PilotResult.None)
            {
                player.Result = PilotResult.Disconnected;
                FinishIfDone();
            }
        }

        public bool Submit(int seat, in PilotInput input, uint skippedTicks = 0)
        {
            if (Phase != PilotPhase.Raid || !Connected(seat) || !input.Valid) return false;
            PilotSeat player = seats[seat];
            PilotInput previous = player.Input;
            if (player.Result != PilotResult.None || input.Sequence <= previous.Sequence || input.Fire < previous.Fire
                || input.Search < previous.Search || input.Reload < previous.Reload) return false;
            player.Input = input;
            // Stamp receipt, not consumption, so an old queued action cannot become fresh after a stall.
            player.InputAt = tick + skippedTicks + 1;
            return true;
        }

        public void Step(uint missedTicks = 0)
        {
            tick += missedTicks + 1;
            if (Phase == PilotPhase.Lobby)
            {
                if (Empty) return;
                if (tick >= deadline) Finish();
                else if (seats[0].Ready && seats[1].Ready) Begin();
                return;
            }
            if (Phase != PilotPhase.Raid) return;
            if (tick >= deadline) { Finish(); return; }
            for (int i = 0; i < seats.Length; i++)
                if (seats[i].Result == PilotResult.None) AdvancePlayer(seats[i]);
            for (int i = 0; i < guards.Length; i++) AdvanceGuard(i, guards[i]);
            for (int i = 0; i < seats.Length; i++)
            {
                PilotSeat player = seats[i];
                if (player.Result != PilotResult.None) continue;
                if (!InExit(player.Body.Position)) player.ExtractionTicks = 0;
                else if (Live(player) && ++player.ExtractionTicks >= ExtractionTime) player.Result = PilotResult.Extracted;
            }
            FinishIfDone();
        }

        void Begin()
        {
            Phase = PilotPhase.Raid;
            deadline = tick + RaidSeconds * Hz;
            for (int i = 0; i < seats.Length; i++)
            {
                PilotSeat player = seats[i];
                player.Body.Position = PilotMap.Spawns[i];
                player.Body.Health = 100;
            }
            for (int i = 0; i < guards.Length; i++)
            {
                PilotGuard guard = guards[i];
                guard.Body.Position = PilotMap.Patrol[i, 0];
                guard.Body.Health = 65;
            }
        }

        bool Connected(int seat) => (uint)seat < seats.Length && seats[seat].Connected;
        static bool InExit(Vector3 point) => Vector3.DistanceSquared(point, PilotMap.Exit) <= PilotMap.ExitRadius * PilotMap.ExitRadius;

        void FinishIfDone()
        {
            if (seats[0].Result != PilotResult.None && seats[1].Result != PilotResult.None) Finish();
        }

        void Finish()
        {
            Phase = PilotPhase.Results;
            deadline = tick + 30 * Hz;
            foreach (var player in seats)
                if (player.Connected && player.Result == PilotResult.None) player.Result = PilotResult.TimeExpired;
        }

        bool Live(PilotSeat player) => player.Input.Sequence != 0 && !player.Input.Has(PilotButtons.Paused)
            && tick - player.InputAt < StaleInput;

        void AdvancePlayer(PilotSeat player)
        {
            ref readonly PilotInput input = ref player.Input;
            bool liveInput = Live(player);
            bool tap = liveInput && input.Fire != player.FireSeen;
            bool fire = liveInput && (tap || input.Has(PilotButtons.Fire));
            bool reload = liveInput && input.Reload != player.ReloadSeen;
            bool search = liveInput && input.Search != player.SearchSeen;
            player.FireSeen = input.Fire;
            player.ReloadSeen = input.Reload;
            player.SearchSeen = input.Search;
            ref PilotActorFrame body = ref player.Body;
            if (liveInput)
            {
                float angle = input.Yaw - body.Yaw;
                if (angle > 180f) angle -= 360f;
                if (angle < -180f) angle += 360f;
                body.Yaw += Math.Clamp(angle, -60f, 60f);
                if (body.Yaw > 180f) body.Yaw -= 360f;
                if (body.Yaw < -180f) body.Yaw += 360f;
                player.Pitch += Math.Clamp(input.Pitch - player.Pitch, -45f, 45f);
                body.Crouched = input.Has(PilotButtons.Crouch);
            }
            player.Aiming = liveInput && input.Has(PilotButtons.Aim);
            body.Running = liveInput && !body.Crouched && !player.Aiming && !fire && !player.Magazine.Reloading
                && input.Has(PilotButtons.Sprint) && input.Forward > .1f && !player.Exhausted;
            float speed = body.Crouched ? 2f : body.Running ? 6.5f : 3.8f;
            if (player.Aiming) speed *= .72f;
            if (input.Has(PilotButtons.Quiet) && !body.Running) speed *= .55f;
            Vector3 before = body.Position;
            if (liveInput)
            {
                Vector3 forward = PilotMap.Facing(body.Yaw), right = new Vector3(forward.Z, 0, -forward.X);
                Vector3 motion = right * input.Right + forward * input.Forward;
                if (motion.LengthSquared() > 1f) motion = Vector3.Normalize(motion);
                body.Position = PilotMap.Move(before, motion * (speed * StepSeconds));
            }
            body.Speed = Vector3.Distance(before, body.Position) * Hz;
            if (body.Running && body.Speed > .1f)
            {
                player.Stamina = Math.Max(0f, player.Stamina - 10f * StepSeconds);
                if (player.Stamina <= 0) player.Exhausted = true;
            }
            else
            {
                player.Stamina = Math.Min(100f, player.Stamina + 14f * StepSeconds);
                if (player.Exhausted && player.Stamina >= 25f && !input.Has(PilotButtons.Sprint)) player.Exhausted = false;
            }
            player.InputAck = input.Sequence;
            if (player.Magazine.Reloading) player.Reserve -= player.Magazine.Step(StepSeconds, player.Reserve);
            else if (reload || (fire && player.Magazine.Rounds == 0)) player.Magazine.BeginReload(player.Reserve, ReloadSeconds);
            else if (fire && tick >= player.NextShot && player.Magazine.Fire()) Shoot(player);

            if (body.Speed > .15f || fire || player.Magazine.Reloading)
            {
                player.SearchTarget = -1;
                player.SearchTicks = 0;
                return;
            }
            if (!liveInput || !search && player.SearchTarget < 0) return;
            int target = PilotMap.SearchTarget(Eye(body), PilotMap.Facing(body.Yaw, player.Pitch));
            if (target < 0 || (claimed & (1 << target)) != 0 || (!search && target != player.SearchTarget))
            {
                player.SearchTarget = -1;
                player.SearchTicks = 0;
                return;
            }
            if (target != player.SearchTarget) player.SearchTicks = 0;
            player.SearchTarget = target;
            if (++player.SearchTicks < SearchTime) return;
            byte bit = (byte)(1 << target);
            claimed |= bit;
            player.Cargo |= bit;
            player.SearchTarget = -1;
            player.SearchTicks = 0;
        }

        static Vector3 Eye(in PilotActorFrame body) => body.Position + Vector3.UnitY * (body.Crouched ? 1.02f : 1.58f);

        void Shoot(PilotSeat player)
        {
            ref PilotActorFrame body = ref player.Body;
            body.Shots++;
            player.NextShot = tick + 3;
            float spread = player.Aiming ? .28f : body.Crouched ? .56f : body.Speed > .15f ? 1.24f : .82f;
            Vector3 ray = PilotMap.Facing(body.Yaw + Jitter(spread), player.Pitch + Jitter(spread));
            Vector3 origin = Eye(body);
            float limit = PilotMap.Obstacle(origin, ray, 75f);
            if (ray.Y < -.00001f) limit = Math.Min(limit, -origin.Y / ray.Y);
            PilotGuard target = null;
            foreach (var guard in guards)
            {
                if (guard.Body.Health == 0 || !Hitbox(guard.Body, origin, ray, limit, out float hit) || hit >= limit) continue;
                limit = hit;
                target = guard;
            }
            if (target != null) target.Body.Health = (byte)Math.Max(0, target.Body.Health - 24);
        }

        float Jitter(float magnitude) => (float)(random.NextDouble() * 2 - 1) * magnitude;

        static bool Hitbox(in PilotActorFrame body, Vector3 origin, Vector3 ray, float limit, out float hit)
        {
            float height = body.Crouched ? 1.2f : 1.8f;
            return new PilotBox(body.Position.X, height * .5f, body.Position.Z, .76f, height, .76f).Intersect(origin, ray, limit, out hit);
        }

        void AdvanceGuard(int index, PilotGuard guard)
        {
            ref PilotActorFrame actor = ref guard.Body;
            if (actor.Health == 0) return;
            PilotSeat target = null;
            float nearest = 25f * 25f;
            Vector3 eye = Eye(actor), ray = default;
            foreach (var player in seats)
            {
                if (player.Result != PilotResult.None) continue;
                Vector3 delta = Eye(player.Body) - eye;
                float distance = delta.LengthSquared();
                if (distance >= nearest) continue;
                var direction = PilotMap.Unit(delta);
                float range = (float)Math.Sqrt(distance);
                if (PilotMap.Obstacle(eye, direction, range) < range) continue;
                target = player;
                nearest = distance;
                ray = direction;
            }
            actor.Speed = 0f;
            if (target != null)
            {
                actor.Yaw = PilotMap.Yaw(ray);
                if (!guard.Engaged) guard.NextShot = tick + 22;
                guard.Engaged = true;
                if (tick < guard.NextShot) return;
                guard.NextShot = tick + 26;
                actor.Shots++;
                float pitch = (float)(-Math.Atan2(ray.Y, PilotMap.Plane(ray).Length()) * 180 / Math.PI);
                ray = PilotMap.Facing(actor.Yaw + Jitter(2.2f), pitch + Jitter(1.2f));
                float limit = PilotMap.Obstacle(eye, ray, 25f);
                if (Hitbox(target.Body, eye, ray, limit, out float hit) && hit < limit)
                {
                    target.Body.Health = (byte)Math.Max(0, target.Body.Health - 7);
                    if (target.Body.Health == 0) target.Result = PilotResult.Downed;
                }
                return;
            }
            guard.Engaged = false;
            Vector3 goal = PilotMap.Patrol[index, guard.Waypoint];
            Vector3 movement = goal - actor.Position;
            if (movement.LengthSquared() < .15f * .15f)
            {
                guard.Waypoint = (guard.Waypoint + 1) % PilotMap.Patrol.GetLength(1);
                movement = PilotMap.Patrol[index, guard.Waypoint] - actor.Position;
            }
            Vector3 before = actor.Position;
            actor.Position = PilotMap.Move(before, PilotMap.Unit(movement) * (2f * StepSeconds));
            actor.Yaw = PilotMap.Yaw(movement);
            actor.Speed = Vector3.Distance(before, actor.Position) * Hz;
        }

        public PilotFrame Snapshot(int seat)
        {
            PilotSeat player = seats[seat];
            return new PilotFrame
            {
                Tick = tick, InputAck = player.InputAck, Phase = Phase, Result = player.Result, Seat = (byte)seat,
                Seconds = Phase == PilotPhase.Results || tick >= deadline ? (ushort)0 : (ushort)((deadline - tick + Hz - 1) / Hz),
                Connected = (byte)((seats[0].Connected ? 1 : 0) | (seats[1].Connected ? 2 : 0)),
                Ready = (byte)((seats[0].Ready ? 1 : 0) | (seats[1].Ready ? 2 : 0)), Claimed = claimed, Cargo = player.Cargo,
                Rounds = (byte)player.Magazine.Rounds, Reserve = (byte)player.Reserve, Stamina = (byte)player.Stamina,
                SearchTicks = player.SearchTicks, ExtractionTicks = player.ExtractionTicks, Reloading = player.Magazine.Reloading,
                SprintExhausted = player.Exhausted,
                First = View(seats[0].Body, seats[0].Result == PilotResult.None), Second = View(seats[1].Body, seats[1].Result == PilotResult.None),
                GuardA = View(guards[0].Body, guards[0].Body.Health > 0), GuardB = View(guards[1].Body, guards[1].Body.Health > 0)
            };

            PilotActorFrame View(PilotActorFrame actor, bool alive)
            {
                actor.Visible = Phase != PilotPhase.Lobby && alive && actor.Health != 0;
                return actor;
            }
        }

        sealed class PilotSeat
        {
            public bool Connected, Ready, Aiming, Exhausted;
            public PilotActorFrame Body;
            public PilotResult Result;
            public PilotInput Input;
            public uint InputAt, InputAck, FireSeen, ReloadSeen, SearchSeen, NextShot;
            public float Pitch, Stamina = 100f;
            public readonly Magazine Magazine = new Magazine(20, 20);
            public int Reserve = 60, SearchTarget = -1;
            public byte SearchTicks, ExtractionTicks, Cargo;
        }

        sealed class PilotGuard
        {
            public PilotActorFrame Body;
            public uint NextShot;
            public int Waypoint = 1;
            public bool Engaged;
        }
    }
}
