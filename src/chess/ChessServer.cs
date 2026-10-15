using System;
using System.Collections.Generic;
using System.Linq;
using Chess.Common;
using Players;
using Players.Interface.Model;
using Players.Services;
using Ships;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;
using Ships.Services;
using UnityEngine;
using WorldTracking.Interface.Services;

namespace Chess
{
    /// <summary>
    /// The host's side: seats, moves (checked against the full rules), resign,
    /// draws, a new game - and the crash detector that knocks pieces over.
    /// Every change is written into the table's part state, which is how
    /// every other player's board updates.
    /// </summary>
    public static class ChessServer
    {
        public const byte KindSit = 1, KindLeave = 2, KindMove = 3, KindResign = 4, KindDraw = 5, KindNewGame = 6, KindStandUp = 7, KindWatch = 8, KindFree = 9;
        public static float Reach = 4f;
        public static float KnockAt = 30f;   // m/s^2 of sideways jolt before pieces start to fall

        private static bool Load(uint shipId, ushort partId, out TrackedShipServer ship, out StatefulPart part, out ChessGame g)
        {
            g = null; part = null;
            ship = Svc.Get<ShipsServerTracker>()?.GetShip(shipId);
            if (ship == null || !ship.TryGetStatefulPart(partId, out part) || part.Settings == null || !ChessParts.IsChess(part.Settings.id)) return false;
            BeaconState b = part.State as BeaconState;
            if (b == null) return false;
            if (!ChessGame.TryDecode(b.Name, out g)) g = new ChessGame();
            return true;
        }

        public static void Save(TrackedShipServer ship, StatefulPart part, ChessGame g)
        {
            g.Seq++;
            ship.UpdatePartState(part.Id, Rec.With((BeaconState)part.State, "Name", g.Encode()));
        }

        public static Vector3 WorldPos(TrackedShipServer ship, StatefulPart part, uint space)
        {
            return Svc.Get<IWorldTransposeServer>().Position(ship.RelativeSpaceId, space, part.LocalPosition + part.LocalRotation * new Vector3(0f, ChessParts.DimsOf(part.Settings.id).BoardY, 0f));
        }

