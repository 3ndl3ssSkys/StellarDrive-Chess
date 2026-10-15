using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Chess
{
    /// <summary>
    /// One solid part of a piece's shape: a capsule (a segment A-B, thickened by R) or a
    /// disc (centre A, facing B, radius R, half as thick as T) - a base, a collar, a crown.
    /// </summary>
    public struct Prim
    {
        public bool Disc;
        public Vector3 A, B;
        public float R, T;
    }

    /// <summary>A piece's shape where it is now (board frame), with a bounding ball to skip far pairs quickly.</summary>
    public class Body
    {
        public readonly List<Prim> Prims = new List<Prim>();
        public Vector3 Center;
        public float Radius;
        public int Code;
        public Vector3 Pos;
        public Quaternion Rot = Quaternion.identity;
        public float Scale = 1f;
    }

    /// <summary>
    /// How the pieces touch: each piece is a few capsules and discs that follow its drawn
    /// shape (the base, the body, the collar, the head or crown). Pieces that meet are held
    /// apart where their shapes touch - toppling, lying or standing. Also the host's domino
    /// rule: which standing piece a falling one hits first.
    /// </summary>
    public static class ChessPhysics
    {
        // ---- the shapes (mesh units: 7 cm squares, base at y = 0, facing +z) -------------------

        private static Prim Cap(float y0, float y1, float r) { return new Prim { A = new Vector3(0f, y0, 0f), B = new Vector3(0f, y1, 0f), R = r }; }
        private static Prim Cap(Vector3 a, Vector3 b, float r) { return new Prim { A = a, B = b, R = r }; }
        private static Prim Disc(float y, float r, float halfThick) { return new Prim { Disc = true, A = new Vector3(0f, y, 0f), B = Vector3.up, R = r, T = halfThick }; }

        private static readonly Prim[][] _shapes = new Prim[7][];

        /// <summary>The shape of a piece type; the first part is always its base (a disc on the board).</summary>
        public static Prim[] Shape(int code)
        {
            int t = Math.Abs(code);
            if (_shapes[t] != null) return _shapes[t];
            Prim[] s;
            switch (t)
            {
                case Position.P: s = new[] { Disc(0.0048f, 0.021f, 0.0048f), Disc(0.0105f, 0.0152f, 0.002f), Cap(0.012f, 0.027f, 0.0105f), Disc(0.028f, 0.012f, 0.001f), Cap(0.0375f, 0.0375f, 0.0088f) }; break;
                case Position.N:
                    // the head is a flat cut-out with square edges: two capsules side by side for the neck and ears, two for the snout (eyes too)
                    s = new[] { Disc(0.0048f, 0.021f, 0.0048f), Disc(0.0105f, 0.0152f, 0.002f), Disc(0.011f, 0.0142f, 0.0015f),
                        Cap(new Vector3(-0.0045f, 0.018f, -0.0036f), new Vector3(-0.0045f, 0.052f, 0f), 0.0125f), Cap(new Vector3(0.0045f, 0.018f, -0.0036f), new Vector3(0.0045f, 0.052f, 0f), 0.0125f),
                        Cap(new Vector3(-0.0045f, 0.026f, 0.006f), new Vector3(-0.0045f, 0.040f, 0.0165f), 0.0085f), Cap(new Vector3(0.0045f, 0.026f, 0.006f), new Vector3(0.0045f, 0.040f, 0.0165f), 0.0085f),
                        Cap(new Vector3(-0.0045f, 0.0135f, 0.0035f), new Vector3(-0.0045f, 0.022f, 0.0045f), 0.009f), Cap(new Vector3(0.0045f, 0.0135f, 0.0035f), new Vector3(0.0045f, 0.022f, 0.0045f), 0.009f) };
                    break;
                case Position.B: s = new[] { Disc(0.0048f, 0.021f, 0.0048f), Disc(0.0105f, 0.0152f, 0.002f), Cap(0.012f, 0.05f, 0.0118f), Cap(0.0585f, 0.0585f, 0.0035f) }; break;
                case Position.R: s = new[] { Disc(0.0048f, 0.021f, 0.0048f), Disc(0.0105f, 0.0152f, 0.002f), Cap(0.012f, 0.038f, 0.0135f), Disc(0.0438f, 0.0191f, 0.0077f) }; break;
                case Position.Q: s = new[] { Disc(0.0048f, 0.023f, 0.0048f), Disc(0.0105f, 0.0152f, 0.002f), Cap(0.012f, 0.055f, 0.0115f), Disc(0.039f, 0.0128f, 0.001f), Disc(0.0578f, 0.0148f, 0.0035f), Cap(0.065f, 0.065f, 0.0042f) }; break;
                default:
                    s = new[] { Disc(0.0048f, 0.024f, 0.0048f), Disc(0.0105f, 0.0152f, 0.002f), Cap(0.012f, 0.058f, 0.0125f), Disc(0.043f, 0.0138f, 0.001f), Disc(0.0615f, 0.0132f, 0.0012f),
                        Cap(0.0628f, 0.078f, 0.0023f), Cap(new Vector3(-0.0065f, 0.0725f, 0f), new Vector3(0.0065f, 0.0725f, 0f), 0.0023f) };
                    break;
            }
            return _shapes[t] = s;
        }

        /// <summary>The base's radius (mesh units): a piece tips over its edge.</summary>
        public static float BaseRadius(int code) { return Shape(code)[0].R; }

        /// <summary>How heavy a piece is next to a pawn.</summary>
        public static float Mass(int code)
        {
            switch (Math.Abs(code)) { case 1: return 1f; case 2: return 1.4f; case 3: return 1.4f; case 4: return 1.7f; case 5: return 2.1f; default: return 2.4f; }
        }

        /// <summary>Pieces face the other side; knights face their own player's left (see the table visual).</summary>
        public static Quaternion Facing(int code)
        {
            if (Math.Abs(code) == Position.N) return Quaternion.Euler(0f, code > 0 ? -90f : 90f, 0f);
            return code > 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
        }

        /// <summary>The way a knocked piece falls: the knock direction, a few degrees apart per square.</summary>
        public static Vector3 FallDir(int knockDir, int sq)
        {
            float jitter = ((sq * 37) % 41) - 20;
            float dir = (knockDir + jitter) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(dir), 0f, Mathf.Cos(dir));
        }

        // ---- poses ------------------------------------------------------------------------------

        /// <summary>
        /// A piece tipped <paramref name="angle"/> degrees over the front edge of its base, towards
        /// <paramref name="fall"/>: where its origin (the middle of its underside) is, and how it is turned.
        /// </summary>
        public static void TipPose(Vector3 home, Vector3 fall, float baseR, float angle, Quaternion facing, out Vector3 pos, out Quaternion rot)
        {
            Quaternion q = Quaternion.AngleAxis(angle, Vector3.Cross(Vector3.up, fall));
            pos = home + fall * baseR + q * (-fall * baseR);
            rot = q * facing;
        }

        /// <summary>
        /// How far over a piece ends up lying: past 90 degrees, until its head (thinner than its
        /// base) is on the board too. Found by turning it further over the base's rim.
        /// </summary>
        public static float RestAngle(int code, Vector3 fall, Quaternion facing)
        {
            Prim[] s = Shape(code);
            float lo = 90f, hi = 125f;
            if (Lowest(s, fall, facing, lo) <= 0f) return lo;
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Lowest(s, fall, facing, mid) > 0f) lo = mid; else hi = mid;
            }
            return lo;
        }

        /// <summary>The lowest point of everything but the base (mesh units) with the piece tipped this far.</summary>
        private static float Lowest(Prim[] s, Vector3 fall, Quaternion facing, float angle)
        {
            Vector3 pos; Quaternion rot;
            TipPose(Vector3.zero, fall, s[0].R, angle, facing, out pos, out rot);
            float low = float.MaxValue;
            for (int i = 1; i < s.Length; i++)
            {
                Prim p = World(s[i], pos, rot, 1f);
                low = Mathf.Min(low, LowestOf(p));
            }
            return low;
        }

        public static float LowestOf(Prim p)
        {
            if (!p.Disc) return Mathf.Min(p.A.y, p.B.y) - p.R;
            float ny = Mathf.Abs(p.B.y);
            return p.A.y - p.R * Mathf.Sqrt(Mathf.Max(0f, 1f - ny * ny)) - p.T * ny;
        }

        public static Prim World(Prim p, Vector3 pos, Quaternion rot, float scale)
        {
            Prim w = p;
            w.A = pos + rot * (p.A * scale);
            w.B = p.Disc ? (rot * p.B).normalized : pos + rot * (p.B * scale);
            w.R = p.R * scale;
            w.T = p.T * scale;
            return w;
        }

        public static void SetPose(Body b, int code, Vector3 pos, Quaternion rot, float scale)
        {
            b.Code = code; b.Pos = pos; b.Rot = rot; b.Scale = scale;
            b.Prims.Clear();
            Vector3 sum = Vector3.zero;
            foreach (Prim p in Shape(code)) { Prim w = World(p, pos, rot, scale); b.Prims.Add(w); sum += w.A + (w.Disc ? w.A : w.B); }
            b.Center = sum / (2f * b.Prims.Count);
            float r = 0f;
            foreach (Prim w in b.Prims)
            {
                float reach = w.Disc ? w.R : w.R;
                r = Mathf.Max(r, (w.A - b.Center).magnitude + reach);
                if (!w.Disc) r = Mathf.Max(r, (w.B - b.Center).magnitude + reach);
            }
            b.Radius = r;
        }

        // ---- contacts ---------------------------------------------------------------------------

        /// <summary>Closest points of segments p1-q1 and p2-q2.</summary>
        public static void SegSeg(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (a < 1e-12f && e < 1e-12f) { c1 = p1; c2 = p2; return; }
            if (a < 1e-12f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e < 1e-12f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), den = a * e - b * b;
                    s = den > 1e-12f ? Mathf.Clamp01((b * f - c * e) / den) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            c1 = p1 + d1 * s; c2 = p2 + d2 * t;
        }

        private static Vector3 SegPoint(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 d = b - a;
            float l = d.sqrMagnitude;
            return l < 1e-12f ? a : a + d * Mathf.Clamp01(Vector3.Dot(p - a, d) / l);
        }

        /// <summary>A disc, as seen from a point: the diameter pointing that way, thickened by its half thickness.</summary>
        private static void DiscEdge(Prim d, Vector3 toward, out Vector3 a, out Vector3 b)
        {
            Vector3 v = Vector3.ProjectOnPlane(toward - d.A, d.B);
            if (v.sqrMagnitude < 1e-12f) { v = Vector3.ProjectOnPlane(Vector3.right, d.B); if (v.sqrMagnitude < 1e-6f) v = Vector3.ProjectOnPlane(Vector3.forward, d.B); }
            v = v.normalized * Mathf.Max(0f, d.R - d.T);
            a = d.A - v; b = d.A + v;
        }

        /// <summary>
        /// The gap between two parts (negative: how deep they overlap); <paramref name="n"/> points
        /// from <paramref name="y"/> to <paramref name="x"/>, <paramref name="at"/> is where they meet.
        /// </summary>
        public static float Gap(Prim x, Prim y, out Vector3 n, out Vector3 at)
        {
            Vector3 xa = x.A, xb = x.B, ya = y.A, yb = y.B;
            float xr = x.Disc ? x.T : x.R, yr = y.Disc ? y.T : y.R;
            Vector3 c1, c2;
            // a disc turns its edge towards what it meets (twice: the second time towards the closest point found)
            Vector3 towardX = x.Disc ? x.A : SegPoint(x.A, x.B, y.A), towardY = y.Disc ? y.A : SegPoint(y.A, y.B, x.A);
            for (int pass = 0; pass < 2; pass++)
            {
                if (x.Disc) DiscEdge(x, towardY, out xa, out xb);
                if (y.Disc) DiscEdge(y, towardX, out ya, out yb);
                SegSeg(xa, xb, ya, yb, out c1, out c2);
                towardX = c1; towardY = c2;
                if (!x.Disc && !y.Disc) break;
            }
            SegSeg(xa, xb, ya, yb, out c1, out c2);
            Vector3 d = c1 - c2;
            float dist = d.magnitude;
            n = dist > 1e-7f ? d / dist : Vector3.up;
            at = (c1 - n * xr + c2 + n * yr) * 0.5f;
            return dist - xr - yr;
        }

        /// <summary>The deepest overlap (or smallest gap) between two bodies; float.MaxValue when far apart.</summary>
        public static float Gap(Body x, Body y, out Vector3 n, out Vector3 at)
        {
            n = Vector3.up; at = x.Center;
            if ((x.Center - y.Center).sqrMagnitude > (x.Radius + y.Radius) * (x.Radius + y.Radius)) return float.MaxValue;
            float best = float.MaxValue;
            for (int i = 0; i < x.Prims.Count; i++)
                for (int j = 0; j < y.Prims.Count; j++)
                {
                    Vector3 nn, aa;
                    float g = Gap(x.Prims[i], y.Prims[j], out nn, out aa);
                    if (g < best) { best = g; n = nn; at = aa; }
                }
            return best;
        }

        // ---- the host's dominoes ------------------------------------------------------------------

        /// <summary>
        /// The first standing piece that the piece on <paramref name="sq"/>, toppling towards
        /// <paramref name="fall"/>, would hit - or -1. Pieces in <paramref name="skip"/> are not in
        /// the way (they are falling too). <paramref name="high"/>: how far up the one hit
        /// it lands (0 its foot .. 1 its top), <paramref name="angle"/>: how far over the faller is then.
        /// </summary>
        public static int FirstHit(ChessDims d, sbyte[] board, ulong skip, int sq, Vector3 fall, out float high, out float angle)
        {
            high = 0f; angle = 0f;
            int code = board[sq];
            if (code == 0) return -1;
            float s = d.PieceScale;
            Vector3 home = d.SquareCenter(sq);
            float reach = d.Height(code) + 0.03f * s;
            List<int> near = new List<int>();
            List<Body> bodies = new List<Body>();
            for (int o = 0; o < 64; o++)
            {
                if (o == sq || board[o] == 0 || (skip & (1UL << o)) != 0) continue;
                if ((d.SquareCenter(o) - home).magnitude > reach) continue;
                Body b = new Body();
                SetPose(b, board[o], d.SquareCenter(o), Facing(board[o]), s);
                near.Add(o); bodies.Add(b);
            }
            if (near.Count == 0) return -1;
            Quaternion facing = Facing(code);
            float rest = RestAngle(code, fall, facing), baseR = BaseRadius(code) * s;
            Body me = new Body();
            for (float a = 2f; a <= rest + 0.01f; a += 2f)
            {
                Vector3 pos; Quaternion rot;
                TipPose(home, fall, baseR, a, facing, out pos, out rot);
                SetPose(me, code, pos, rot, s);
                for (int i = 0; i < bodies.Count; i++)
                {
                    Vector3 n, at;
                    if (Gap(me, bodies[i], out n, out at) < 0f)
                    {
                        angle = a;
                        high = Mathf.Clamp01((at.y - d.BoardY) / Mathf.Max(0.001f, d.Height(board[near[i]])));
                        return near[i];
                    }
                }
            }
            return -1;
        }
    }

    /// <summary>
    /// When the Momentum mod is loaded: the push the ship's motion gives anything aboard, at a
    /// point, just as Momentum works it out for the people (the ship's lurches and the fling of
    /// its turning), scaled by Momentum's PushStrength. Found by name, so Chess needs no Momentum.
    /// </summary>
    public static class MomentumLink
    {
        private static bool _looked;
        private static MethodInfo _get, _felt;
        private static FieldInfo _strength;
        private static PropertyInfo _value;

        public static bool Present { get { Look(); return _get != null && _felt != null; } }

        private static void Look()
        {
            if (_looked) return;
            _looked = true;
            try
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = a.GetType("Momentum.ShipMotion");
                    if (t == null) continue;
                    _get = t.GetMethod("Get", new[] { typeof(bool), typeof(uint) });
                    _felt = t.GetMethods().FirstOrDefaultNamed("Felt");
                    Type mod = a.GetType("Momentum.MomentumMod");
                    _strength = mod != null ? mod.GetField("Strength", BindingFlags.Public | BindingFlags.Static) : null;
                    break;
                }
            }
            catch { _get = null; _felt = null; }
        }

        /// <summary>The push felt at <paramref name="local"/> (the root ship's frame), in that frame.</summary>
        public static bool TryFelt(bool server, uint rootShip, Vector3 local, out Vector3 felt)
        {
            felt = Vector3.zero;
            if (!Present) return false;
            try
            {
                object m = _get.Invoke(null, new object[] { server, rootShip });
                if (m == null) return false;
                felt = (Vector3)_felt.Invoke(null, new object[] { m, local });
                object e = _strength != null ? _strength.GetValue(null) : null;
                if (e != null)
                {
                    if (_value == null) _value = e.GetType().GetProperty("Value");
                    object v = _value != null ? _value.GetValue(e, null) : null;
                    if (v is float) felt *= Mathf.Max(0f, (float)v);
                }
                return true;
            }
            catch { _get = null; return false; }
        }

        private static MethodInfo FirstOrDefaultNamed(this MethodInfo[] ms, string name)
        {
            foreach (MethodInfo m in ms) if (m.Name == name && m.GetParameters().Length == 2) return m;
            return null;
        }
    }
}
