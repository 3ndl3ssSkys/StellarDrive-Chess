using System;
using System.Collections.Generic;
using System.Text;

namespace Chess
{
    /// <summary>A move on the board: squares 0..63 are a1..h8 (file + 8 * rank).</summary>
    public struct Move : IEquatable<Move>
    {
        public byte From, To;
        public sbyte Promo;     // 0, or the piece a pawn becomes (+-2..5)
        public byte Flags;      // Capture | EnPassant | Castle | DoublePush

        public const byte Capture = 1, EnPassant = 2, Castle = 4, DoublePush = 8;

        public bool Equals(Move o) { return From == o.From && To == o.To && Promo == o.Promo; }
        public override bool Equals(object o) { return o is Move && Equals((Move)o); }
        public override int GetHashCode() { return From | (To << 8) | (Promo << 16); }

        /// <summary>Coordinate form, e.g. "e2e4", "e7e8q".</summary>
        public string Uci()
        {
            string s = Position.SquareName(From) + Position.SquareName(To);
            if (Promo != 0) s += "nbrq"[Math.Abs(Promo) - 2];
            return s;
        }
    }

    /// <summary>
    /// A chess position with the complete rules: castling, en passant,
    /// promotion, check, checkmate, stalemate, the fifty-move rule and dead
    /// positions (insufficient material). Pieces: +1..+6 white pawn, knight,
    /// bishop, rook, queen, king; negative for black; 0 empty.
    /// </summary>
    public class Position
    {
        public const sbyte P = 1, N = 2, B = 3, R = 4, Q = 5, K = 6;
        public sbyte[] Sq = new sbyte[64];
        public bool WhiteToMove = true;
        public int Castling = 15;    // 1 white O-O, 2 white O-O-O, 4 black O-O, 8 black O-O-O
        public int EnPassant = -1;   // the square a pawn may capture onto
        public int Halfmove, Fullmove = 1;

        public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        public static int File(int s) { return s & 7; }
        public static int Rank(int s) { return s >> 3; }
        public static string SquareName(int s) { return "" + (char)('a' + File(s)) + (char)('1' + Rank(s)); }
        public static int SquareOf(string n) { return (n[0] - 'a') + 8 * (n[1] - '1'); }

        public Position Clone()
        {
            Position p = (Position)MemberwiseClone();
            p.Sq = (sbyte[])Sq.Clone();
            return p;
        }

        // ---- FEN ------------------------------------------------------------------------

        public static Position FromFen(string fen)
        {
            Position p = new Position();
            string[] f = fen.Trim().Split(' ');
            int rank = 7, file = 0;
            foreach (char c in f[0])
            {
                if (c == '/') { rank--; file = 0; continue; }
                if (char.IsDigit(c)) { file += c - '0'; continue; }
                int t = "pnbrqk".IndexOf(char.ToLowerInvariant(c)) + 1;
                if (t <= 0) throw new FormatException("bad piece '" + c + "'");
                p.Sq[file + 8 * rank] = (sbyte)(char.IsUpper(c) ? t : -t);
                file++;
            }
            p.WhiteToMove = f.Length < 2 || f[1] == "w";
            p.Castling = 0;
            if (f.Length > 2) foreach (char c in f[2]) p.Castling |= c == 'K' ? 1 : c == 'Q' ? 2 : c == 'k' ? 4 : c == 'q' ? 8 : 0;
            p.EnPassant = f.Length > 3 && f[3] != "-" ? SquareOf(f[3]) : -1;
            p.Halfmove = f.Length > 4 ? int.Parse(f[4]) : 0;
            p.Fullmove = f.Length > 5 ? int.Parse(f[5]) : 1;
            return p;
        }

        public string ToFen()
        {
            StringBuilder sb = new StringBuilder();
            for (int rank = 7; rank >= 0; rank--)
            {
                int empty = 0;
                for (int file = 0; file < 8; file++)
                {
                    sbyte v = Sq[file + 8 * rank];
                    if (v == 0) { empty++; continue; }
                    if (empty > 0) { sb.Append(empty); empty = 0; }
                    char c = "pnbrqk"[Math.Abs(v) - 1];
                    sb.Append(v > 0 ? char.ToUpperInvariant(c) : c);
                }
                if (empty > 0) sb.Append(empty);
                if (rank > 0) sb.Append('/');
            }
            sb.Append(WhiteToMove ? " w " : " b ");
            string cs = (Castling & 1) != 0 ? "K" : "";
            cs += (Castling & 2) != 0 ? "Q" : "";
            cs += (Castling & 4) != 0 ? "k" : "";
            cs += (Castling & 8) != 0 ? "q" : "";
            sb.Append(cs.Length > 0 ? cs : "-");
            sb.Append(' ').Append(EnPassant >= 0 ? SquareName(EnPassant) : "-");
            sb.Append(' ').Append(Halfmove).Append(' ').Append(Fullmove);
            return sb.ToString();
        }

