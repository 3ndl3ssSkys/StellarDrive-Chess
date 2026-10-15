using System;
using System.Collections.Generic;
using Chess.Common;
using UnityEngine;

namespace Chess
{
    /// <summary>
    /// The table and the pieces, made the way the game's own parts look: flat
    /// palette colours, ink hatching, bevelled edges. Metres; a piece stands
    /// with its base at y = 0 and faces +z (towards black) - black's knights
    /// are turned round.
    /// </summary>
    /// <summary>
    /// The sizes of one kind of chess set: the table (2 x 2), the board that lies on
    /// the floor (2 x 2) and the small board (one tile). Everything that draws, points
    /// at or seats players around a set reads its sizes from here.
    /// </summary>
    public class ChessDims
    {
        public int Kind;             // 0 table, 1 floor board, 2 small board
        public float Square, Frame;  // square size; the frame round the board
        public float TopY, BoardY;   // the surface it all stands on; the playing surface
        public float Extent;         // half the width of that surface: nothing may go past it
        public float PieceScale { get { return Square / 0.07f; } }   // the piece meshes are made for 7 cm squares
        public float Half { get { return Square * 4f; } }
        /// <summary>How big this set is next to the table's board (the camera keeps the same view of it).</summary>
        public float View { get { return Half / 0.64f; } }
        public float TrayX { get { return Half + Frame + (Extent - Half - Frame) * 0.5f; } }
        public float TrayZ0 { get { return Half * 1.03f; } }
        public float TrayStep { get { return Square * 0.5875f; } }

        public Vector3 SquareCenter(int sq) { return new Vector3((Position.File(sq) - 3.5f) * Square, BoardY, (Position.Rank(sq) - 3.5f) * Square); }

        public int SquareAt(Vector3 local)
        {
            int f = Mathf.FloorToInt(local.x / Square + 4f), r = Mathf.FloorToInt(local.z / Square + 4f);
            return f >= 0 && f < 8 && r >= 0 && r < 8 ? f + 8 * r : -1;
        }

        public float Height(int code) { return ChessModels.MeshHeight(code) * PieceScale; }

        /// <summary>White's losses sit on black's right, black's on white's right, between the board and the edge.</summary>
        public Vector3 TraySpot(int code, int index)
        {
            float x = (code > 0 ? -1f : 1f) * TrayX;
            float z = (code > 0 ? 1f : -1f) * (TrayZ0 - index * TrayStep);
            return new Vector3(x, TopY, z);
        }

        public static readonly ChessDims Table = new ChessDims { Kind = 0, Square = 0.16f, Frame = 0.06f, TopY = 0.80f, BoardY = 0.826f, Extent = 0.93f };
        public static readonly ChessDims Floor = new ChessDims { Kind = 1, Square = 0.16f, Frame = 0.06f, TopY = 0.03f, BoardY = 0.052f, Extent = 0.93f };
        public static readonly ChessDims Small = new ChessDims { Kind = 2, Square = 0.08f, Frame = 0.03f, TopY = 0.022f, BoardY = 0.034f, Extent = 0.46f };

        public static ChessDims OfKind(int kind) { return kind == 1 ? Floor : kind == 2 ? Small : Table; }
    }

    public static class ChessModels
    {
        // A 2 x 2 table: a 1.86 m top, the board (1.28 m + frame) in the middle and the
        // trays of taken pieces beside it, all well inside the edge.
        public const float TableHalf = 0.93f;
        public const float Square = 0.16f;          // 8 squares = 1.28 m
        public const float PieceScale = Square / 0.07f;   // the piece meshes are made for 7 cm squares
        public const float TopY = 0.80f;            // table top surface
        public const float BoardY = 0.826f;         // playing surface
        public const float Frame = 0.06f;
        public const float Half = Square * 4f;      // board half width (0.64)

        public static Vector3 SquareCenter(int sq) { return new Vector3((Position.File(sq) - 3.5f) * Square, BoardY, (Position.Rank(sq) - 3.5f) * Square); }

        public static int SquareAt(Vector3 local)
        {
            int f = Mathf.FloorToInt(local.x / Square + 4f), r = Mathf.FloorToInt(local.z / Square + 4f);
            return f >= 0 && f < 8 && r >= 0 && r < 8 ? f + 8 * r : -1;
        }

        public static Material Mat() { return Looks.Get("chess", Color.white, 0.55f, 0.05f); }

        // ---- the table ---------------------------------------------------------------------