        /// <summary>A player's request, as the host handles it. Returns a message for them, or null.</summary>
        public static string Handle(TrackedPlayerServer player, ChessMsg m)
        {
            TrackedShipServer ship; StatefulPart part; ChessGame g;
            if (!Load(m.Ship, m.Part, out ship, out part, out g)) return "That table is gone.";
            if ((WorldPos(ship, part, player.SpaceId) - player.Position).magnitude > Reach + 1.5f) return "Too far from the table.";
            bool here = AtTable(player, ship, part);
            if (!here && m.Kind != KindSit && m.Kind != KindWatch) return "Sit down at the table first.";
            bool isWhite = here && g.White == player.Id, isBlack = here && g.Black == player.Id;
            switch (m.Kind)
            {
                case KindWatch:
                    return SitDown(player, ship, part) ? null : "You can't use the table right now.";
                case KindSit:
                    {
                        bool white = m.Text == "w";
                        uint seat = white ? g.White : g.Black;
                        if (seat != 0 && seat != player.Id && StillSeated(seat, ship, part))
                        {
                            SitDown(player, ship, part);
                            return (white ? "White" : "Black") + "'s seat is taken - watching.";
                        }
                        if (!SitDown(player, ship, part)) return "You can't use the table right now.";
                        if (white) { g.White = player.Id; g.WhiteName = player.Name; } else { g.Black = player.Id; g.BlackName = player.Name; }
                        Save(ship, part, g);
                        return null;
                    }
                case KindLeave:
                    if (!isWhite && !isBlack) return null;
                    if (isWhite) { g.White = 0; }
                    if (isBlack) { g.Black = 0; }
                    Save(ship, part, g);
                    return null;
                case KindMove:
                    {
                        if (g.Free) return FreeMove(g, ship, part, isWhite || isBlack, m.Text);
                        if (g.Over) return "The game is over - start a new one.";
                        if (g.Knocked != 0) return "Stand the pieces back up first.";
                        Position pos = g.Pos();
                        if ((pos.WhiteToMove && !isWhite) || (!pos.WhiteToMove && !isBlack)) return "It is not your move.";
                        Move mv = pos.Legal().FirstOrDefault(x => x.Uci() == m.Text);
                        if (mv.From == mv.To) return "That move is not allowed.";
                        g.LastSan = pos.San(mv);
                        g.Captured = pos.Sq[mv.To] != 0 ? Fen(pos.Sq[mv.To]) : (mv.Flags & Move.EnPassant) != 0 ? (pos.WhiteToMove ? 'p' : 'P') : '-';
                        pos.Apply(mv);
                        g.Fen = pos.ToFen();
                        g.Last = mv.Uci();
                        g.DrawOffer = '-';
                        g.Status = ChessGame.OutcomeCode(pos.Result());
                        Save(ship, part, g);
                        return null;
                    }
                case KindResign:
                    if (g.Over || (!isWhite && !isBlack)) return null;
                    // with both seats yours, it is the side to move that gives up
                    bool whiteGivesUp = isWhite && (!isBlack || g.Pos().WhiteToMove);
                    g.Status = whiteGivesUp ? "R0" : "R1";
                    Save(ship, part, g);
                    return null;
                case KindDraw:
                    {
                        if (g.Over || (!isWhite && !isBlack)) return null;
                        char me = isWhite && isBlack ? (g.Pos().WhiteToMove ? 'w' : 'b') : isWhite ? 'w' : 'b';
                        if (g.DrawOffer != '-' && g.DrawOffer != me) { g.Status = "A"; g.DrawOffer = '-'; }
                        else g.DrawOffer = me;
                        Save(ship, part, g);
                        return null;
                    }
                case KindFree:
                    {
                        if (!isWhite && !isBlack) return "Sit at the table first.";
                        bool on = m.Text == "1";
                        if (on == g.Free) return null;
                        g.Free = on;
                        // back to the rules: from the start (a free board need not even have kings)
                        if (!on) { g.Fen = Position.StartFen; g.Last = ""; g.LastSan = ""; g.Captured = '-'; g.Status = ""; g.DrawOffer = '-'; g.Knocked = 0; }
                        Save(ship, part, g);
                        return on ? null : "Rules are back on - a new game.";
                    }
                case KindNewGame:
                    if (!isWhite && !isBlack) return "Sit at the table first.";
                    if (!g.Free && !g.Over && g.Last.Length > 0 && !(isWhite && isBlack)) return "Finish (or resign) this game first.";
                    g.Fen = Position.StartFen; g.Last = ""; g.LastSan = ""; g.Captured = '-'; g.Status = ""; g.DrawOffer = '-'; g.Knocked = 0;
                    Save(ship, part, g);
                    return null;
                case KindStandUp:
                    if (g.Knocked == 0) return null;
                    g.Knocked = 0;
                    Save(ship, part, g);
                    return null;
            }
            return null;
        }

        /// <summary>Free play: any piece to any other square (taking whatever is there). No turns, no check.</summary>
        private static string FreeMove(ChessGame g, TrackedShipServer ship, StatefulPart part, bool seated, string uci)
        {
            if (!seated) return "Sit at the table first.";
            if (g.Knocked != 0) return "Stand the pieces back up first.";
            if (uci == null || uci.Length < 4) return "That move is not allowed.";
            int from = Position.SquareOf(uci.Substring(0, 2)), to = Position.SquareOf(uci.Substring(2, 2));
            if (from < 0 || from > 63 || to < 0 || to > 63 || from == to) return "That move is not allowed.";
            Position pos = g.Pos();
            sbyte piece = pos.Sq[from];
            if (piece == 0) return "There is no piece there.";
            g.Captured = pos.Sq[to] != 0 ? Fen(pos.Sq[to]) : '-';
            pos.Sq[to] = piece;
            pos.Sq[from] = 0;
            pos.EnPassant = -1;
            pos.Castling = 0;
            pos.WhiteToMove = piece < 0;    // the other colour "to move", for the look of it
            g.Fen = pos.ToFen();
            g.Last = uci.Substring(0, 4);
            g.LastSan = char.ToUpperInvariant(Fen(piece)) + (g.Captured != '-' ? "x" : "") + uci.Substring(2, 2);
            g.Status = "";
            g.DrawOffer = '-';
            Save(ship, part, g);
            return null;
        }

        private static char Fen(sbyte v) { char c = "pnbrqk"[Math.Abs(v) - 1]; return v > 0 ? char.ToUpperInvariant(c) : c; }

        /// <summary>
        /// Put the player "at" the table, in the game's own state for using a part
        /// (research table, targeting computer): no walking or turning, F leaves,
        /// ESC pauses. Many players can be at one table (two seats and watchers),
        /// so the part itself is not locked the way a pilot seat is.
        /// </summary>
        public static bool SitDown(TrackedPlayerServer player, TrackedShipServer ship, StatefulPart part)
        {
            if (AtTable(player, ship, part)) return true;
            if (player.StateVal != PlayerStateType.Walking) return false;
            player.SetState(new PlayerState { Type = PlayerStateType.InteractingWithGenericPart, EntityId = ship.Id, PartId = part.Id });
            return true;
        }

