using System;
using System.Collections.Generic;
using System.Linq;
using Chess.Common;
using Players.Interface.Model;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.State;
using Ships;
using Ships.Services;
using Ships.Visuals;
using Ships.Parts.Common;
using UnityEngine;

namespace Chess
{
    public static class ChessParts
    {
        public const ushort TableId = 9400, FloorBoardId = 9401, SmallBoardId = 9402;

        public static bool IsChess(ushort id) { return id == TableId || id == FloorBoardId || id == SmallBoardId; }

        public static ChessDims DimsOf(ushort id) { return id == FloorBoardId ? ChessDims.Floor : id == SmallBoardId ? ChessDims.Small : ChessDims.Table; }

        public static void Register()
        {
            ChessDims t = ChessDims.Table, f = ChessDims.Floor, s = ChessDims.Small;
            PartKit.Register(new PartDef
            {
                Id = TableId,
                AssetName = "ModChessTable",
                Label = "Chess Table",
                Description = "A chess table (2 x 2). Walk up to White's or Black's side and press F to sit and play; from anywhere else you watch. F gets up again.",
                StateClass = "BeaconState",
                Snap = SnappingStyle.PreciseOnFloor,
                Mass = 45f,
                Size = new Vector3(2f, 1f, 2f),
                BoundsCenter = new Vector3(0f, 0.415f, 0f),
                BoundsSize = new Vector3(t.Extent * 2f, 0.83f, t.Extent * 2f),
                VisualType = typeof(ChessTableVisual),
                BuildVisual = ChessModels.BuildTable,
                InteractionType = typeof(ChessInteraction),
                InteractCenter = new Vector3(0f, 0.75f, 0f),
                InteractSize = new Vector3(t.Extent * 2f, 0.3f, t.Extent * 2f),
                Research = new[] { "Spaceship Necessities", "Doors" },
                MenuTab = "Extra",
                Cost = { new KeyValuePair<uint, int>(110u, 8), new KeyValuePair<uint, int>(101u, 2) },
            });
            PartKit.Register(new PartDef
            {
                Id = FloorBoardId,
                AssetName = "ModChessFloorBoard",
                Label = "Chess Board",
                Description = "A chess board that lies on the floor (2 x 2), no table needed. Play it like the table; left click picks it up to move it, game and all.",
                StateClass = "BeaconState",
                Snap = SnappingStyle.PreciseOnFloor,
                Mass = 8f,
                Size = new Vector3(2f, 0.25f, 2f),
                BoundsCenter = new Vector3(0f, f.TopY * 0.5f, 0f),
                BoundsSize = new Vector3(f.Extent * 2f, f.TopY, f.Extent * 2f),
                VisualType = typeof(ChessTableVisual),
                BuildVisual = ChessModels.BuildFloorBoard,
                InteractionType = typeof(ChessInteraction),
                // over the board and the pieces on it (the crosshair must meet it before the base)
                InteractCenter = new Vector3(0f, 0.09f, 0f),
                InteractSize = new Vector3(f.Extent * 2f + 0.01f, 0.18f, f.Extent * 2f + 0.01f),
                Research = new[] { "Spaceship Necessities", "Doors" },
                MenuTab = "Extra",
                Cost = { new KeyValuePair<uint, int>(110u, 4), new KeyValuePair<uint, int>(101u, 1) },
            });
            PartKit.Register(new PartDef
            {
                Id = SmallBoardId,
                AssetName = "ModChessSmallBoard",
                Label = "Small Chess Board",
                Description = "A small chess board for one tile. Plays like the table; left click picks it up to move it, game and all.",
                StateClass = "BeaconState",
                Snap = SnappingStyle.PreciseOnFloor,
                Mass = 2f,
                Size = new Vector3(1f, 0.25f, 1f),
                BoundsCenter = new Vector3(0f, s.TopY * 0.5f, 0f),
                BoundsSize = new Vector3(s.Extent * 2f, s.TopY, s.Extent * 2f),
                VisualType = typeof(ChessTableVisual),
                BuildVisual = ChessModels.BuildSmallBoard,
                InteractionType = typeof(ChessInteraction),
                InteractCenter = new Vector3(0f, 0.05f, 0f),
                InteractSize = new Vector3(s.Extent * 2f + 0.01f, 0.1f, s.Extent * 2f + 0.01f),
                Research = new[] { "Spaceship Necessities", "Doors" },
                MenuTab = "Extra",
                Cost = { new KeyValuePair<uint, int>(110u, 2) },
            });
        }
    }

    /// <summary>
    /// The pieces on a set, laid out from its state. A new move glides the piece
    /// across (lifted over the others), a capture is carried off to the tray beside
    /// the board, and knocked-over pieces topple the way the ship threw them - then
    /// lie loose, rolling and sliding when the ship tilts or lurches, until they are
    /// stood back up.
    /// </summary>
    public class ChessTableVisual : DefaultShipPartVisuals
    {
        public ChessGame Game;
        public ChessDims Dims = ChessDims.Table;
        public static readonly HashSet<ChessTableVisual> All = new HashSet<ChessTableVisual>();

        private class Piece
        {
            public GameObject Go;
            public int Code;
            public int Sq;
            public Vector3 From, To;
            public float T0, Dur;
            public bool Lift;
            public Quaternion RotFrom, RotTo;
            public bool DieAtEnd;
            public Loose Loose;
            public Rock Rock;
            public readonly Body Body = new Body();
        }

        /// <summary>A piece knocked over: waiting to be hit, toppling, then lying loose on the board.</summary>
        private class Loose
        {
            public Vector3 Home, Fall;         // the square it stood on; the way it falls (board plane)
            public Quaternion Facing;
            public float BaseR, Rest;          // its base's radius; how far over it ends up (past 90: its head on the board too)
            public float Angle, Spin;          // toppling: degrees over, and degrees per second
            public bool Started, Down;
            public float WaitUntil;            // waiting for the piece behind to hit it - or it goes by itself then
            public int Bounces;
            // lying: a body that slides, rolls and turns on the board
            public Quaternion Lie;             // how it landed
            public Vector3 C, Vel;             // its middle (centre of mass; board plane) and how it moves
            public float Yaw, Omega;           // turned about the board's up since it landed (degrees), and how fast (degrees/s)
            public float Roll;                 // turned about its own length
            public float Mass, Inertia, Mid;   // Mid: its middle, from the base along its length (m)
            public float Apex;                 // rolling, it turns about the tip of its cone (m from the base; 0: it rolls straight)
        }

