using System;
using System.Collections.Generic;
using System.Linq;
using Chess.Common;
using Managers.Interface.Services;
using Players;
using Players.Interface.Model;
using Players.Interface.Services;
using Players.Services;
using Players.Visuals.Camera;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.State;
using Ships.Interface.Services;
using Ships.Services;
using Ships.Visuals;
using Ships.Parts.Common.Model.Reactions;
using UI.Common;
using UI.Interface.Services;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chess
{
    /// <summary>
    /// Sitting at (or watching) a table. Where you stand when you press E
    /// decides it: at White's or Black's end you take that seat (if it is
    /// free); anywhere else you watch from where you were.
    ///
    /// At the table you are in the game's own "using a part" state, the one
    /// the research table and targeting computer use: you don't walk or turn,
    /// F gets up (the game's Leave key, as for every seat and machine), and
    /// ESC opens the pause menu as usual. The camera looks down on the board
    /// from your end; click a piece, then where it goes.
    /// </summary>
    public static class ChessView
    {
        public enum Mode { None, White, Black, Watch }
        public static Mode Current = Mode.None;
        public static PartContext Table;
        public static string Message = "";
        public static int Selected = -1, Hover = -1;
        private static Vector3 _watchDir = Vector3.back;
        private static Mode _endAtEntry = Mode.Watch;
        private static ModMenu _panel;
        private static float _enterTime;
        private static Vector3 _fromPos;
        private static Quaternion _fromRot;
        private static GameObject _rig;
        private static Move? _pendingPromo;
        private static MenuToken _token;
        private static bool _entered;

        public static bool Active { get { return Current != Mode.None; } }
        public static bool PanelOpen { get { return _panel != null && _panel.IsOpen; } }
        /// <summary>Where the panel starts, in pixels from the left.</summary>
        public static float PanelLeft { get { return _panel != null && _panel.IsOpen ? _panel.Window.x : Screen.width; } }
        public static string Debug = "";

        public static void Init()
        {
            _panel = new ModMenu("CHESS", 0xC4E55) { Width = 300f, Height = 430f, Draw = DrawPanel, Passive = true, HintText = "" };
        }

        public static ChessTableVisual Visual() { return VisualOf(Table); }

        public static ChessTableVisual VisualOf(PartContext ctx)
        {
            ShipsClientTracker ships = Svc.Get<ShipsClientTracker>();
            ShipPartVisuals v;
            IShipPartVisual pv;
            if (ships == null || !ships.TryGetShipVisuals(ctx.ShipId, out v) || v == null || !v.TryGetFloatingPartVisual(ctx.PartId, out pv) || pv == null || pv.GameObject == null) return null;
            return pv.GameObject.GetComponentInChildren<ChessTableVisual>(true);
        }

        /// <summary>Which end of the board a point is at (board frame): -z is White's, +z Black's.</summary>
        public static Mode SideAt(Vector3 local, out Vector3 dir)
        {
            dir = new Vector3(local.x, 0f, local.z);
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.back;
            dir.Normalize();
            // "mostly straight on": within 40 degrees of the middle of that end
            if (dir.z < -0.766f) return Mode.White;
            if (dir.z > 0.766f) return Mode.Black;
            return Mode.Watch;
        }

        /// <summary>E on a table: ask the host for the seat at this end, or to watch.</summary>
        public static void Enter(PartContext table)
        {
            ChessTableVisual v = VisualOf(table);
            Camera cam = Camera.main;
            if (v == null || cam == null) return;
            Vector3 dir;
            Mode side = SideAt(v.transform.InverseTransformPoint(cam.transform.position), out dir);
            Table = table;
            _watchDir = dir;
            _endAtEntry = side;
            _entered = true;
            _fromPos = cam.transform.position;
            _fromRot = cam.transform.rotation;
            Message = "";
            ChessGame g = v.Game ?? new ChessGame();
            uint me = MyId();
            if (side != Mode.Watch)
            {
                uint seat = side == Mode.White ? g.White : g.Black;
                if (seat == 0 || seat == me) { Send(ChessServer.KindSit, side == Mode.White ? "w" : "b"); return; }
                Message = (side == Mode.White ? "White" : "Black") + "'s seat is taken - watching.";
            }
            Send(ChessServer.KindWatch, "");
        }

        public static uint MyId()
        {
            PlayersClientTracker cp = Svc.Get<PlayersClientTracker>();
            return cp != null && cp.LocalPlayer != null ? cp.LocalPlayer.Id : 0u;
        }

        /// <summary>Get up: what F does (the game's own Leave).</summary>
        public static void Leave()
        {
            Svc.Get<IShipInteractionNetworkService>()?.StopInteract();
        }

        public static void Send(byte kind, string text)
        {
            if (!ChessMod.Net.ToServer(new ChessMsg { Kind = kind, Ship = Table.ShipId, Part = Table.PartId, Text = text ?? "" })) Message = "Not connected.";
        }

        public static void OnReply(ChessMsg m) { if (!string.IsNullOrEmpty(m.Text)) Message = m.Text; }

        public static bool Paused()
        {
            IPlayerPauseStateProxy p = Svc.Get<IPlayerPauseStateProxy>();
            return p != null && p.IsPaused;
        }

        /// <summary>Is the local player (by the game's own state) using a chess table, and which?</summary>
        public static bool AtTable(out PartContext table)
        {
            table = default(PartContext);
            PlayersClientTracker cp = Svc.Get<PlayersClientTracker>();
            if (cp == null || cp.LocalPlayer == null) return false;
            PlayerState st = cp.LocalPlayer.State;
            if (st.Type != PlayerStateType.InteractingWithGenericPart) return false;
            table = new PartContext { ShipId = st.EntityId, PartId = st.PartId };
            return VisualOf(table) != null;
        }

        // ---- every frame ---------------------------------------------------------------------

        public static void Update()
        {
            PartContext at;
            if (!AtTable(out at)) { if (Active) Stop(); return; }
            if (!Active || at.ShipId != Table.ShipId || at.PartId != Table.PartId) Start(at);
            ChessTableVisual v = Visual();
            ChessGame g = v.Game ?? new ChessGame();
            uint me = MyId();
            Mode mode = g.White == me && g.Black == me ? (_endAtEntry == Mode.Black ? Mode.Black : Mode.White)
                : g.White == me ? Mode.White : g.Black == me ? Mode.Black : Mode.Watch;
            if (mode != Current)
            {
                // a seat taken (or lost): swing round to it from wherever the camera is now
                if (mode == Mode.Watch && Current != Mode.Watch) _watchDir = Current == Mode.Black ? Vector3.forward : Vector3.back;
                Current = mode;
                Camera c = Camera.main;
                if (c != null) { _fromPos = c.transform.position; _fromRot = c.transform.rotation; }
                _enterTime = Time.time;
            }
            bool paused = Paused();
            if (paused && _panel.IsOpen) _panel.Close();
            if (!paused && !_panel.IsOpen) OpenPanel();
            Hover = paused ? -1 : PointedSquare(v);
            Mouse mouse = Mouse.current;
            if (!paused && mouse != null && mouse.leftButton.wasPressedThisFrame && !OverPanel(mouse.position.ReadValue())) Click(Hover);
            IEnumerable<int> targets = null;
            if (Selected >= 0) targets = g.Free ? new List<int>() : g.Pos().Legal().Where(m => m.From == Selected).Select(m => (int)m.To).Distinct().ToList();
            v.ShowMarks(Selected, targets, Hover);
        }

        private static void Start(PartContext at)
        {
            if (Active) Stop();
            if (!_entered || at.ShipId != Table.ShipId || at.PartId != Table.PartId)
            {
                // at a table without E on this client (the host put us there): watch from where we stand
                Table = at;
                Camera cam = Camera.main;
                ChessTableVisual tv = Visual();
                if (cam != null && tv != null) { _endAtEntry = SideAt(tv.transform.InverseTransformPoint(cam.transform.position), out _watchDir); _fromPos = cam.transform.position; _fromRot = cam.transform.rotation; }
            }
            _entered = false;
            Current = Mode.Watch;
            _enterTime = Time.time;
            Selected = -1; Hover = -1; _pendingPromo = null;
            HideRig(true);
            // The game's own list of open menus shows the cursor while we are here. Not a
            // closable menu, so ESC still opens the pause menu.
            if (_token == null)
            {
                GameObject go = new GameObject("ChessViewMenuToken");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _token = go.AddComponent<MenuToken>();
            }
            Menus()?.TrackMenu(_token);
            OpenPanel();
        }

        private static void Stop()
        {
            ChessTableVisual v = Visual();
            if (v != null) v.ShowMarks(-1, null, -1);
            Current = Mode.None;
            Selected = -1; Hover = -1; _pendingPromo = null;
            HideRig(false);
            if (_panel.IsOpen) _panel.Close();
            if (_token != null) Menus()?.UntrackMenu(_token);
        }

        /// <summary>A new scene: nothing of the old one is left to tidy.</summary>
        public static void Reset()
        {
            Current = Mode.None;
            Selected = -1; Hover = -1; _pendingPromo = null; _entered = false;
            _rig = null;
            if (_panel != null && _panel.IsOpen) _panel.Close();
        }

        private static MenuTracker Menus()
        {
            MenuTracker m = Svc.Get<IClosableMenuService>() as MenuTracker;
            return m != null ? m : UnityEngine.Object.FindFirstObjectByType<MenuTracker>();
        }

        private static void OpenPanel()
        {
            _panel.Open();
            _panel.Window = new Rect(Screen.width - _panel.Window.width - 16f, 150f, _panel.Window.width, _panel.Window.height);   // below the game's objectives list
        }

        private static void HideRig(bool hide)
        {
            try
            {
                if (_rig == null)
                {
                    PlayerCamera pc = UnityEngine.Object.FindFirstObjectByType<PlayerCamera>();
                    if (pc != null) foreach (Transform t in pc.GetComponentsInChildren<Transform>(true)) if (t.name == "PlayerRig") { _rig = t.gameObject; break; }
                }
                if (_rig != null) _rig.SetActive(!hide);
            }
            catch { }
        }

        private static bool OverPanel(Vector2 mouse)
        {
            return _panel.IsOpen && _panel.Window.Contains(new Vector2(mouse.x, Screen.height - mouse.y));
        }

        // ---- the camera ----------------------------------------------------------------------

        public const float EyeBack = 0.62f, EyeUp = 1.3f, AimBack = 0.08f;

        /// <summary>The eye and the point it looks at, in the table's frame, from the end <paramref name="back"/>.</summary>
        public static void Seat(Vector3 back, ChessDims d, out Vector3 eye, out Vector3 look)
        {
            // the same view of any set: a small board is looked at from closer
            Vector3 center = new Vector3(0f, d.BoardY, 0f);
            eye = center + (back * EyeBack + Vector3.up * EyeUp) * d.View;
            look = center + back * AimBack * d.View;
        }

        /// <summary>
        /// High over your own end, looking down on the board at about 65
        /// degrees: the whole board in view and the near squares not squashed.
        /// Watching, the same from the side you came from.
        /// </summary>
        public static bool CameraPose(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            ChessTableVisual v = Active ? Visual() : null;
            if (v == null) return false;
            Transform t = v.transform;
            Vector3 back = Current == Mode.White ? Vector3.back : Current == Mode.Black ? Vector3.forward : _watchDir;
            Vector3 eye, look;
            Seat(back, v.Dims, out eye, out look);
            if (!CeilingOff) eye = UnderCeiling(v, eye, look, back);
            Vector3 wEye = t.TransformPoint(eye), wLook = t.TransformPoint(look);
            Quaternion want = Quaternion.LookRotation(wLook - wEye, t.up);
            // ease in from where the player was looking
            float k = Mathf.Clamp01((Time.time - _enterTime) / 0.5f);
            Debug = "mode " + Current + " k " + k.ToString("F2") + " eye " + eye.ToString("F2") + " look " + look.ToString("F2");
            k = k * k * (3f - 2f * k);
            pos = Vector3.Lerp(_fromPos, wEye, k);
            rot = Quaternion.Slerp(_fromRot, want, k);
            return true;
        }

        /// <summary>How thick a ceiling (a floor tile) is taken to be, below the voxel's floor level.</summary>
        public const float CeilingThick = 0.2f;
        /// <summary>Self test only: the old camera, which went up through a low ceiling.</summary>
        public static bool CeilingOff;
        public static string CeilingNote = "";

        /// <summary>
        /// The eye kept under the ceiling (a floor tile of the ship above the set), with room for
        /// the camera's near plane. Lowered, it moves back as far as it came down, so the view
        /// stays as wide - looking at the board more from the side instead.
        /// </summary>
        public static Vector3 UnderCeiling(ChessTableVisual v, Vector3 eye, Vector3 look, Vector3 back)
        {
            CeilingNote = "";
            IShipStateRead ship; Transform shipT;
            // the table's own ship (known from where we sat down), else the ship the set hangs under
            ShipsClientTracker ships = Svc.Get<ShipsClientTracker>();
            ship = null; shipT = null;
            if (ships == null || !ships.TryGetShip(Table.ShipId, out ship) || ship == null || !ships.TryGetShipVisualTransform(Table.ShipId, out shipT) || shipT == null)
                if (!v.TryShip(out ship, out shipT)) { CeilingNote = "no ship found"; return eye; }
            Transform t = v.transform;
            if (Vector3.Dot(shipT.up, t.up) < 0.98f) { CeilingNote = "set not level with the ship"; return eye; }
            Camera cam = Camera.main;
            float near = cam != null ? cam.nearClipPlane : 0.1f;
            float fov = cam != null ? cam.fieldOfView : 60f, aspect = cam != null ? cam.aspect : 1.78f;
            // the near plane's corners reach this far from the eye
            float halfH = near * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float margin = Mathf.Sqrt(near * near + halfH * halfH * (1f + aspect * aspect)) + 0.03f;
            float dist = (eye - look).magnitude;
            for (int pass = 0; pass < 3; pass++)
            {
                float ceil = CeilingAbove(ship, shipT, t, look, eye, margin);
                if (float.IsInfinity(ceil)) { if (pass == 0) CeilingNote = "open above"; return eye; }
                float top = ceil - margin;
                if (eye.y <= top + 0.001f) { if (pass == 0) CeilingNote = "ceiling " + ceil.ToString("F2") + " (eye " + eye.y.ToString("F2") + ") - clear"; return eye; }
                float dy = Mathf.Max(top - look.y, 0.12f * v.Dims.View);
                float across = Mathf.Sqrt(Mathf.Max(0f, dist * dist - dy * dy));
                eye = look + back * across + Vector3.up * dy;
                CeilingNote = "ceiling " + ceil.ToString("F2") + ": eye lowered to " + eye.y.ToString("F2") + ", " + across.ToString("F2") + " back";
            }
            return eye;
        }

        /// <summary>
        /// The lowest ceiling over the way from the board to the eye and round the eye (set frame, y of
        /// its underside), or +infinity. A ceiling is a floor tile in a voxel above the board.
        /// </summary>
        public static float CeilingAbove(IShipStateRead ship, Transform shipT, Transform t, Vector3 look, Vector3 eye, float margin)
        {
            float best = float.PositiveInfinity;
            HashSet<Vector2Int> seen = new HashSet<Vector2Int>();
            List<Vector3> pts = new List<Vector3>();
            for (int i = 0; i <= 8; i++) pts.Add(Vector3.Lerp(look, eye, i / 8f));
            foreach (Vector3 o in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back }) pts.Add(eye + o * margin);
            Vector3 boardShip = shipT.InverseTransformPoint(t.TransformPoint(look));
            int k0 = Mathf.FloorToInt(boardShip.y + 0.5f) + 1;    // the first voxel whose floor is above the board
            foreach (Vector3 p in pts)
            {
                Vector3 s = shipT.InverseTransformPoint(t.TransformPoint(p));
                Vector2Int col = new Vector2Int(Mathf.RoundToInt(s.x), Mathf.RoundToInt(s.z));
                if (!seen.Add(col)) continue;
                for (int k = k0; k <= k0 + 4; k++)
                {
                    Ships.Grid.Model.ShipVoxel vx = ship.GetVoxel(new Vector3Int(col.x, k, col.y));
                    if (vx == null || !vx.Floor.HasValue) continue;
                    // a floor tile's walking surface is at the voxel's bottom (y - 0.5): its underside is a little below
                    Vector3 under = shipT.TransformPoint(new Vector3(s.x, k - 0.5f - CeilingThick, s.z));
                    best = Mathf.Min(best, t.InverseTransformPoint(under).y);
                    break;
                }
            }
            return best;
        }

        public static int PointedSquare(ChessTableVisual v)
        {
            Camera cam = Camera.main;
            Mouse mouse = Mouse.current;
            if (cam == null || mouse == null) return -1;
            return SquareUnder(v, cam.ScreenPointToRay(mouse.position.ReadValue()));
        }

        public static int SquareUnder(ChessTableVisual v, Ray ray)
        {
            Transform t = v.transform;
            Plane board = new Plane(t.up, t.TransformPoint(new Vector3(0f, v.Dims.BoardY, 0f)));
            float d;
            if (!board.Raycast(ray, out d)) return -1;
            return v.Dims.SquareAt(t.InverseTransformPoint(ray.GetPoint(d)));
        }


        /// <summary>A click on a square (also used by the self test): select, reselect or move.</summary>
        public static void Click(int sq)
        {
            ChessTableVisual v = Visual();
            if (v == null || sq < 0) return;
            ChessGame g = v.Game ?? new ChessGame();
            if (Current != Mode.White && Current != Mode.Black) { Message = "Watching - take a free seat in the panel to play."; return; }
            Position pos = g.Pos();
            uint me = MyId();
            if (g.Free)
            {
                // free play: any piece, anywhere
                if (Selected < 0 || Selected == sq) { Selected = pos.Sq[sq] != 0 && Selected != sq ? sq : -1; Message = ""; return; }
                Send(ChessServer.KindMove, Position.SquareName(Selected) + Position.SquareName(sq));
                Selected = -1;
                return;
            }
            if (g.Over) { Message = g.StatusText(); return; }
            bool mine = (pos.WhiteToMove && g.White == me) || (!pos.WhiteToMove && g.Black == me);
            sbyte piece = pos.Sq[sq];
            bool own = piece != 0 && (piece > 0) == pos.WhiteToMove;
            if (Selected < 0 || own)
            {
                if (!mine) { Message = "Wait for your move."; Selected = -1; return; }
                if (!own) { Selected = -1; return; }
                Selected = sq;
                Message = "";
                return;
            }
            List<Move> moves = pos.Legal().Where(m => m.From == Selected && m.To == sq).ToList();
            Selected = -1;
            if (moves.Count == 0) { Message = "That piece can't go there."; return; }
            if (moves.Count > 1) { _pendingPromo = moves[0]; Message = "Promote to:"; return; }   // a promotion: ask what to
            Send(ChessServer.KindMove, moves[0].Uci());
        }

        // ---- the panel --------------------------------------------------------------------

        private static void DrawPanel(ModMenu m)
        {
            ChessTableVisual v = Visual();
            ChessGame g = v != null && v.Game != null ? v.Game : new ChessGame();
            uint me = MyId();
            GUILayout.Label("White: " + Who(g.White, g.WhiteName, me));
            GUILayout.Label("Black: " + Who(g.Black, g.BlackName, me));
            GUILayout.Space(4);
            GUILayout.Label(g.StatusText());
            if (g.LastSan.Length > 0) GUILayout.Label("Last move: " + g.LastSan, ModMenu.Note);
            if (g.DrawOffer != '-' && !g.Over) GUILayout.Label((g.DrawOffer == 'w' ? "White" : "Black") + " offers a draw.", ModMenu.Note);
            if (g.Knocked != 0) GUILayout.Label(ChessServer.CountBits(g.Knocked) + " piece(s) knocked over.", ModMenu.Note);
            GUILayout.Space(6);
            bool seated = g.White == me || g.Black == me;
            if (_pendingPromo.HasValue)
            {
                GUILayout.BeginHorizontal();
                foreach (var kv in new[] { new KeyValuePair<string, int>("Queen", 5), new KeyValuePair<string, int>("Rook", 4), new KeyValuePair<string, int>("Bishop", 3), new KeyValuePair<string, int>("Knight", 2) })
                    if (GUILayout.Button(kv.Key, GUILayout.Height(30)))
                    {
                        Move pm = _pendingPromo.Value;
                        pm.Promo = (sbyte)(g.Pos().WhiteToMove ? kv.Value : -kv.Value);
                        Send(ChessServer.KindMove, pm.Uci());
                        _pendingPromo = null;
                        Message = "";
                    }
                GUILayout.EndHorizontal();
            }
            if (g.Knocked != 0 && GUILayout.Button("Stand the pieces back up", GUILayout.Height(28))) Send(ChessServer.KindStandUp, "");
            if (seated)
            {
                if (!g.Over && !g.Free)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(g.DrawOffer != '-' && g.DrawOffer != (g.White == me ? 'w' : 'b') ? "Accept draw" : "Offer draw")) Send(ChessServer.KindDraw, "");
                    if (GUILayout.Button("Resign")) Send(ChessServer.KindResign, "");
                    GUILayout.EndHorizontal();
                }
                if (g.Over || g.Free || (g.White == me && g.Black == me)) { if (GUILayout.Button("New game", GUILayout.Height(28))) Send(ChessServer.KindNewGame, ""); }
                // the rules can be switched off for this board: free play (and back on, from a new game)
                if (GUILayout.Button(g.Free ? "Revert to chess rules" : "Free play (no rules)")) Send(ChessServer.KindFree, g.Free ? "0" : "1");
                // nobody on the other side: take it too and play both colours
                if (g.White == me && g.Black == 0 && GUILayout.Button("Also play Black")) Send(ChessServer.KindSit, "b");
                if (g.Black == me && g.White == 0 && GUILayout.Button("Also play White")) Send(ChessServer.KindSit, "w");
            }
            else
            {
                GUILayout.BeginHorizontal();
                if (g.White == 0 && GUILayout.Button("Sit as White")) { _endAtEntry = Mode.White; Send(ChessServer.KindSit, "w"); }
                if (g.Black == 0 && GUILayout.Button("Sit as Black")) { _endAtEntry = Mode.Black; Send(ChessServer.KindSit, "b"); }
                GUILayout.EndHorizontal();
            }
            GUILayout.FlexibleSpace();
            // (the game already shows "[F] Leave" while you are at a board)
            if (Message.Length > 0) GUILayout.Label(Message, ModMenu.Note);
        }

        private static string Who(uint id, string name, uint me)
        {
            if (id == 0) return "(free)";
            return (string.IsNullOrEmpty(name) ? "player " + id : name) + (id == me ? " (you)" : "");
        }

        public static void OnGUI() { _panel.OnGUI(); }

        /// <summary>The camera override, called from the patched PlaceWalkingCamera.</summary>
        public static bool CameraPrefix(PlayerCamerasUpdater __instance)
        {
            Vector3 p; Quaternion r;
            if (!CameraPose(out p, out r)) return true;
            try
            {
                Refl.Call(Refl.Get(__instance, "_camera"), "UpdateCamera", p, r);
                Refl.Call(Refl.Get(__instance, "_distantCamera"), "UpdateCamera", p, r);
                return false;
            }
            catch { return true; }
        }

        /// <summary>Stands for the chess view in the game's list of open menus (so the cursor shows).</summary>
        public class MenuToken : MonoBehaviour { }
    }
}