        public static bool AtTable(TrackedPlayerServer p, TrackedShipServer ship, StatefulPart part)
        {
            PlayerState st = p.State;
            return st.Type == PlayerStateType.InteractingWithGenericPart && st.EntityId == ship.Id && st.PartId == part.Id;
        }

        /// <summary>A seat is held while its player is at the table (in the game, not saved: a rejoin starts standing).</summary>
        private static bool StillSeated(uint playerId, TrackedShipServer ship, StatefulPart part)
        {
            PlayersServerTracker ps = Svc.Get<PlayersServerTracker>();
            TrackedPlayerServer p = ps?.ActivePlayers.FirstOrDefault(x => x.Id == playerId);
            return p != null && AtTable(p, ship, part);
        }

        // ---- every frame on the host ----------------------------------------------------------

        private class Watch { public Vector3 V, ASmooth; public float T, Seen, Tick; public uint Space; public bool Has; public float Cooldown; }
        private static readonly Dictionary<uint, Watch> _ships = new Dictionary<uint, Watch>();
        private static readonly List<KeyValuePair<TrackedShipServer, StatefulPart>> _tables = new List<KeyValuePair<TrackedShipServer, StatefulPart>>();
        private static float _nextScan, _nextSeats;
        public static int KnockEvents;
        public static string LastKnock = "";

        public static void Update()
        {
            if (!Net.IsServer) return;
            ShipsServerTracker ships = Svc.Get<ShipsServerTracker>();
            if (ships == null) return;
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                _tables.Clear();
                foreach (TrackedShipServer s in ships.Ships)
                    foreach (StatefulPart p in s.GetAllStatefulParts<BeaconState>())
                        if (p.Settings != null && ChessParts.IsChess(p.Settings.id)) _tables.Add(new KeyValuePair<TrackedShipServer, StatefulPart>(s, p));
            }
            if (_tables.Count == 0) return;
            // The ship's velocity only changes on physics ticks, so a change seen
            // this frame happened within one tick - or within this frame, when frames
            // are slower than ticks - never across the whole time since the last
            // change (a ship sitting still, then hit, changes in one tick).
            // What knocks pieces over is a sudden change against the recent average:
            // a steady burn, however hard, leaves them standing; a crash or a lurch
            // does not.
            float now = Time.time;
            foreach (var t in _tables)
            {
                TrackedShipServer ship = t.Key;
                if (ship == null || ship.IsShipDestroyed) continue;
                Watch w;
                if (!_ships.TryGetValue(ship.Id, out w)) _ships[ship.Id] = w = new Watch();
                Vector3 v = ship.PhysicsState.Velocity;
                uint space = ship.SpaceId;
                if (!w.Has || w.Space != space) { w.V = v; w.T = now; w.Seen = now; w.Tick = Mathf.Max(Time.fixedDeltaTime, 0.01f); w.Space = space; w.Has = true; w.ASmooth = Vector3.zero; continue; }
                float frame = now - w.Seen;
                w.Seen = now;
                if (v == w.V) continue;
                float since = now - w.T;
                // how often the velocity changes while it is changing: the physics tick as the host runs it
                if (since < 0.25f) w.Tick = Mathf.Lerp(w.Tick, since, 0.1f);
                float dt = Mathf.Clamp(since, 0.004f, Mathf.Max(frame, w.Tick));
                // it sat steady in between: the recent average has settled towards zero
                if (since > dt) w.ASmooth *= Mathf.Exp(-(since - dt) / 0.5f);
                Vector3 a = (v - w.V) / dt;
                Vector3 spike = a - w.ASmooth;
                w.ASmooth = Vector3.Lerp(w.ASmooth, a, 1f - Mathf.Exp(-dt / 0.5f));
                w.V = v; w.T = now;
                if (spike.magnitude > KnockAt && now >= w.Cooldown) { Jolt(ship, t.Value, spike); w.Cooldown = now + 0.4f; }
            }
            if (Time.unscaledTime >= _nextSeats)
            {
                _nextSeats = Time.unscaledTime + 0.25f;
                foreach (var t in _tables) FreeEmptySeats(t.Key, t.Value);
            }
        }