        /// <summary>A standing piece rocked onto its base's edge by a lurch too small to knock it over.</summary>
        private class Rock { public Vector3 Dir; public float Angle, Rate; }

        private readonly Dictionary<int, Piece> _board = new Dictionary<int, Piece>();
        private readonly List<Piece> _flying = new List<Piece>();
        private readonly List<GameObject> _tray = new List<GameObject>();
        private readonly List<Body> _trayBodies = new List<Body>();
        private Transform _root, _marks;
        private Material _mat;
        private Vector3 _lastPos, _lastVel, _accel;
        private bool _hasLast;

        public override void SetNetworkState(IStatefulPartState state) { Apply(state, false); }
        public override void ReactToNetworkStateChange(StatefulPartStateChangeEvent change) { Apply(change.NewState, true); }

        private void OnEnable() { All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        private void Ensure()
        {
            if (_root != null) return;
            Dims = ChessDims.OfKind(ChessModels.KindOf(transform));
            _root = new GameObject("Pieces").transform;
            _root.SetParent(transform, false);
            _marks = new GameObject("Marks").transform;
            _marks.SetParent(transform, false);
            _mat = ChessModels.Mat();
        }

        private void Apply(IStatefulPartState state, bool animate)
        {
            BeaconState b = state as BeaconState;
            ChessGame g;
            if (b == null) return;
            if (!ChessGame.TryDecode(b.Name, out g)) g = new ChessGame();   // a new set: the starting position
            ChessGame prev = Game;
            Game = g;
            Ensure();
            Layout(prev, animate && prev != null);
        }

        private GameObject Make(int code)
        {
            GameObject go = new GameObject("Piece" + code);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = ChessModels.PieceMesh(code);
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            go.transform.localScale = Vector3.one * Dims.PieceScale;
            return go;
        }

        /// <summary>
        /// Pieces face the other side - except knights, which are turned to
        /// face their own player's left the way people set them, so both
        /// players see the horse's head side on rather than from behind.
        /// </summary>
        private Quaternion Facing(int code) { return ChessPhysics.Facing(code); }

        private void Layout(ChessGame prev, bool animate)
        {
            Position pos = Game.Pos();
            float now = Time.time;
            _quiet = 0;
            // 1. the move that was just played, animated
            if (animate && Game.Last.Length >= 4 && (Game.Last != prev.Last || Game.Seq != prev.Seq) && Game.Seq > prev.Seq && Game.Fen != prev.Fen)
            {
                int from = Position.SquareOf(Game.Last.Substring(0, 2)), to = Position.SquareOf(Game.Last.Substring(2, 2));
                Piece mover;
                if (_board.TryGetValue(from, out mover))
                {
                    _board.Remove(from);
                    // whatever stood on the target (or behind it, en passant) goes to the tray
                    int capSq = to;
                    Piece victim;
                    if (!_board.TryGetValue(capSq, out victim) && Math.Abs(mover.Code) == Position.P && Position.File(from) != Position.File(to))
                    {
                        capSq = to - 8 * Math.Sign(mover.Code);
                        _board.TryGetValue(capSq, out victim);
                    }
                    if (victim != null)
                    {
                        _board.Remove(capSq);
                        victim.Loose = null; victim.Rock = null;
                        Fly(victim, Dims.TraySpot(victim.Code, CountTray(victim.Code > 0)), 0.5f, true, now + 0.15f, victim.Go.transform.localRotation, Facing(victim.Code), true);
                    }
                    mover.Loose = null; mover.Rock = null;
                    Fly(mover, Dims.SquareCenter(to), 0.45f, true, now, mover.Go.transform.localRotation, Facing(mover.Code));
                    _board[to] = mover;
                    // castling: the rook comes round too
                    if (Math.Abs(mover.Code) == Position.K && Math.Abs(to - from) == 2)
                    {
                        int rf = to > from ? from + 3 : from - 4, rt = to > from ? from + 1 : from - 1;
                        Piece rook;
                        if (_board.TryGetValue(rf, out rook)) { _board.Remove(rf); rook.Loose = null; rook.Rock = null; Fly(rook, Dims.SquareCenter(rt), 0.45f, true, now + 0.2f, rook.Go.transform.localRotation, Facing(rook.Code)); _board[rt] = rook; }
                    }
                }
            }
            // 2. everything else snaps to where the state says - or falls over, or gets stood up
            List<int> fresh = new List<int>();
            for (int sq = 0; sq < 64; sq++)
            {
                int code = pos.Sq[sq];
                Piece p;
                _board.TryGetValue(sq, out p);
                if (code == 0) { if (p != null) { Destroy(p.Go); _board.Remove(sq); } continue; }
                if (p != null && p.Code != code)
                {
                    // promotion: same piece object, new shape
                    p.Code = code;
                    p.Go.GetComponent<MeshFilter>().sharedMesh = ChessModels.PieceMesh(code);
                }
                if (p == null) { p = new Piece { Go = Make(code), Code = code }; _board[sq] = p; p.Go.transform.localPosition = Dims.SquareCenter(sq); p.Go.transform.localRotation = Facing(code); }
                p.Sq = sq;
                bool knocked = (Game.Knocked & (1UL << sq)) != 0;
                if (knocked)
                {
                    if (p.Loose == null)
                    {
                        _flying.Remove(p);
                        p.Rock = null;
                        p.Loose = NewLoose(p, sq, animate);
                        fresh.Add(sq);
                    }
                    continue;
                }
                bool wasLoose = p.Loose != null;
                p.Loose = null;
                Vector3 target = Dims.SquareCenter(sq);
                Quaternion rot = Facing(code);
                if (_flying.Contains(p)) { p.To = target; p.RotTo = rot; continue; }
                if ((p.Go.transform.localPosition - target).sqrMagnitude > 1e-6f || Quaternion.Angle(p.Go.transform.localRotation, rot) > 0.5f)
                {
                    // stood back up (or moved): lifted back onto its square
                    p.Rock = null;
                    if (animate || wasLoose) Fly(p, target, wasLoose ? 0.5f : 0.35f, wasLoose, now, p.Go.transform.localRotation, rot);
                    else { p.Go.transform.localPosition = target; p.Go.transform.localRotation = rot; }
                }
            }
            // 3. dominoes: a piece that the one behind it falls onto waits to be hit (the host knocked it over for that)
            if (animate && fresh.Count > 0)
            {
                ulong freshMask = 0;
                foreach (int sq in fresh) freshMask |= 1UL << sq;
                foreach (int sq in fresh)
                {
                    float high, angle;
                    int hit = ChessPhysics.FirstHit(Dims, pos.Sq, Game.Knocked & ~freshMask, sq, _board[sq].Loose.Fall, out high, out angle);
                    Piece q;
                    if (hit >= 0 && (freshMask & (1UL << hit)) != 0 && _board.TryGetValue(hit, out q) && q.Loose != null) { q.Loose.Started = false; q.Loose.WaitUntil = now + 1.5f; }
                }
            }
            if (!animate) RebuildTray();
            else Invoke(nameof(RebuildTray), 0.8f);
        }

        private Loose NewLoose(Piece p, int sq, bool animate)
        {
            float s = Dims.PieceScale, h = Dims.Height(p.Code);
            Loose l = new Loose { Home = Dims.SquareCenter(sq), Fall = ChessPhysics.FallDir(Game.KnockDir, sq), Facing = Facing(p.Code), Started = true };
            l.BaseR = ChessPhysics.BaseRadius(p.Code) * s;
            l.Rest = ChessPhysics.RestAngle(p.Code, l.Fall, l.Facing);
            l.Spin = (150f + (sq % 5) * 18f) / Mathf.Sqrt(s);
            l.Mass = ChessPhysics.Mass(p.Code);
            l.Mid = h * 0.38f;
            l.Inertia = l.Mass * (h * h / 12f + l.BaseR * l.BaseR / 4f);
            float tilt = (l.Rest - 90f) * Mathf.Deg2Rad;
            l.Apex = Math.Abs(p.Code) != Position.N && tilt > 0.01f ? l.BaseR / Mathf.Tan(tilt) : 0f;
            p.Loose = l;
            if (!animate) Land(p, l, 0f);   // loaded already fallen: lying where it fell
            else { p.Go.transform.localPosition = l.Home; p.Go.transform.localRotation = l.Facing; }
            return l;
        }

        private void Fly(Piece p, Vector3 to, float dur, bool lift, float t0, Quaternion rotFrom, Quaternion rotTo, bool die = false)
        {
            p.From = p.Go.transform.localPosition;
            p.To = to; p.Dur = dur; p.Lift = lift; p.T0 = t0; p.RotFrom = rotFrom; p.RotTo = rotTo; p.DieAtEnd = die;
            if (!_flying.Contains(p)) _flying.Add(p);
        }

        private void Fly(Piece p, Vector3 to, float dur, bool lift, float t0, Quaternion rotFrom, Quaternion rotTo) { Fly(p, to, dur, lift, t0, rotFrom, rotTo, false); }

        private void Update()
        {
            float now = Time.time, dt = Mathf.Min(Time.deltaTime, 0.05f);
            SenseMotion(dt);
            for (int i = _flying.Count - 1; i >= 0; i--)
            {
                Piece p = _flying[i];
                if (p.Go == null) { _flying.RemoveAt(i); continue; }
                float t = Mathf.Clamp01((now - p.T0) / p.Dur);
                if (now < p.T0) continue;
                float e = t * t * (3f - 2f * t);
                Vector3 pos = Vector3.Lerp(p.From, p.To, e);
                if (p.Lift) pos.y += Mathf.Sin(t * Mathf.PI) * 0.12f * Dims.View;
                p.Go.transform.localPosition = pos;
                p.Go.transform.localRotation = Quaternion.Slerp(p.RotFrom, p.RotTo, p.Lift ? e : t * t);
                if (t >= 1f)
                {
                    _flying.RemoveAt(i);
                    if (p.DieAtEnd) Destroy(p.Go);
                }
            }
            if (dt <= 0f) return;
            StepRocking(dt);
            foreach (var kv in _board) if (kv.Value.Loose != null && kv.Value.Go != null) { StepLoose(now, dt); break; }
        }

        // ---- the pieces touch each other ---------------------------------------------------------
        // Every piece is a few capsules and discs that follow its drawn shape (ChessPhysics). A toppling
        // piece stops where it meets another: a standing one it leans on, one that is falling or waiting
        // to fall it pushes over, one lying it shoves along. A lying piece is a body on the board: the
        // ship's lurches and tilt slide and roll it, and pieces that meet are held apart and trade their
        // momentum (heavier pieces push lighter ones further; a knock off-centre turns them).

        private readonly List<Piece> _lying = new List<Piece>(), _tipping = new List<Piece>();
        private readonly Body _probe = new Body();
        private int _quiet;
        /// <summary>For the self test: pushes between lying pieces, and pieces set falling by the one behind them.</summary>
        public int Contacts, Dominoes;

        private float Slop { get { return 0.0003f * Dims.PieceScale; } }

        private void Sync(Piece p)
        {
            Transform t = p.Go.transform;
            ChessPhysics.SetPose(p.Body, p.Code, t.localPosition, t.localRotation, t.localScale.x);
        }

        private void StepLoose(float now, float dt)
        {
            _lying.Clear(); _tipping.Clear();
            bool busy = false;
            foreach (Piece p in _board.Values)
            {
                if (p.Go == null) continue;
                Sync(p);
                Loose l = p.Loose;
                if (l == null) continue;
                if (!l.Started && now >= l.WaitUntil) l.Started = true;
                if (l.Down) _lying.Add(p);
                else { busy = true; if (l.Started) _tipping.Add(p); }
            }
            // 1. toppling
            foreach (Piece p in _tipping) Tip(p, dt);
            // 2. lying: moved by the ship, then kept apart - in small steps when they move fast, so none
            // passes into another between two looks (at most about a centimetre a step on the table)
            float fastest = 0f;
            foreach (Piece p in _lying) fastest = Mathf.Max(fastest, p.Loose.Vel.magnitude);
            int steps = Mathf.Clamp(Mathf.CeilToInt(fastest * dt / (0.07f * Dims.Square)), 1, 10);
            float h = dt / steps;
            for (int step = 0; step < steps; step++)
            {
                foreach (Piece p in _lying) if (Drive(p, h)) busy = true;
                if (!busy && _quiet >= 30) continue;
                // (the board's edge in every pass: pieces squeezed against it are sorted out together)
                for (int iter = 0; iter < 4; iter++)
                {
                    foreach (Piece p in _lying) Separate(p);
                    foreach (Piece p in _lying) KeepOn(p);
                }
            }
            _quiet = busy ? 0 : _quiet + 1;
            foreach (Piece p in _lying) Place(p);
        }

        /// <summary>Would the piece, tipped this far, go into another? (the deepest one it would, or null for the tray)</summary>
        // each obstacle's gap where the toppling piece is now: only what it moves INTO stops it
        // (not a piece resting on its back, nor one it is already leaving)
        private readonly Dictionary<object, float> _gapNow = new Dictionary<object, float>();

        private void GapsNow(Piece p)
        {
            _gapNow.Clear();
            Loose l = p.Loose;
            Vector3 pos; Quaternion rot, n2 = Quaternion.identity;
            ChessPhysics.TipPose(l.Home, l.Fall, l.BaseR, l.Angle, l.Facing, out pos, out rot);
            ChessPhysics.SetPose(_probe, p.Code, pos, rot, p.Go.transform.localScale.x);
            Vector3 n, at;
            foreach (Piece q in _board.Values) if (q != p && q.Go != null) _gapNow[q] = ChessPhysics.Gap(_probe, q.Body, out n, out at);
            foreach (Body b in _trayBodies) _gapNow[b] = ChessPhysics.Gap(_probe, b, out n, out at);
        }

        /// <summary>Would the piece, tipped this far, go into another? (the deepest one it would, or null for the tray)</summary>
        private bool Blocked(Piece p, float angle, out Piece hit)
        {
            Loose l = p.Loose;
            Vector3 pos; Quaternion rot;
            ChessPhysics.TipPose(l.Home, l.Fall, l.BaseR, angle, l.Facing, out pos, out rot);
            ChessPhysics.SetPose(_probe, p.Code, pos, rot, p.Go.transform.localScale.x);
            hit = null;
            float worst = -Slop;
            bool blocked = false;
            Vector3 n, at;
            float now;
            foreach (Piece q in _board.Values)
            {
                if (q == p || q.Go == null) continue;
                float g = ChessPhysics.Gap(_probe, q.Body, out n, out at);
                // a loose piece behind it (resting on its back) doesn't hold it: it is lifted off instead (Lift)
                if (g < worst && q.Loose != null && q.Loose.Started && Vector3.Dot(at - l.Home, l.Fall) < 0f) continue;
                if (g < worst && !(_gapNow.TryGetValue(q, out now) && g >= now - 1e-6f)) { worst = g; hit = q; blocked = true; }
            }
            foreach (Body b in _trayBodies)
            {
                float g = ChessPhysics.Gap(_probe, b, out n, out at);
                if (g < worst && !(_gapNow.TryGetValue(b, out now) && g >= now - 1e-6f)) { worst = g; hit = null; blocked = true; }
            }
            return blocked;
        }

        private void Tip(Piece p, float dt)
        {
            Loose l = p.Loose;
            // tipping over its base's edge, faster and faster, with a small bounce as it lands
            // (about a third of a second from the knock to the board; the small set's pieces are quicker)
            l.Spin += (700f + 900f * Mathf.Sin(Mathf.Clamp(l.Angle, 0f, 90f) * Mathf.Deg2Rad)) / Dims.PieceScale * dt;
            float want = Mathf.Clamp(l.Angle + l.Spin * dt, 0f, l.Rest);
            Piece hit;
            if (want > l.Angle) GapsNow(p);
            if (want > l.Angle && Blocked(p, want, out hit))
            {
                // as far as it goes before it touches
                float lo = l.Angle, hi = want;
                for (int i = 0; i < 7; i++) { float mid = (lo + hi) * 0.5f; Piece h2; if (Blocked(p, mid, out h2)) hi = mid; else lo = mid; }
                l.Angle = lo;
                Struck(p, hit);
            }
            else l.Angle = want;
            if (l.Angle <= 0f && l.Spin < 0f) l.Spin = 0f;
            Vector3 pos; Quaternion rot;
            ChessPhysics.TipPose(l.Home, l.Fall, l.BaseR, l.Angle, l.Facing, out pos, out rot);
            p.Go.transform.localPosition = pos;
            p.Go.transform.localRotation = rot;
            Sync(p);
            Lift(p);
            if (l.Angle >= l.Rest - 0.01f && l.Spin > 0f)
            {
                if (l.Bounces < 1 && l.Spin > 120f) { l.Spin = -l.Spin * 0.22f; l.Bounces++; }
                else Land(p, l, 0.08f * Dims.View);   // it keeps going a little the way it fell
            }
        }

        /// <summary>
        /// Toppling pieces leaning on this one from behind are lifted back as far as they must be
        /// (its base's back edge rises as it tips over). Lying ones are shoved off by Separate.
        /// </summary>
        private void Lift(Piece p)
        {
            Vector3 n, at, pos; Quaternion rot;
            foreach (Piece q in _tipping)
            {
                if (q == p || q.Go == null || q.Loose == null || q.Loose.Down) continue;
                Loose o = q.Loose;
                if (ChessPhysics.Gap(q.Body, p.Body, out n, out at) >= -Slop) continue;
                float lo = 0f, hi = o.Angle;
                for (int i = 0; i < 8; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    ChessPhysics.TipPose(o.Home, o.Fall, o.BaseR, mid, o.Facing, out pos, out rot);
                    ChessPhysics.SetPose(_probe, q.Code, pos, rot, q.Go.transform.localScale.x);
                    if (ChessPhysics.Gap(_probe, p.Body, out n, out at) >= -Slop) lo = mid; else hi = mid;
                }
                o.Angle = lo;
                o.Spin = Mathf.Min(o.Spin, 0f);
                ChessPhysics.TipPose(o.Home, o.Fall, o.BaseR, o.Angle, o.Facing, out pos, out rot);
                q.Go.transform.localPosition = pos;
                q.Go.transform.localRotation = rot;
                Sync(q);
            }
        }

        /// <summary>A toppling piece has met <paramref name="q"/> (null: the tray).</summary>
        private void Struck(Piece p, Piece q)
        {
            Loose l = p.Loose;
            if (q == null || q.Loose == null) { l.Spin = 0f; return; }             // standing: it leans on it
            Loose o = q.Loose;
            if (!o.Down)
            {
                // falling, or about to: pushed over faster (a heavy piece more so)
                if (!o.Started) { o.Started = true; Dominoes++; }
                o.Spin = Mathf.Max(o.Spin, l.Spin * Mathf.Clamp(0.55f * l.Mass / o.Mass + 0.25f, 0.4f, 1.1f));
                l.Spin *= 0.35f;
                return;
            }
            // lying: shoved along the way it falls
            float tip = l.Spin * Mathf.Deg2Rad * Dims.Height(p.Code) * 0.6f;
            o.Vel += l.Fall * tip * (l.Mass / (l.Mass + o.Mass));
            l.Spin *= 0.5f;
            Contacts++;
            _quiet = 0;
        }

        private void Land(Piece p, Loose l, float speed)
        {
            Vector3 pos; Quaternion rot;
            ChessPhysics.TipPose(l.Home, l.Fall, l.BaseR, l.Rest, l.Facing, out pos, out rot);
            l.Down = true; l.Started = true; l.Angle = l.Rest; l.Spin = 0f;
            l.Lie = rot; l.Yaw = 0f; l.Omega = 0f; l.Roll = 0f;
            l.C = pos + rot * Vector3.up * l.Mid; l.C.y = 0f;
            l.Vel = l.Fall * speed;
            _quiet = 0;
            Place(p);
        }

        /// <summary>A lying piece, from its state: where its origin is and how it is turned.</summary>
        private void LyingPose(Loose l, out Vector3 pos, out Quaternion rot)
        {
            Quaternion q = Quaternion.AngleAxis(l.Yaw, Vector3.up) * l.Lie;
            Vector3 axis = q * Vector3.up;
            rot = Quaternion.AngleAxis(l.Roll, axis) * q;
            Vector3 o = l.C - axis * l.Mid;
            float surface = Mathf.Abs(o.x) <= Dims.Half + Dims.Frame && Mathf.Abs(o.z) <= Dims.Half + Dims.Frame ? Dims.BoardY : Dims.TopY;
            pos = new Vector3(o.x, surface + l.BaseR * Mathf.Sin(l.Rest * Mathf.Deg2Rad), o.z);
        }

        private void Place(Piece p)
        {
            Vector3 pos; Quaternion rot;
            LyingPose(p.Loose, out pos, out rot);
            p.Go.transform.localPosition = pos;
            p.Go.transform.localRotation = rot;
            ChessPhysics.SetPose(p.Body, p.Code, pos, rot, p.Go.transform.localScale.x);
        }

        /// <summary>
        /// A lying piece rolls across its length and slides along it, with the ship's tilt and lurches,
        /// slowed by friction. A piece rolls in a curve, being thicker at its base (a knight slides).
        /// Returns whether it is moving.
        /// </summary>
        private bool Drive(Piece p, float dt)
        {
            Loose l = p.Loose;
            Quaternion q = Quaternion.AngleAxis(l.Yaw, Vector3.up) * l.Lie;
            Vector3 along = Vector3.ProjectOnPlane(q * Vector3.up, Vector3.up).normalized;
            Vector3 across = Vector3.Cross(Vector3.up, along);
            bool knight = Math.Abs(p.Code) == Position.N;
            Vector3 force = PlaneForce;
            Vector3 acc = across * Vector3.Dot(force, across) * (knight ? 0.25f : 0.55f) + along * Vector3.Dot(force, along) * 0.25f;
            bool pushed = acc.magnitude >= 0.4f;
            float fric = 1.8f + (pushed ? 0f : 4f);                // at rest it stays at rest
            l.Vel += (acc - l.Vel * fric) * dt;
            l.Vel.y = 0f;
            // (sliding on the board, nothing goes faster than this - a harder throw would send it off the table)
            l.Vel = Vector3.ClampMagnitude(l.Vel, 15f * Dims.Square);
            if (l.Vel.magnitude < 0.002f && !pushed) l.Vel = Vector3.zero;
            float lat = Vector3.Dot(l.Vel, across);
            if (!knight && l.Apex > l.Mid * 1.05f && Mathf.Abs(lat) > 0.001f)
            {
                // rolling on a cone: its middle goes round the cone's tip
                Vector3 r = along * (l.Mid - l.Apex);
                float w = Vector3.Cross(r, across * lat).y / r.sqrMagnitude;
                l.Omega = Mathf.Lerp(l.Omega, w * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt / 0.06f));
            }
            else l.Omega *= Mathf.Exp(-(pushed ? 2f : 6f) * dt);
            if (Mathf.Abs(l.Omega) < 0.5f && !pushed) l.Omega = 0f;
            l.C += l.Vel * dt;
            l.Yaw += l.Omega * dt;
            if (!knight) l.Roll -= lat * dt / Mathf.Max(0.001f, l.BaseR * 0.8f) * Mathf.Rad2Deg;
            Place(p);
            return l.Vel.sqrMagnitude > 0f || l.Omega != 0f;
        }