        // ---- attacks --------------------------------------------------------------------

        private static readonly int[][] KnightD = { new[] { 1, 2 }, new[] { 2, 1 }, new[] { 2, -1 }, new[] { 1, -2 }, new[] { -1, -2 }, new[] { -2, -1 }, new[] { -2, 1 }, new[] { -1, 2 } };
        private static readonly int[][] KingD = { new[] { 1, 0 }, new[] { 1, 1 }, new[] { 0, 1 }, new[] { -1, 1 }, new[] { -1, 0 }, new[] { -1, -1 }, new[] { 0, -1 }, new[] { 1, -1 } };
        private static readonly int[][] RookD = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
        private static readonly int[][] BishopD = { new[] { 1, 1 }, new[] { 1, -1 }, new[] { -1, 1 }, new[] { -1, -1 } };

        private static bool On(int f, int r) { return f >= 0 && f < 8 && r >= 0 && r < 8; }

        /// <summary>Is square <paramref name="s"/> attacked by the side <paramref name="byWhite"/>?</summary>
        public bool Attacked(int s, bool byWhite)
        {
            int f = File(s), r = Rank(s);
            int sign = byWhite ? 1 : -1;
            // pawns
            int pr = r - sign;
            if (On(f - 1, pr) && Sq[(f - 1) + 8 * pr] == sign * P) return true;
            if (On(f + 1, pr) && Sq[(f + 1) + 8 * pr] == sign * P) return true;
            foreach (int[] d in KnightD) if (On(f + d[0], r + d[1]) && Sq[(f + d[0]) + 8 * (r + d[1])] == sign * N) return true;
            foreach (int[] d in KingD) if (On(f + d[0], r + d[1]) && Sq[(f + d[0]) + 8 * (r + d[1])] == sign * K) return true;
            foreach (int[] d in RookD)
                for (int ff = f + d[0], rr = r + d[1]; On(ff, rr); ff += d[0], rr += d[1])
                {
                    sbyte v = Sq[ff + 8 * rr];
                    if (v == 0) continue;
                    if (v == sign * R || v == sign * Q) return true;
                    break;
                }
            foreach (int[] d in BishopD)
                for (int ff = f + d[0], rr = r + d[1]; On(ff, rr); ff += d[0], rr += d[1])
                {
                    sbyte v = Sq[ff + 8 * rr];
                    if (v == 0) continue;
                    if (v == sign * B || v == sign * Q) return true;
                    break;
                }
            return false;
        }

        public int KingSquare(bool white)
        {
            sbyte k = (sbyte)(white ? K : -K);
            for (int i = 0; i < 64; i++) if (Sq[i] == k) return i;
            return -1;
        }

        public bool InCheck(bool white)
        {
            int k = KingSquare(white);
            return k >= 0 && Attacked(k, !white);
        }

        // ---- moves ----------------------------------------------------------------------

        private void Add(List<Move> l, int from, int to, byte flags)
        {
            l.Add(new Move { From = (byte)from, To = (byte)to, Flags = flags });
        }