        private static void FreeEmptySeats(TrackedShipServer ship, StatefulPart part)
        {
            BeaconState b = part.State as BeaconState;
            ChessGame g;
            if (b == null || !ChessGame.TryDecode(b.Name, out g)) return;
            bool changed = false;
            if (g.White != 0 && !StillSeated(g.White, ship, part)) { g.White = 0; changed = true; }
            if (g.Black != 0 && !StillSeated(g.Black, ship, part)) { g.Black = 0; changed = true; }
            if (changed) Save(ship, part, g);
        }

        /// <summary>
        /// The ship jolted with acceleration <paramref name="a"/> (its own
        /// space). Everything on the table is thrown the opposite way; tall
        /// pieces go over first, and the harder the jolt the more fall.
        /// </summary>
        public static void Jolt(TrackedShipServer ship, StatefulPart part, Vector3 a)
        {
            BeaconState b = part.State as BeaconState;
            ChessGame g;
            if (b == null) return;
            if (!ChessGame.TryDecode(b.Name, out g)) g = new ChessGame();
            Quaternion tableRot = ship.Rotation * part.LocalRotation;
            Vector3 local = Quaternion.Inverse(tableRot) * a;
            Vector3 flat = new Vector3(local.x, 0f, local.z);
            float side = flat.magnitude, up = Mathf.Abs(local.y);
            // a hard bump from below counts too, with no preferred direction
            float strength = Mathf.Max(side, up * 0.5f);
            if (strength < KnockAt) return;
            Vector3 throwDir = side > 1f ? -flat.normalized : Vector3.forward;
            int dir = Mathf.RoundToInt(Mathf.Atan2(throwDir.x, throwDir.z) * Mathf.Rad2Deg);
            if (dir < 0) dir += 360;
            System.Random rng = new System.Random(unchecked((int)(part.Id * 7919 + g.Seq * 31 + ship.Id)));
            Position pos = g.Pos();
            float over = Mathf.Clamp01((strength - KnockAt) / (KnockAt * 3f));
            ulong before = g.Knocked;
            for (int sq = 0; sq < 64; sq++)
            {
                if (pos.Sq[sq] == 0 || (g.Knocked & (1UL << sq)) != 0) continue;
                float tall = ChessModels.MeshHeight(pos.Sq[sq]) / 0.078f;       // the king is the tallest
                float chance = Mathf.Clamp01(over * (0.35f + 0.9f * tall) + (over > 0.02f ? 0.08f * tall : 0f));
                if (rng.NextDouble() < chance) g.Knocked |= 1UL << sq;
            }
            if (g.Knocked == before) return;
            g.KnockDir = dir;
            int thrown = CountBits(g.Knocked & ~before);
            int dominoes = Dominoes(g, pos, ChessParts.DimsOf(part.Settings.id), before, over, rng);
            KnockEvents++;
            LastDominoes = dominoes;
            LastKnock = "jolt " + strength.ToString("0") + " m/s2 towards " + dir + " deg: " + thrown + " piece(s) over" + (dominoes > 0 ? ", and " + dominoes + " more knocked down by them" : "");
            Save(ship, part, g);
        }

        public static int LastDominoes;

        /// <summary>
        /// Falling pieces knock over what they fall onto: each one the jolt sent over hits the
        /// first standing piece in its way (if any), which goes over too - more likely the heavier
        /// the faller and the higher up it lands - and may take the next one with it.
        /// </summary>
        private static int Dominoes(ChessGame g, Position pos, ChessDims d, ulong before, float over, System.Random rng)
        {
            Queue<int> falling = new Queue<int>();
            for (int sq = 0; sq < 64; sq++) if ((g.Knocked & ~before & (1UL << sq)) != 0) falling.Enqueue(sq);
            int n = 0;
            while (falling.Count > 0)
            {
                int sq = falling.Dequeue();
                float high, angle;
                int hit = ChessPhysics.FirstHit(d, pos.Sq, g.Knocked, sq, ChessPhysics.FallDir(g.KnockDir, sq), out high, out angle);
                if (hit < 0) continue;
                float heavier = ChessPhysics.Mass(pos.Sq[sq]) / ChessPhysics.Mass(pos.Sq[hit]);
                float chance = Mathf.Clamp01(0.2f + 0.45f * heavier * (0.3f + high) + 0.4f * over);
                if (rng.NextDouble() >= chance) continue;   // it stays up: the faller leans on it
                g.Knocked |= 1UL << hit;
                falling.Enqueue(hit);
                n++;
            }
            return n;
        }

        public static int CountBits(ulong v) { int n = 0; while (v != 0) { v &= v - 1; n++; } return n; }
    }
}