        /// <summary>A lying piece moved out of every other piece it went into, trading momentum with it.</summary>
        private void Separate(Piece p)
        {
            Loose a = p.Loose;
            Vector3 n, at;
            foreach (Piece q in _board.Values)
            {
                if (q == p || q.Go == null) continue;
                float g = ChessPhysics.Gap(p.Body, q.Body, out n, out at);
                if (g >= -Slop) continue;
                Loose b = q.Loose != null && q.Loose.Down ? q.Loose : null;   // a lying piece moves too; anything else stands firm
                Push(p, a, q, b, -g, n, at);
            }
            foreach (Body tb in _trayBodies)
            {
                float g = ChessPhysics.Gap(p.Body, tb, out n, out at);
                if (g < -Slop) Push(p, a, null, null, -g, n, at);
            }
        }

        private void Push(Piece p, Loose a, Piece q, Loose b, float depth, Vector3 n, Vector3 at)
        {
            Vector3 nh = new Vector3(n.x, 0f, n.z);
            if (nh.sqrMagnitude < 0.04f)
            {
                // meeting (nearly) on top of each other: apart sideways, from the other's middle
                Vector3 c = q != null ? q.Body.Center : at;
                nh = new Vector3(p.Body.Center.x - c.x, 0f, p.Body.Center.z - c.z);
                if (nh.sqrMagnitude < 1e-10f) nh = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(p.Go.transform.up, Vector3.up));
            }
            // (met on a slant, they must go further apart sideways than the depth itself)
            float slant = Mathf.Clamp(new Vector3(n.x, 0f, n.z).magnitude, 0.3f, 1f);
            depth /= slant;
            nh.Normalize();
            float wa = b != null ? b.Mass / (a.Mass + b.Mass) : 1f;
            a.C += nh * (depth * wa);
            if (b != null) b.C -= nh * (depth * (1f - wa));
            // the speed they had into each other is traded (a little bounce), and a push off-centre turns them
            Vector3 ra = at - a.C; ra.y = 0f;
            Vector3 va = a.Vel + Vector3.Cross(Vector3.up * (a.Omega * Mathf.Deg2Rad), ra);
            Vector3 vb = Vector3.zero, rb = Vector3.zero;
            if (b != null) { rb = at - b.C; rb.y = 0f; vb = b.Vel + Vector3.Cross(Vector3.up * (b.Omega * Mathf.Deg2Rad), rb); }
            float vn = Vector3.Dot(va - vb, nh);
            if (vn < 0f)
            {
                float ka = Vector3.Cross(ra, nh).y, kb = b != null ? Vector3.Cross(rb, nh).y : 0f;
                float den = 1f / a.Mass + ka * ka / a.Inertia + (b != null ? 1f / b.Mass + kb * kb / b.Inertia : 0f);
                float j = -(1f + 0.3f) * vn / den;
                a.Vel += nh * (j / a.Mass);
                a.Omega += ka * j / a.Inertia * Mathf.Rad2Deg;
                if (b != null)
                {
                    b.Vel -= nh * (j / b.Mass);
                    b.Omega -= kb * j / b.Inertia * Mathf.Rad2Deg;
                    Contacts++;
                }
                _quiet = 0;
            }
            Place(p);
            if (q != null && b != null) Place(q);
        }