        /// <summary>Moves that obey how the pieces move, ignoring whether they leave the king in check.</summary>
        public List<Move> PseudoLegal()
        {
            List<Move> l = new List<Move>(48);
            int sign = WhiteToMove ? 1 : -1;
            for (int s = 0; s < 64; s++)
            {
                sbyte v = Sq[s];
                if (v == 0 || Math.Sign(v) != sign) continue;
                int f = File(s), r = Rank(s), t = Math.Abs(v);
                switch (t)
                {
                    case P:
                        {
                            int r1 = r + sign, start = WhiteToMove ? 1 : 6, last = WhiteToMove ? 7 : 0;
                            if (On(f, r1) && Sq[f + 8 * r1] == 0)
                            {
                                Pawn(l, s, f + 8 * r1, 0, r1 == last, sign);
                                int r2 = r + 2 * sign;
                                if (r == start && Sq[f + 8 * r2] == 0) Add(l, s, f + 8 * r2, Move.DoublePush);
                            }
                            foreach (int df in new[] { -1, 1 })
                            {
                                if (!On(f + df, r1)) continue;
                                int to = (f + df) + 8 * r1;
                                if (Sq[to] != 0 && Math.Sign(Sq[to]) == -sign) Pawn(l, s, to, Move.Capture, r1 == last, sign);
                                else if (to == EnPassant) Add(l, s, to, (byte)(Move.Capture | Move.EnPassant));
                            }
                            break;
                        }
                    case N: Steps(l, s, KnightD, sign); break;
                    case K:
                        Steps(l, s, KingD, sign);
                        Castles(l, s, sign);
                        break;
                    case B: Slides(l, s, BishopD, sign); break;
                    case R: Slides(l, s, RookD, sign); break;
                    case Q: Slides(l, s, BishopD, sign); Slides(l, s, RookD, sign); break;
                }
            }
            return l;
        }

        private void Pawn(List<Move> l, int from, int to, byte flags, bool promo, int sign)
        {
            if (!promo) { Add(l, from, to, flags); return; }
            foreach (sbyte pc in new sbyte[] { Q, R, B, N })
                l.Add(new Move { From = (byte)from, To = (byte)to, Flags = flags, Promo = (sbyte)(pc * sign) });
        }

        private void Steps(List<Move> l, int s, int[][] ds, int sign)
        {
            int f = File(s), r = Rank(s);
            foreach (int[] d in ds)
            {
                int ff = f + d[0], rr = r + d[1];
                if (!On(ff, rr)) continue;
                sbyte v = Sq[ff + 8 * rr];
                if (v == 0) Add(l, s, ff + 8 * rr, 0);
                else if (Math.Sign(v) == -sign) Add(l, s, ff + 8 * rr, Move.Capture);
            }
        }

        private void Slides(List<Move> l, int s, int[][] ds, int sign)
        {
            int f = File(s), r = Rank(s);
            foreach (int[] d in ds)
                for (int ff = f + d[0], rr = r + d[1]; On(ff, rr); ff += d[0], rr += d[1])
                {
                    sbyte v = Sq[ff + 8 * rr];
                    if (v == 0) { Add(l, s, ff + 8 * rr, 0); continue; }
                    if (Math.Sign(v) == -sign) Add(l, s, ff + 8 * rr, Move.Capture);
                    break;
                }
        }

        private void Castles(List<Move> l, int s, int sign)
        {
            bool white = sign > 0;
            int home = white ? 4 : 60;
            if (s != home || InCheck(white)) return;
            int kSide = white ? 1 : 4, qSide = white ? 2 : 8;
            if ((Castling & kSide) != 0 && Sq[home + 1] == 0 && Sq[home + 2] == 0 && Sq[home + 3] == sign * R
                && !Attacked(home + 1, !white) && !Attacked(home + 2, !white))
                Add(l, home, home + 2, Move.Castle);
            if ((Castling & qSide) != 0 && Sq[home - 1] == 0 && Sq[home - 2] == 0 && Sq[home - 3] == 0 && Sq[home - 4] == sign * R
                && !Attacked(home - 1, !white) && !Attacked(home - 2, !white))
                Add(l, home, home - 2, Move.Castle);
        }

        /// <summary>Every legal move.</summary>
        public List<Move> Legal()
        {
            List<Move> l = PseudoLegal();
            List<Move> ok = new List<Move>(l.Count);
            foreach (Move m in l)
            {
                Position n = Clone();
                n.Apply(m);
                if (!n.InCheck(WhiteToMove)) ok.Add(m);
            }
            return ok;
        }