        public static void BuildTable(Transform visual)
        {
            Material mat = Mat();
            MeshKit wood = new MeshKit { Pal = Palette.Brown };
            // four turned legs, an apron between them, and the top
            foreach (float x in new[] { -0.8f, 0.8f })
                foreach (float z in new[] { -0.8f, 0.8f })
                    wood.Lathe(new Vector3(x, 0f, z), new List<Vector2>
                    {
                        new Vector2(0.055f, 0f), new Vector2(0.06f, 0.03f), new Vector2(0.045f, 0.07f), new Vector2(0.04f, 0.3f),
                        new Vector2(0.052f, 0.34f), new Vector2(0.04f, 0.38f), new Vector2(0.042f, 0.62f), new Vector2(0.06f, 0.66f), new Vector2(0.06f, 0.72f),
                    }, 12, MeshKit.CellSoft, true, false);
            wood.BevelBox(new Vector3(0f, 0.7f, -0.8f), new Vector3(1.6f, 0.1f, 0.05f), 0.01f, MeshKit.CellPlain, MeshKit.CellFillMid, MeshKit.CellHatch);
            wood.BevelBox(new Vector3(0f, 0.7f, 0.8f), new Vector3(1.6f, 0.1f, 0.05f), 0.01f, MeshKit.CellPlain, MeshKit.CellFillMid, MeshKit.CellHatch);
            wood.BevelBox(new Vector3(-0.8f, 0.7f, 0f), new Vector3(0.05f, 0.1f, 1.6f), 0.01f, MeshKit.CellPlain, MeshKit.CellFillMid, MeshKit.CellHatch);
            wood.BevelBox(new Vector3(0.8f, 0.7f, 0f), new Vector3(0.05f, 0.1f, 1.6f), 0.01f, MeshKit.CellPlain, MeshKit.CellFillMid, MeshKit.CellHatch);
            wood.BevelBox(new Vector3(0f, TopY - 0.025f, 0f), new Vector3(TableHalf * 2f, 0.05f, TableHalf * 2f), 0.015f, MeshKit.CellPlain, MeshKit.CellFillMid, MeshKit.CellHatch);
            wood.ToObject("Table", visual, mat);
            BoardOn(visual, ChessDims.Table, mat);
            Kind(visual, 0);
        }

        /// <summary>A board with no table: a wooden base on the floor (2 x 2), or a small one (one tile).</summary>
        public static void BuildFloorBoard(Transform visual) { BuildLowBoard(visual, ChessDims.Floor); }
        public static void BuildSmallBoard(Transform visual) { BuildLowBoard(visual, ChessDims.Small); }

        private static void BuildLowBoard(Transform visual, ChessDims d)
        {
            Material mat = Mat();
            MeshKit wood = new MeshKit { Pal = Palette.Brown };
            wood.BevelBox(new Vector3(0f, d.TopY * 0.5f, 0f), new Vector3(d.Extent * 2f, d.TopY, d.Extent * 2f), Mathf.Min(0.012f, d.TopY * 0.4f), MeshKit.CellPlain, MeshKit.CellFillMid, MeshKit.CellHatch);
            wood.ToObject("Base", visual, mat);
            BoardOn(visual, d, mat);
            Kind(visual, d.Kind);
        }

        /// <summary>Which kind of set a visual is (a marker child: mod components' fields are not copied with the prefab).</summary>
        private static void Kind(Transform visual, int kind)
        {
            GameObject k = new GameObject("ChessKind" + kind);
            k.transform.SetParent(visual, false);
        }

        public static int KindOf(Transform visual)
        {
            for (int k = 2; k >= 1; k--) if (visual.Find("ChessKind" + k) != null) return k;
            return 0;
        }

        /// <summary>The board: a dark frame, 64 squares and a1..h8 round the edge (readable from white's side).</summary>
        private static void BoardOn(Transform visual, ChessDims d, Material mat)
        {
            MeshKit frame = new MeshKit { Pal = Palette.Dark };
            float w = d.Half + d.Frame;
            frame.BevelBox(new Vector3(0f, d.TopY + (d.BoardY - d.TopY) * 0.5f, 0f), new Vector3(w * 2f, d.BoardY - d.TopY, w * 2f), Mathf.Min(0.008f, (d.BoardY - d.TopY) * 0.3f), MeshKit.CellPlain, MeshKit.CellFillMid, MeshKit.CellHatch);
            frame.ToObject("BoardFrame", visual, mat);
            MeshKit sq = new MeshKit();
            Vector3 lift = Vector3.up * 0.0005f;
            for (int s = 0; s < 64; s++)
            {
                bool dark = ((Position.File(s) + Position.Rank(s)) & 1) == 0;   // a1 is dark
                sq.Pal = dark ? Palette.Brown : Palette.Beige;
                Vector3 c = d.SquareCenter(s);
                float h = d.Square * 0.5f;
                sq.Quad(new Vector3(c.x - h, d.BoardY, c.z - h) + lift, new Vector3(c.x - h, d.BoardY, c.z + h) + lift, new Vector3(c.x + h, d.BoardY, c.z + h) + lift, new Vector3(c.x + h, d.BoardY, c.z - h) + lift, dark ? MeshKit.CellHatch : MeshKit.CellPlain);
            }
            sq.ToObject("Squares", visual, mat);
            Color ink = new Color(0.86f, 0.8f, 0.66f);
            float size = d.View;
            for (int i = 0; i < 8; i++)
            {
                float t = (i - 3.5f) * d.Square;
                Letter(visual, ((char)('a' + i)).ToString(), new Vector3(t, d.BoardY + 0.001f, -d.Half - d.Frame * 0.5f), ink, size);
                Letter(visual, ((char)('1' + i)).ToString(), new Vector3(-d.Half - d.Frame * 0.5f, d.BoardY + 0.001f, t), ink, size);
            }
        }