        /// <summary>Everything of a lying piece stays on the surface: the edge bumps it back.</summary>
        private void KeepOn(Piece p)
        {
            Loose l = p.Loose;
            float lim = Dims.Extent - 0.002f;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Prim w in p.Body.Prims)
            {
                foreach (Vector3 v in w.Disc ? new[] { w.A } : new[] { w.A, w.B })
                {
                    minX = Mathf.Min(minX, v.x - w.R); maxX = Mathf.Max(maxX, v.x + w.R);
                    minZ = Mathf.Min(minZ, v.z - w.R); maxZ = Mathf.Max(maxZ, v.z + w.R);
                }
            }
            bool moved = false;
            if (maxX > lim) { l.C.x -= maxX - lim; if (l.Vel.x > 0f) l.Vel.x = -l.Vel.x * 0.3f; moved = true; }
            else if (minX < -lim) { l.C.x += -lim - minX; if (l.Vel.x < 0f) l.Vel.x = -l.Vel.x * 0.3f; moved = true; }
            if (maxZ > lim) { l.C.z -= maxZ - lim; if (l.Vel.z > 0f) l.Vel.z = -l.Vel.z * 0.3f; moved = true; }
            else if (minZ < -lim) { l.C.z += -lim - minZ; if (l.Vel.z < 0f) l.Vel.z = -l.Vel.z * 0.3f; moved = true; }
            if (moved) Place(p);
        }

        // ---- standing pieces rock --------------------------------------------------------------------

        public const float MaxRock = 10f;

        /// <summary>
        /// A standing piece rocks onto its base's edge once the push along the board outweighs its
        /// own weight's hold (about 1 g for these pieces - the lurch that staggers a person), and
        /// clacks back down after. Only the host's jolt rule knocks one right over.
        /// </summary>
        private void StepRocking(float dt)
        {
            float push0 = PlaneForce.magnitude, press = Press;
            foreach (Piece p in _board.Values)
            {
                if (p.Go == null || p.Loose != null || _flying.Contains(p)) { p.Rock = null; continue; }
                float rb = ChessPhysics.BaseRadius(p.Code) * Dims.PieceScale, hc = Dims.Height(p.Code) * 0.38f;
                if (p.Rock == null)
                {
                    if (press < 0.5f || push0 * hc <= press * rb) continue;
                    p.Rock = new Rock { Dir = PlaneForce / push0 };
                }
                Rock r = p.Rock;
                float push = Mathf.Max(0f, Vector3.Dot(PlaneForce, r.Dir)), th = r.Angle * Mathf.Deg2Rad;
                float alpha = (push * (hc * Mathf.Cos(th) + rb * Mathf.Sin(th)) - press * (rb * Mathf.Cos(th) - hc * Mathf.Sin(th))) / (1.3f * (hc * hc + rb * rb));
                r.Rate += alpha * Mathf.Rad2Deg * dt;
                r.Rate *= Mathf.Exp(-1.5f * dt);
                r.Angle += r.Rate * dt;
                if (r.Angle >= MaxRock) { r.Angle = MaxRock; if (r.Rate > 0f) r.Rate = 0f; }
                Vector3 home = Dims.SquareCenter(p.Sq);
                if (r.Angle <= 0f)
                {
                    r.Angle = 0f;
                    if (r.Rate < -25f) r.Rate = -r.Rate * 0.25f;   // a clack, and a smaller rock
                    else { p.Rock = null; p.Go.transform.localPosition = home; p.Go.transform.localRotation = Facing(p.Code); continue; }
                }
                Vector3 pos; Quaternion rot;
                ChessPhysics.TipPose(home, r.Dir, rb, r.Angle, Facing(p.Code), out pos, out rot);
                p.Go.transform.localPosition = pos;
                p.Go.transform.localRotation = rot;
            }
        }

        public int Rocking { get { return _board.Values.Count(p => p.Rock != null); } }
        public float MostRock { get { float m = 0f; foreach (Piece p in _board.Values) if (p.Rock != null) m = Mathf.Max(m, p.Rock.Angle); return m; } }

        // ---- how the set is being thrown about -------------------------------------------------------

        /// <summary>What a piece feels along the board (gravity on a tilted ship, and the ship's lurches the other way), and into it.</summary>
        public Vector3 PlaneForce;
        public float Press = 9.8f;
        /// <summary>Where the push came from last: Momentum's own reading of the ship, or the set's own motion.</summary>
        public bool FromMomentum;

        private Transform _rootT, _shipT;
        private uint _rootId, _shipId;
        private float _nextFind;

        /// <summary>The ship this set is on, and the root of its docked group (found from the ship visuals it hangs under).</summary>
        private void FindShip()
        {
            if (_shipT != null && Time.time < _nextFind) return;
            _nextFind = Time.time + 2f;
            _shipT = null; _rootT = null;
            try
            {
                ShipsClientTracker ships = Svc.Get<ShipsClientTracker>();
                if (ships == null) return;
                int best = -1;
                foreach (TrackedShipClient s in ships.Ships)
                {
                    Transform t;
                    if (!ships.TryGetShipVisualTransform(s.Id, out t) || t == null) continue;
                    if (!transform.IsChildOf(t))
                    {
                        // (the part visuals may hang elsewhere: then under the ship's part visuals)
                        ShipPartVisuals spv;
                        if (!ships.TryGetShipVisuals(s.Id, out spv) || spv == null || !transform.IsChildOf(spv.transform)) continue;
                    }
                    int depth = 0;
                    for (Transform x = t; x != null; x = x.parent) depth++;
                    if (depth > best) { best = depth; _shipT = t; _shipId = s.Id; }
                }
                IShipStateRead root;
                if (_shipT != null && ships.TryGetRootShip(_shipId, out root) && root != null)
                {
                    Transform rt;
                    _rootId = root.Id;
                    if (ships.TryGetShipVisualTransform(root.Id, out rt)) _rootT = rt;
                }
            }
            catch { _shipT = null; _rootT = null; }
        }

        /// <summary>The ship under this set (for the chess camera: its hull is where the ceiling is).</summary>
        public bool TryShip(out IShipStateRead ship, out Transform shipT)
        {
            FindShip();
            ship = null; shipT = _shipT;
            ShipsClientTracker ships = Svc.Get<ShipsClientTracker>();
            return _shipT != null && ships != null && ships.TryGetShip(_shipId, out ship) && ship != null;
        }

        private void SenseMotion(float dt)
        {
            if (dt <= 0f) return;
            Vector3 p = transform.position;
            // where it would be, going on as it was: a set that turns up far from there was placed there
            // (the world's origin moved, the ship was put somewhere) - that is no motion
            Vector3 expected = _lastPos + _lastVel * dt;
            if (_hasLast && (p - expected).magnitude < 0.2f + 30f * dt)
            {
                Vector3 v = (p - _lastPos) / dt;
                // (smoothed, not capped: a sudden change of the ship's speed reaches the pieces whole,
                // however few frames it happened in)
                Vector3 a = (v - _lastVel) / dt;
                _accel += (a - _accel) * (1f - Mathf.Exp(-dt / 0.05f));
                _lastVel = v;
            }
            else _accel *= Mathf.Exp(-dt / 0.05f);
            _lastPos = p; _hasLast = true;
            // with Momentum: the very push it gives the people aboard, turning included
            Vector3 felt = -_accel;
            FromMomentum = false;
            if (MomentumLink.Present)
            {
                FindShip();
                Vector3 m;
                if (_rootT != null && (MomentumLink.TryFelt(false, _rootId, _rootT.InverseTransformPoint(p), out m) || MomentumLink.TryFelt(true, _rootId, _rootT.InverseTransformPoint(p), out m)))
                {
                    felt = _rootT.TransformDirection(m);
                    FromMomentum = true;
                }
            }
            // a loose piece feels gravity along the board (a tilted ship) and the ship's lurches, the other way
            Vector3 gWorld = Physics.gravity.sqrMagnitude > 0.01f ? Physics.gravity : -transform.up * 9.8f;
            Vector3 f = transform.InverseTransformDirection(gWorld + felt);
            PlaneForce = new Vector3(f.x, 0f, f.z);
            Press = -f.y;
        }

        // ---- for the self test --------------------------------------------------------------------

        public int LooseCount { get { return _board.Values.Count(p => p.Loose != null); } }
        public int LyingCount { get { return _board.Values.Count(p => p.Loose != null && p.Loose.Down); } }
        /// <summary>Knocked pieces stopped part way over, leaning on another.</summary>
        public int LeaningCount { get { return _board.Values.Count(p => p.Loose != null && !p.Loose.Down && p.Loose.Started && p.Loose.Angle > 1f); } }

        /// <summary>Where each knocked piece is now (lying, leaning or still falling).</summary>
        public IEnumerable<Vector3> LoosePositions() { return _board.Values.Where(p => p.Loose != null && p.Go != null).OrderBy(p => p.Sq).Select(p => p.Go.transform.localPosition); }

        /// <summary>The deepest two pieces (one of them knocked over) go into each other now, by the shapes they touch with (m).</summary>
        public float DeepestOverlap(out string where)
        {
            where = "";
            float worst = 0f;
            List<Piece> all = _board.Values.Where(p => p.Go != null).ToList();
            foreach (Piece p in all) Sync(p);
            Vector3 n, at;
            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                {
                    if (all[i].Loose == null && all[j].Loose == null) continue;
                    float g = ChessPhysics.Gap(all[i].Body, all[j].Body, out n, out at);
                    if (-g > worst) { worst = -g; where = Position.SquareName(all[i].Sq) + "/" + Position.SquareName(all[j].Sq); }
                }
                if (all[i].Loose == null) continue;
                foreach (Body tb in _trayBodies)
                {
                    float g = ChessPhysics.Gap(all[i].Body, tb, out n, out at);
                    if (-g > worst) { worst = -g; where = Position.SquareName(all[i].Sq) + "/tray"; }
                }
            }
            return worst;
        }

        /// <summary>Lying pieces overlapping another (self test): pairs more than a millimetre into each other.</summary>
        public int Overlaps()
        {
            List<Piece> l = _board.Values.Where(p => p.Loose != null && p.Loose.Down && p.Go != null).ToList();
            foreach (Piece p in l) Sync(p);
            int n = 0;
            Vector3 nn, at;
            for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                    if (ChessPhysics.Gap(l[i].Body, l[j].Body, out nn, out at) < -0.001f) n++;
            return n;
        }

        /// <summary>Every piece object of the set, drawn (the board's and the tray's), for measuring in the self test.</summary>
        public IEnumerable<Transform> PieceObjects() { return _board.Values.Where(p => p.Go != null).Select(p => p.Go.transform).Concat(_tray.Where(g => g != null).Select(g => g.transform)); }
        /// <summary>The i-th knocked piece (in the order of LoosePositions), as the self test reports it.</summary>
        public string LooseInfo(int i)
        {
            Piece p = _board.Values.Where(x => x.Loose != null && x.Go != null).OrderBy(x => x.Sq).ElementAtOrDefault(i);
            if (p == null) return "?";
            Loose l = p.Loose;
            return Position.SquareName(p.Sq) + " " + (l.Down ? "lying" : "at " + l.Angle.ToString("F0") + " deg") + ", speed " + (l.Vel.magnitude * 1000f).ToString("F0") + " mm/s, turning " + l.Omega.ToString("F0") + " deg/s, push " + PlaneForce.magnitude.ToString("F2");
        }

        public HashSet<Transform> LooseObjects() { return new HashSet<Transform>(_board.Values.Where(p => p.Go != null && p.Loose != null).Select(p => p.Go.transform)); }
        public string NameOf(Transform t) { Piece p = _board.Values.FirstOrDefault(x => x.Go != null && x.Go.transform == t); return p != null ? Position.SquareName(p.Sq) : "tray"; }
        /// <summary>How far over the piece on this square is (0 standing, about 90+ lying), or -1.</summary>
        public float TippedAt(int sq) { Piece p; return _board.TryGetValue(sq, out p) && p.Go != null ? Vector3.Angle(p.Go.transform.localRotation * Vector3.up, Vector3.up) : -1f; }

        public bool Animating { get { return _flying.Count > 0 || _board.Values.Any(p => p.Loose != null && !p.Loose.Down && (!p.Loose.Started || p.Loose.Spin != 0f)); } }

        // ---- the tray of taken pieces, beside the board ------------------------------------

        private static readonly int[] StartCount = { 0, 8, 2, 2, 2, 1, 1 };

        private int CountTray(bool white) { return _tray.Count(g => g != null && (g.name.StartsWith("TrayW") == white)); }

        private void RebuildTray()
        {
            foreach (GameObject g in _tray) if (g != null) Destroy(g);
            _tray.Clear();
            _trayBodies.Clear();
            _quiet = 0;
            if (Game == null) return;
            Position pos = Game.Pos();
            foreach (bool white in new[] { true, false })
            {
                int n = 0;
                foreach (int t in new[] { 5, 4, 3, 2, 1 })
                {
                    int code = white ? t : -t;
                    int onBoard = pos.Sq.Count(v => v == code);
                    for (int k = onBoard; k < StartCount[t]; k++)
                    {
                        GameObject go = Make(code);
                        go.name = (white ? "TrayW" : "TrayB") + t;
                        go.transform.localPosition = Dims.TraySpot(code, n++);
                        go.transform.localRotation = Facing(code);
                        go.transform.localScale = Vector3.one * (0.85f * Dims.PieceScale);
                        _tray.Add(go);
                        Body tb = new Body();
                        ChessPhysics.SetPose(tb, code, go.transform.localPosition, go.transform.localRotation, 0.85f * Dims.PieceScale);
                        _trayBodies.Add(tb);
                    }
                }
            }
        }

        public int PiecesOnBoard { get { return _board.Count; } }
        public int PiecesInTray { get { return _tray.Count(g => g != null); } }

        public Transform PieceAt(int sq) { Piece p; return _board.TryGetValue(sq, out p) && p.Go != null ? p.Go.transform : null; }

        // ---- marks for the player at the board ---------------------------------------------

        private readonly List<GameObject> _markObjs = new List<GameObject>();
        private static readonly Dictionary<float, Mesh> _discs = new Dictionary<float, Mesh>(), _squares = new Dictionary<float, Mesh>();

        public void ShowMarks(int selected, IEnumerable<int> targets, int hover)
        {
            Ensure();
            foreach (GameObject g in _markObjs) if (g != null) Destroy(g);
            _markObjs.Clear();
            if (Game != null && Game.Last.Length >= 4)
            {
                Mark(Position.SquareOf(Game.Last.Substring(0, 2)), Square(), new Color(0.95f, 0.8f, 0.2f), 0.55f);
                Mark(Position.SquareOf(Game.Last.Substring(2, 2)), Square(), new Color(0.95f, 0.8f, 0.2f), 0.55f);
            }
            if (hover >= 0) Mark(hover, Square(), new Color(0.6f, 0.85f, 1f), 0.5f);
            if (selected >= 0) Mark(selected, Square(), new Color(0.3f, 1f, 0.45f), 0.9f);
            if (targets != null) foreach (int t in targets) Mark(t, Disc(), new Color(0.3f, 1f, 0.45f), 1f);
        }

        private void Mark(int sq, Mesh mesh, Color c, float strength)
        {
            GameObject g = new GameObject("Mark" + sq);
            g.transform.SetParent(_marks, false);
            g.transform.localPosition = Dims.SquareCenter(sq) + Vector3.up * 0.0008f;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = Looks.Get("chessmark" + ColorUtility.ToHtmlStringRGB(c) + strength.ToString("0.0"), Color.white, 1f, 0f, c * strength);
            _markObjs.Add(g);
        }

        private Mesh Square()
        {
            Mesh m;
            if (_squares.TryGetValue(Dims.Square, out m) && m != null) return m;
            MeshKit mk = new MeshKit { Pal = Palette.Yellow };
            float h = Dims.Square * 0.5f, i = h - 0.075f * Dims.Square;
            // a frame, not a filled square, so the square's own colour still shows
            mk.Quad(new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(-i, 0, h), new Vector3(-i, 0, -h), MeshKit.CellFillWhite);
            mk.Quad(new Vector3(i, 0, -h), new Vector3(i, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h), MeshKit.CellFillWhite);
            mk.Quad(new Vector3(-i, 0, -h), new Vector3(-i, 0, -i), new Vector3(i, 0, -i), new Vector3(i, 0, -h), MeshKit.CellFillWhite);
            mk.Quad(new Vector3(-i, 0, i), new Vector3(-i, 0, h), new Vector3(i, 0, h), new Vector3(i, 0, i), MeshKit.CellFillWhite);
            return _squares[Dims.Square] = mk.ToMesh("ChessMarkSquare");
        }

        private Mesh Disc()
        {
            Mesh m;
            if (_discs.TryGetValue(Dims.Square, out m) && m != null) return m;
            MeshKit mk = new MeshKit { Pal = Palette.Green };
            float r = Dims.Square * 0.16f;
            mk.Lathe(Vector3.zero, new List<Vector2> { new Vector2(r, 0f), new Vector2(r, 0.003f), new Vector2(0f, 0.003f) }, 16, MeshKit.CellFillWhite, false, false);
            return _discs[Dims.Square] = mk.ToMesh("ChessMarkDisc");
        }
    }

    public class ChessInteraction : ModPartInteraction
    {
        private static List<PartInteractionConfig> _cfg;
        public override IReadOnlyCollection<PartInteractionConfig> Interactions { get { return _cfg ?? (_cfg = new List<PartInteractionConfig> { PartKit.Interact("Play chess"), PartKit.PickUp() }); } }
        public override float DetectionDistance { get { return 2.6f; } }

        public override bool Interact(InteractionType type)
        {
            if (type == InteractionType.PickUp) return PickUpThis();
            if (type != InteractionType.Interact) return false;
            ChessView.Enter(Context);
            return true;
        }
    }
}