        /// <summary>Play a move (assumed legal) on this position.</summary>
        public void Apply(Move m)
        {
            sbyte v = Sq[m.From];
            int sign = Math.Sign(v);
            bool capture = Sq[m.To] != 0 || (m.Flags & Move.EnPassant) != 0;
            // castling rights: moving the king or a rook, or capturing a rook at home
            foreach (int s in new[] { (int)m.From, (int)m.To })
            {
                if (s == 4) Castling &= ~3;
                if (s == 60) Castling &= ~12;
                if (s == 7) Castling &= ~1;
                if (s == 0) Castling &= ~2;
                if (s == 63) Castling &= ~4;
                if (s == 56) Castling &= ~8;
            }
            if ((m.Flags & Move.EnPassant) != 0) Sq[m.To - 8 * sign] = 0;
            if ((m.Flags & Move.Castle) != 0)
            {
                bool king = m.To > m.From;
                int rookFrom = king ? m.From + 3 : m.From - 4, rookTo = king ? m.From + 1 : m.From - 1;
                Sq[rookTo] = Sq[rookFrom];
                Sq[rookFrom] = 0;
            }
            Sq[m.To] = m.Promo != 0 ? m.Promo : v;
            Sq[m.From] = 0;
            EnPassant = (m.Flags & Move.DoublePush) != 0 ? (m.From + m.To) / 2 : -1;
            Halfmove = (Math.Abs(v) == P || capture) ? 0 : Halfmove + 1;
            if (!WhiteToMove) Fullmove++;
            WhiteToMove = !WhiteToMove;
        }

        // ---- the result -----------------------------------------------------------------

        public enum Outcome { Playing, WhiteWins, BlackWins, Stalemate, FiftyMoves, DeadPosition }

        public Outcome Result()
        {
            if (Legal().Count == 0)
                return InCheck(WhiteToMove) ? (WhiteToMove ? Outcome.BlackWins : Outcome.WhiteWins) : Outcome.Stalemate;
            if (Halfmove >= 100) return Outcome.FiftyMoves;
            if (Dead()) return Outcome.DeadPosition;
            return Outcome.Playing;
        }

        /// <summary>Neither side can ever mate: K v K, K+minor v K, K+B v K+B with bishops on one colour.</summary>
        public bool Dead()
        {
            List<int> minors = new List<int>();
            for (int s = 0; s < 64; s++)
            {
                int t = Math.Abs(Sq[s]);
                if (t == 0 || t == K) continue;
                if (t == P || t == R || t == Q) return false;
                minors.Add(s);
            }
            if (minors.Count <= 1) return true;
            // only bishops, all on squares of one colour
            bool allBishops = true;
            int colour = -1;
            foreach (int s in minors)
            {
                if (Math.Abs(Sq[s]) != B) { allBishops = false; break; }
                int c = (File(s) + Rank(s)) & 1;
                if (colour < 0) colour = c; else if (c != colour) { allBishops = false; break; }
            }
            return allBishops;
        }

        // ---- notation -------------------------------------------------------------------

        /// <summary>Standard algebraic notation for a legal move in this position ("Nf3", "exd6", "O-O", "e8=Q#").</summary>
        public string San(Move m)
        {
            string s;
            int t = Math.Abs(Sq[m.From]);
            if ((m.Flags & Move.Castle) != 0) s = m.To > m.From ? "O-O" : "O-O-O";
            else
            {
                bool capture = (m.Flags & Move.Capture) != 0;
                if (t == P) s = (capture ? ((char)('a' + File(m.From))).ToString() + "x" : "") + SquareName(m.To);
                else
                {
                    s = "NBRQK"[t - 2].ToString();
                    // disambiguate against other pieces of the same kind that can reach the square
                    bool sameFile = false, sameRank = false, other = false;
                    foreach (Move o in Legal())
                    {
                        if (o.To != m.To || o.From == m.From || Sq[o.From] != Sq[m.From]) continue;
                        other = true;
                        if (File(o.From) == File(m.From)) sameFile = true;
                        if (Rank(o.From) == Rank(m.From)) sameRank = true;
                    }
                    if (other)
                    {
                        if (!sameFile) s += (char)('a' + File(m.From));
                        else if (!sameRank) s += (char)('1' + Rank(m.From));
                        else s += SquareName(m.From);
                    }
                    s += (capture ? "x" : "") + SquareName(m.To);
                }
                if (m.Promo != 0) s += "=" + "NBRQ"[Math.Abs(m.Promo) - 2];
            }
            Position n = Clone();
            n.Apply(m);
            if (n.InCheck(n.WhiteToMove)) s += n.Legal().Count == 0 ? "#" : "+";
            return s;
        }

        /// <summary>Leaf count of the move tree to <paramref name="depth"/> - the standard move-generator test.</summary>
        public long Perft(int depth)
        {
            if (depth == 0) return 1;
            List<Move> moves = Legal();
            if (depth == 1) return moves.Count;
            long n = 0;
            foreach (Move m in moves)
            {
                Position c = Clone();
                c.Apply(m);
                n += c.Perft(depth - 1);
            }
            return n;
        }
    }
}