        private static void Letter(Transform parent, string s, Vector3 at, Color c, float scale)
        {
            var t = PartKit.Text(parent, "Label" + s, at, Quaternion.Euler(90f, 0f, 0f), 0.36f * scale, c);
            t.rectTransform.sizeDelta = new Vector2(0.06f, 0.06f) * scale;
            t.alignment = TMPro.TextAlignmentOptions.Center;
            t.text = s;
        }

        // ---- the pieces --------------------------------------------------------------------

        private static readonly Dictionary<int, Mesh> _meshes = new Dictionary<int, Mesh>();

        /// <summary>Mesh for a piece code (+-1..6).</summary>
        public static Mesh PieceMesh(int code)
        {
            Mesh m;
            if (_meshes.TryGetValue(code, out m) && m != null) return m;
            MeshKit mk = new MeshKit { Pal = code > 0 ? Palette.Light : Palette.Dark };
            int t = Math.Abs(code);
            List<Vector2> baseRing = new List<Vector2> { new Vector2(0.021f, 0f), new Vector2(0.021f, 0.0045f), new Vector2(0.018f, 0.0075f), new Vector2(0.014f, 0.0095f) };
            List<Vector2> p = new List<Vector2>(baseRing);
            switch (t)
            {
                case Position.P:
                    p.AddRange(new[] { new Vector2(0.0095f, 0.017f), new Vector2(0.0078f, 0.025f), new Vector2(0.012f, 0.027f), new Vector2(0.012f, 0.0288f), new Vector2(0.0072f, 0.0305f) });
                    Ball(p, 0.0375f, 0.0088f);
                    mk.Lathe(Vector3.zero, p, 14, MeshKit.CellSoftLight);
                    break;
                case Position.R:
                    p.AddRange(new[] { new Vector2(0.0135f, 0.012f), new Vector2(0.0125f, 0.036f), new Vector2(0.0165f, 0.038f), new Vector2(0.0165f, 0.046f), new Vector2(0.0115f, 0.046f), new Vector2(0.0115f, 0.043f), new Vector2(0f, 0.043f) });
                    mk.Lathe(Vector3.zero, p, 14, MeshKit.CellSoftLight, true, false);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                        mk.BevelBox(new Vector3(Mathf.Cos(a) * 0.0138f, 0.0485f, Mathf.Sin(a) * 0.0138f), new Vector3(0.0075f, 0.006f, 0.0075f), 0.0012f, MeshKit.CellSoftLight, MeshKit.CellFillMid, MeshKit.CellFillMid);
                    }
                    break;
                case Position.B:
                    p.AddRange(new[] { new Vector2(0.0092f, 0.022f), new Vector2(0.0078f, 0.03f), new Vector2(0.0118f, 0.032f), new Vector2(0.0118f, 0.0338f), new Vector2(0.007f, 0.0355f),
                        new Vector2(0.0098f, 0.04f), new Vector2(0.0108f, 0.045f), new Vector2(0.0092f, 0.05f), new Vector2(0.0052f, 0.0545f), new Vector2(0.0022f, 0.0558f) });
                    Ball(p, 0.0585f, 0.0032f);
                    mk.Lathe(Vector3.zero, p, 14, MeshKit.CellSoftLight);
                    // the mitre's slit
                    mk.M = Matrix4x4.TRS(new Vector3(0f, 0.0475f, 0f), Quaternion.Euler(0f, 0f, 30f), Vector3.one);
                    mk.Box(Vector3.zero, new Vector3(0.0215f, 0.0016f, 0.004f), MeshKit.CellFillDark, MeshKit.CellFillDark, MeshKit.CellFillDark, MeshKit.CellFillDark, MeshKit.CellFillDark, MeshKit.CellFillDark);
                    mk.M = Matrix4x4.identity;
                    break;
                case Position.Q:
                    p.Clear();
                    p.AddRange(new[] { new Vector2(0.023f, 0f), new Vector2(0.023f, 0.0045f), new Vector2(0.019f, 0.008f), new Vector2(0.014f, 0.0105f), new Vector2(0.0102f, 0.024f), new Vector2(0.0082f, 0.036f),
                        new Vector2(0.0128f, 0.038f), new Vector2(0.0128f, 0.0398f), new Vector2(0.0082f, 0.0418f), new Vector2(0.0102f, 0.05f), new Vector2(0.0148f, 0.0598f), new Vector2(0.0118f, 0.0608f), new Vector2(0.0078f, 0.0585f), new Vector2(0.0042f, 0.0615f) });
                    Ball(p, 0.065f, 0.0042f);
                    mk.Lathe(Vector3.zero, p, 16, MeshKit.CellSoftLight);
                    break;
                case Position.K:
                    p.Clear();
                    p.AddRange(new[] { new Vector2(0.024f, 0f), new Vector2(0.024f, 0.0045f), new Vector2(0.02f, 0.008f), new Vector2(0.015f, 0.0105f), new Vector2(0.011f, 0.026f), new Vector2(0.0092f, 0.04f),
                        new Vector2(0.0138f, 0.042f), new Vector2(0.0138f, 0.044f), new Vector2(0.009f, 0.046f), new Vector2(0.011f, 0.056f), new Vector2(0.0132f, 0.0618f), new Vector2(0.006f, 0.0628f), new Vector2(0f, 0.0628f) });
                    mk.Lathe(Vector3.zero, p, 16, MeshKit.CellSoftLight);
                    mk.BevelBox(new Vector3(0f, 0.0705f, 0f), new Vector3(0.0045f, 0.016f, 0.0045f), 0.001f, MeshKit.CellSoftLight, MeshKit.CellFillMid, MeshKit.CellFillMid);
                    mk.BevelBox(new Vector3(0f, 0.0725f, 0f), new Vector3(0.013f, 0.0045f, 0.0045f), 0.001f, MeshKit.CellSoftLight, MeshKit.CellFillMid, MeshKit.CellFillMid);
                    break;
                case Position.N:
                    p.AddRange(new[] { new Vector2(0.0142f, 0.0125f), new Vector2(0f, 0.0125f) });
                    mk.Lathe(Vector3.zero, p, 14, MeshKit.CellSoftLight, true, true);
                    // the head: a side profile (x = forward, y = up), cut out and given thickness
                    List<Vector2> head = new List<Vector2>
                    {
                        new Vector2(-0.0125f, 0.012f), new Vector2(0.011f, 0.012f), new Vector2(0.0095f, 0.021f), new Vector2(0.0155f, 0.03f),
                        new Vector2(0.0215f, 0.0355f), new Vector2(0.0235f, 0.0415f), new Vector2(0.018f, 0.0465f), new Vector2(0.0095f, 0.0515f),
                        new Vector2(0.0062f, 0.0585f), new Vector2(0.0022f, 0.0538f), new Vector2(-0.0035f, 0.0575f), new Vector2(-0.0098f, 0.0495f),
                        new Vector2(-0.0138f, 0.036f), new Vector2(-0.0142f, 0.021f),
                    };
                    mk.M = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, -90f, 0f), Vector3.one);
                    mk.Extrude(head, -0.0088f, 0.0088f, MeshKit.CellSoftLight, MeshKit.CellFillMid, MeshKit.CellSoftLight);
                    // an eye on each side
                    mk.M = Matrix4x4.identity;
                    mk.Box(new Vector3(0f, 0.044f, 0.0135f), new Vector3(0.0185f, 0.0028f, 0.0028f), MeshKit.CellFillDark, MeshKit.CellFillDark, MeshKit.CellFillDark, MeshKit.CellFillDark, MeshKit.CellFillDark, MeshKit.CellFillDark);
                    break;
            }
            m = mk.ToMesh("Chess" + (code > 0 ? "W" : "B") + "PNBRQK"[t - 1]);
            _meshes[code] = m;
            return m;
        }

        /// <summary>Height of each piece as it stands on this board (for falling over, and how easily it does).</summary>
        public static float Height(int code) { return MeshHeight(code) * PieceScale; }

        public static float MeshHeight(int code)
        {
            switch (Math.Abs(code)) { case 1: return 0.047f; case 2: return 0.059f; case 3: return 0.062f; case 4: return 0.051f; case 5: return 0.069f; default: return 0.078f; }
        }

        private static void Ball(List<Vector2> p, float cy, float r)
        {
            for (int i = -3; i <= 6; i++)
            {
                float a = i * Mathf.PI / 12f;   // -45deg .. 90deg
                p.Add(new Vector2(Mathf.Max(0f, Mathf.Cos(a) * r), cy + Mathf.Sin(a) * r));
            }
        }
    }
}
