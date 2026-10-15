using System;
using System.Globalization;

namespace Chess
{
    /// <summary>
    /// Everything a chess table knows, kept in its part state (a BeaconState
    /// name underneath, like the Graves headstone) so it saves with the ship
    /// and reaches every player the way any part state does.
    ///
    /// CHESS|1|fen|whiteId:name|blackId:name|lastUci:lastSan:captured|status|drawOffer|knockedHex|knockDir|seq|free
    /// (free: "F" in free play - no rules, no turns - else "-"; older boards have no such field)
    /// </summary>
    public class ChessGame
    {
        public const string Prefix = "CHESS|1|";

        public string Fen = Position.StartFen;
        public uint White, Black;
        public string WhiteName = "", BlackName = "";
        public string Last = "", LastSan = "";
        public char Captured = '-';     // FEN letter of the piece the last move took, '-' for none
        public string Status = "";      // "" playing; M1/M0 mate (1 = white won); R1/R0 resignation; S stalemate; F fifty moves; D dead position; A agreed draw
        public char DrawOffer = '-';    // 'w' or 'b': that side offers a draw
        public ulong Knocked;           // squares whose piece has been knocked over
        public int KnockDir;            // degrees, board frame: 0 = towards black (+z), 90 = towards the h-file (+x)
        public int Seq;                 // bumps on every change
        public bool Free;               // free play: any seated player moves any piece anywhere

        public bool Over { get { return Status.Length > 0; } }
        public Position Pos() { return Position.FromFen(Fen); }

        public string Encode()
        {
            return Prefix + string.Join("|", new[]
            {
                Fen,
                White + ":" + Clean(WhiteName),
                Black + ":" + Clean(BlackName),
                Last + ":" + LastSan + ":" + Captured,
                Status,
                DrawOffer.ToString(),
                Knocked.ToString("x", CultureInfo.InvariantCulture),
                KnockDir.ToString(CultureInfo.InvariantCulture),
                Seq.ToString(CultureInfo.InvariantCulture),
                Free ? "F" : "-",
            });
        }

        public static bool IsChess(string s) { return s != null && s.StartsWith(Prefix, StringComparison.Ordinal); }

        public static bool TryDecode(string s, out ChessGame g)
        {
            g = null;
            if (!IsChess(s)) return false;
            try
            {
                string[] p = s.Substring(Prefix.Length).Split('|');
                if (p.Length < 9) return false;
                g = new ChessGame { Fen = p[0] };
                Seat(p[1], out g.White, out g.WhiteName);
                Seat(p[2], out g.Black, out g.BlackName);
                string[] last = p[3].Split(':');
                g.Last = last.Length > 0 ? last[0] : "";
                g.LastSan = last.Length > 1 ? last[1] : "";
                g.Captured = last.Length > 2 && last[2].Length > 0 ? last[2][0] : '-';
                g.Status = p[4];
                g.DrawOffer = p[5].Length > 0 ? p[5][0] : '-';
                g.Knocked = ulong.Parse(p[6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                g.KnockDir = int.Parse(p[7], CultureInfo.InvariantCulture);
                g.Seq = int.Parse(p[8], CultureInfo.InvariantCulture);
                g.Free = p.Length > 9 && p[9] == "F";
                Position.FromFen(g.Fen);   // must parse
                return true;
            }
            catch { g = null; return false; }
        }

        private static void Seat(string s, out uint id, out string name)
        {
            int c = s.IndexOf(':');
            id = uint.Parse(c < 0 ? s : s.Substring(0, c), CultureInfo.InvariantCulture);
            name = c < 0 ? "" : s.Substring(c + 1);
        }

        private static string Clean(string s)
        {
            s = (s ?? "").Replace("|", "/").Replace(":", ";");
            return s.Length > 24 ? s.Substring(0, 24) : s;
        }

        public string StatusText()
        {
            switch (Status)
            {
                case "M1": return "Checkmate - White wins.";
                case "M0": return "Checkmate - Black wins.";
                case "R1": return "Black resigned - White wins.";
                case "R0": return "White resigned - Black wins.";
                case "S": return "Stalemate - a draw.";
                case "F": return "Fifty moves without a capture or pawn move - a draw.";
                case "D": return "Neither side can mate - a draw.";
                case "A": return "Draw agreed.";
            }
            if (Free) return "Free play - no rules: move any piece anywhere.";
            Position p = Pos();
            return (p.WhiteToMove ? "White" : "Black") + " to move" + (p.InCheck(p.WhiteToMove) ? " - check!" : ".");
        }

        public static string OutcomeCode(Position.Outcome o)
        {
            switch (o)
            {
                case Position.Outcome.WhiteWins: return "M1";
                case Position.Outcome.BlackWins: return "M0";
                case Position.Outcome.Stalemate: return "S";
                case Position.Outcome.FiftyMoves: return "F";
                case Position.Outcome.DeadPosition: return "D";
            }
            return "";
        }
    }
}
