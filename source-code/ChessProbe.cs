using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chess.Common;
using Players;
using Players.Interface.Model;
using Players.Interface.Services;
using Players.LocalClient.Controls;
using Players.Services;
using Ships;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Settings;
using Ships.Services;
using UI.Common;
using UI.Interface.Services;
using UnityEngine;
using UnityEngine.InputSystem;
using WorldTracking.Interface.Model;
using WorldTracking.Interface.Services;

namespace Chess
{
    /// <summary>Self tests for Chess, driven by DemoVerifyHarness.</summary>
    public static class ChessProbe
    {
        public static IEnumerator Run(string probe, Action<string, bool, string> check, Action<string> note)
        {
            switch (probe)
            {
                case "rules": return Rules(check, note);
                case "table": return TableGame(check, note);
                case "boards": return Boards(check, note);
                case "free": return Free(check, note);
                case "tumble": return Tumble(check, note);
                case "contact": return Contact(check, note);
                case "rock": return Rock(check, note);
                case "ceiling": return Ceiling(check, note);
                case "persist-set": return PersistSet(check, note);
                case "persist-check": return PersistCheck(check, note);
                default: check("probe " + probe, false, "unknown"); return null;
            }
        }

        /// <summary>Real seconds (the pause menu is opened on purpose here).</summary>
        private static IEnumerator Secs(float s) { float until = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < until) yield return null; }

        private static IEnumerator Rules(Action<string, bool, string> check, Action<string> note)
        {
            long n = Position.FromFen(Position.StartFen).Perft(3);
            long k = Position.FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1").Perft(2);
            check("the rules inside the game: move counts match the standard tests", n == 8902 && k == 2039, "start depth 3: " + n + ", kiwipete depth 2: " + k);
            yield break;
        }

        private static ChessGame Game(TrackedShipServer ship, ushort id)
        {
            StatefulPart p;
            ChessGame g;
            if (ship == null || !ship.TryGetStatefulPart(id, out p)) return null;
            return ChessGame.TryDecode(((BeaconState)p.State).Name, out g) ? g : new ChessGame();
        }

        private static IEnumerator Ask(TrackedShipServer ship, ushort table, byte kind, string text)
        {
            ChessMod.LastReply = null;
            ChessMod.Net.ToServer(new ChessMsg { Kind = kind, Ship = ship.Id, Part = table, Text = text });
            yield return Secs(0.35f);
        }

        /// <summary>
        /// The player's way to the table: let the game's own detector find what the
        /// crosshair is on, and press F (the Interact key) the way the game's control does.
        /// </summary>
        private static IEnumerator PressF(Action<string, bool, string> check, string where)
        {
            for (int i = 0; i < 25 && !(TestBench.Hovered() is ChessInteraction); i++) yield return null;
            string hovered = TestBench.Describe(TestBench.Hovered());
            bool isTable = TestBench.Hovered() is ChessInteraction;
            bool pressed = isTable && TestBench.PressInteract();
            check("F on the table (" + where + "): the game's crosshair finds it and F interacts", isTable && pressed, "crosshair on " + hovered + ", pressed " + pressed);
        }

        private static string StateOf(TrackedPlayerServer me)
        {
            PlayerState st = me.State;
            return st.Type + (st.Type == PlayerStateType.InteractingWithGenericPart ? " (ship " + st.EntityId + " part " + st.PartId + ")" : "");
        }

        private static bool On<T>() where T : MonoBehaviour
        {
            T c = UnityEngine.Object.FindFirstObjectByType<T>();
            return c != null && c.isActiveAndEnabled;
        }

        /// <summary>The keys the game's input asset binds to a control's handler ("OnLeave" on the leave control, ...).</summary>
        private static string KeysFor(string method)
        {
            List<string> keys = new List<string>();
            foreach (PlayerInput pi in UnityEngine.Object.FindObjectsByType<PlayerInput>(FindObjectsSortMode.None))
            {
                if (pi.actions == null) continue;
                foreach (PlayerInput.ActionEvent ev in pi.actionEvents)
                    for (int i = 0; i < ev.GetPersistentEventCount(); i++)
                        if (ev.GetPersistentMethodName(i) == method)
                        {
                            InputAction a = pi.actions.FindAction(ev.actionId);
                            if (a != null) keys.AddRange(a.bindings.Select(b => a.name + "=" + b.effectivePath));
                        }
            }
            return string.Join(", ", keys.Distinct().ToArray());
        }

        /// <summary>Everything drawn by the table, in the table's own frame (x/z extents).</summary>
        private static void Extents(ChessTableVisual vis, out float all, out float onTop)
        {
            all = 0f; onTop = 0f;
            Transform t = vis.transform;
            foreach (MeshFilter mf in vis.GetComponentsInChildren<MeshFilter>(false))
            {
                MeshRenderer r = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || r == null || !r.enabled) continue;
                bool table = mf.gameObject.name == "Table";
                Vector3[] v = mf.sharedMesh.vertices;
                foreach (Vector3 p in v)
                {
                    Vector3 l = t.InverseTransformPoint(mf.transform.TransformPoint(p));
                    float e = Mathf.Max(Mathf.Abs(l.x), Mathf.Abs(l.z));
                    all = Mathf.Max(all, e);
                    if (!table) onTop = Mathf.Max(onTop, e);
                }
            }
        }

        private static IEnumerator TableGame(Action<string, bool, string> check, Action<string> note)
        {
            PartSettings s = TestBench.Part(p => p.id == ChessParts.TableId);
            check("the chess table part exists", s != null && s.part != null, s != null ? s.fullLabel : "missing");
            if (s == null) yield break;
            check("it takes a 2 x 2 floor space", s.size == new Vector3(2f, 1f, 2f) && s.Bounds.size.x <= 2f && s.Bounds.size.z <= 2f, "size " + s.size + ", bounds " + s.Bounds.size.ToString("F2"));

            TestBench b = null;
            yield return TestBench.Create(6, x => b = x);
            Vector3 at = new Vector3(3f, -0.46f, 3f);
            ushort table = b.Place(s, at);
            yield return Secs(0.6f);
            PartContext ctx = new PartContext { ShipId = b.Ship.Id, PartId = table };
            ChessTableVisual vis = ChessView.VisualOf(ctx);
            check("a new table shows the starting position", vis != null && vis.PiecesOnBoard == 32, vis != null ? vis.PiecesOnBoard + " pieces on the board" : "no table visual");
            if (vis == null) { yield return b.Dispose(); yield break; }
            float all, onTop;
            Extents(vis, out all, out onTop);
            // the fullest the trays can get: 15 pieces a side
            float trayMax = 0f;
            for (int i = 0; i < 15; i++)
                foreach (int c in new[] { 1, -1 })
                {
                    Vector3 sp = ChessDims.Table.TraySpot(c, i);
                    trayMax = Mathf.Max(trayMax, Mathf.Abs(sp.x) + 0.021f * ChessModels.PieceScale * 0.85f, Mathf.Abs(sp.z) + 0.021f * ChessModels.PieceScale * 0.85f);
                }
            check("everything fits the 2 x 2 table: the board and full trays stay on the top",
                all <= 1.0f && onTop <= ChessModels.TableHalf && trayMax <= ChessModels.TableHalf,
                "table reaches " + all.ToString("F3") + " m from its centre (2 x 2 = 1.0), board and pieces " + onTop.ToString("F3") + ", full trays " + trayMax.ToString("F3") + " (top edge " + ChessModels.TableHalf + ")");
            yield return b.Shot("chess-table", b.Look(new Vector3(5.4f, -0.46f, 0.7f), at + new Vector3(0f, 0.8f, 0f)), 30);

            // ---- E at White's end: seated as White, in the game's own "using a part" state -------
            TrackedPlayerServer me = TestBench.Me();
            b.Look(new Vector3(3f, -0.46f, 1.55f), at + new Vector3(0f, 0.8f, 0.3f));
            yield return Secs(0.4f);
            yield return PressF(check, "at White's end");
            string track = "";
            for (int i = 0; i < 8; i++)
            {
                yield return Secs(0.25f);
                Camera c0 = Camera.main;
                track += " " + ((i + 1) * 0.25f).ToString("F2") + "s:" + ChessView.Current + "/" + (c0 != null ? Vector3.Angle(c0.transform.forward, -vis.transform.up).ToString("F0") : "-");
            }
            note("seating, camera from straight down over time:" + track + " | " + ChessView.Debug);
            ChessGame g = Game(b.Ship, table);
            check("pressing E at White's end sits you as White, in the game's own 'using a part' state",
                ChessView.Current == ChessView.Mode.White && g.White == me.Id && me.State.Type == PlayerStateType.InteractingWithGenericPart && me.State.PartId == table,
                "view " + ChessView.Current + ", white seat " + g.White + " (you are " + me.Id + "), state " + StateOf(me));
            bool walk = On<PlayerWalkingControls>(), look = On<PlayerLookControls>(), leave = On<PlayerLeavableObjectControl>(), esc = On<PlayerEscapeControl>();
            check("at the table the game holds walking and looking; its own Leave and Escape controls are live", !walk && !look && leave && esc,
                "walking " + walk + ", looking " + look + ", leave " + leave + ", escape " + esc);
            string fKeys = KeysFor("OnLeave"), escKeys = KeysFor("OnEscape");
            check("... Leave is the F key and Escape is ESC", fKeys.Contains("<Keyboard>/f") && escKeys.Contains("<Keyboard>/escape"), "leave: " + fKeys + " | escape: " + escKeys);
            MenuTracker menus = UnityEngine.Object.FindFirstObjectByType<MenuTracker>();
            check("the cursor is free for clicking pieces, and no closable menu is open (so ESC pauses)",
                Cursor.visible && Cursor.lockState == CursorLockMode.None && menus != null && !menus.HasClosableMenuOpen() && ChessView.PanelOpen,
                "cursor visible " + Cursor.visible + " lock " + Cursor.lockState + ", closable menu open " + (menus != null && menus.HasClosableMenuOpen()) + ", panel " + ChessView.PanelOpen);

            // the view: from above, the whole board and both trays on screen, clear of the panel
            Camera cam = Camera.main;
            Transform tt = vis.transform;
            float pitch = Vector3.Angle(cam.transform.forward, -tt.up);
            float panelLeft = ChessView.PanelLeft / Screen.width;
            float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
            float w = ChessModels.Half + ChessModels.Frame;
            List<Vector3> pts = new List<Vector3>();
            foreach (float x in new[] { -w, w }) foreach (float z in new[] { -w, w }) pts.Add(new Vector3(x, ChessModels.BoardY, z));
            foreach (int c in new[] { 1, -1 }) foreach (int i in new[] { 0, 14 }) pts.Add(ChessDims.Table.TraySpot(c, i));
            foreach (Vector3 p in pts)
            {
                Vector3 vp = cam.WorldToViewportPoint(tt.TransformPoint(p));
                minX = Mathf.Min(minX, vp.x); maxX = Mathf.Max(maxX, vp.x); minY = Mathf.Min(minY, vp.y); maxY = Mathf.Max(maxY, vp.y);
            }
            check("seated, the camera looks down on the board (not across it)", pitch < 30f, "view is " + pitch.ToString("F0") + " deg from straight down (fov " + cam.fieldOfView.ToString("F0") + ")");
            check("... with the whole board and both trays on screen, left of the panel", minX > 0.01f && maxX < panelLeft && minY > 0.01f && maxY < 0.99f,
                "on screen x " + minX.ToString("F2") + ".." + maxX.ToString("F2") + " (panel from " + panelLeft.ToString("F2") + "), y " + minY.ToString("F2") + ".." + maxY.ToString("F2") + ", " + Screen.width + "x" + Screen.height);
            yield return TestBench.Screen("chess-seated");

            // click e2, then e4 - through the camera, as a mouse click lands
            int e2 = Position.SquareOf("e2"), e4 = Position.SquareOf("e4");
            int under = ChessView.SquareUnder(vis, cam.ViewportPointToRay(cam.WorldToViewportPoint(tt.TransformPoint(ChessModels.SquareCenter(e2)))));
            check("the square under the cursor is the one drawn there", under == e2, "aimed at e2, got " + (under >= 0 ? Position.SquareName(under) : "none"));
            ChessView.Click(e2);
            yield return Secs(0.1f);
            yield return TestBench.Screen("chess-selected");
            ChessView.Click(e4);
            yield return Secs(0.7f);
            g = Game(b.Ship, table);
            check("clicking a pawn and its square plays the move", g.Last == "e2e4" && g.Fen.StartsWith("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b"), "last " + g.Last + ", " + g.Fen);
            yield return Ask(b.Ship, table, ChessServer.KindMove, "e7e5");
            g = Game(b.Ship, table);
            check("you can't move for the other side", g.Last == "e2e4" && ChessMod.LastReply != null, "reply: " + ChessMod.LastReply);

            // ---- ESC: the game's pause menu, with you still at the table ------------------------
            PlayerPauseToggler pause = UnityEngine.Object.FindFirstObjectByType<PlayerPauseToggler>();
            IPlayerPauseStateProxy paused = Svc.Get<IPlayerPauseStateProxy>();
            pause.TogglePause();   // what the Escape control does in this state (no closable menu open)
            yield return Secs(0.5f);
            check("ESC opens the pause menu; the chess panel steps aside, you stay seated",
                paused.IsPaused && !ChessView.PanelOpen && ChessView.Current == ChessView.Mode.White && me.State.Type == PlayerStateType.InteractingWithGenericPart,
                "paused " + paused.IsPaused + ", panel " + ChessView.PanelOpen + ", view " + ChessView.Current + ", state " + StateOf(me));
            yield return TestBench.Screen("chess-paused");
            pause.TogglePause();
            yield return Secs(0.4f);
            check("... and back from it, the panel returns", !paused.IsPaused && ChessView.PanelOpen && Cursor.visible, "paused " + paused.IsPaused + ", panel " + ChessView.PanelOpen + ", cursor " + Cursor.visible);

            // ---- F: the game's Leave -------------------------------------------------------------
            Svc.Get<Managers.Interface.Services.IShipInteractionNetworkService>().StopInteract();   // what F calls (PlayerLeavableObjectControl.OnLeave)
            yield return Secs(0.8f);
            g = Game(b.Ship, table);
            walk = On<PlayerWalkingControls>(); look = On<PlayerLookControls>();
            check("F gets you up: walking again, the seat is free, the chess view and cursor are gone",
                me.State.Type == PlayerStateType.Walking && g.White == 0 && !ChessView.Active && !ChessView.PanelOpen && walk && look && Cursor.lockState == CursorLockMode.Locked,
                "state " + StateOf(me) + ", white seat " + g.White + ", view " + ChessView.Current + ", panel " + ChessView.PanelOpen + ", walking " + walk + ", looking " + look + ", cursor " + Cursor.lockState);

            // ---- from the side you watch ---------------------------------------------------------
            b.Look(new Vector3(4.6f, -0.46f, 3f), at + new Vector3(0f, 0.8f, 0f));
            yield return Secs(0.4f);
            yield return PressF(check, "from the side");
            yield return Secs(1.0f);
            g = Game(b.Ship, table);
            pitch = Vector3.Angle(Camera.main.transform.forward, -tt.up);
            check("from the side you watch (no seat), from above too", ChessView.Current == ChessView.Mode.Watch && g.White == 0 && g.Black == 0 && me.State.Type == PlayerStateType.InteractingWithGenericPart && pitch < 30f,
                "view " + ChessView.Current + ", seats " + g.White + "/" + g.Black + ", state " + StateOf(me) + ", " + pitch.ToString("F0") + " deg from straight down");
            yield return TestBench.Screen("chess-watch");
            // take a seat from the panel while watching
            yield return Ask(b.Ship, table, ChessServer.KindSit, "b");
            yield return Secs(0.8f);
            check("a free seat can be taken from the panel while watching", Game(b.Ship, table).Black == me.Id && ChessView.Current == ChessView.Mode.Black, "black seat " + Game(b.Ship, table).Black + ", view " + ChessView.Current);
            ChessView.Leave();
            yield return Secs(0.6f);

            // ---- a whole game: take both seats and play scholar's mate ------------------------
            b.Look(new Vector3(3f, -0.46f, 1.55f), at + new Vector3(0f, 0.8f, 0.3f));
            yield return Secs(0.3f);
            yield return Ask(b.Ship, table, ChessServer.KindSit, "w");
            yield return Ask(b.Ship, table, ChessServer.KindSit, "b");
            yield return Ask(b.Ship, table, ChessServer.KindMove, "e7e8");
            check("an illegal move is refused", Game(b.Ship, table).Last == "e2e4" && ChessMod.LastReply != null, "reply: " + ChessMod.LastReply);
            foreach (string m in new[] { "e7e5", "f1c4", "b8c6", "d1h5", "g8f6", "h5f7" }) yield return Ask(b.Ship, table, ChessServer.KindMove, m);
            yield return Secs(0.8f);
            g = Game(b.Ship, table);
            vis = ChessView.VisualOf(ctx) ?? vis;
            check("scholar's mate ends the game: checkmate, White wins", g.Status == "M1" && g.LastSan == "Qxf7#", g.StatusText() + " (" + g.LastSan + ")");
            Transform q = vis.PieceAt(Position.SquareOf("f7"));
            check("the board shows it: queen on f7, the pawn in the tray", vis.PiecesOnBoard == 31 && vis.PiecesInTray == 1 && q != null && q.GetComponent<MeshFilter>().sharedMesh.name == "ChessWQ",
                vis.PiecesOnBoard + " on the board, " + vis.PiecesInTray + " in the tray, f7: " + (q != null ? q.GetComponent<MeshFilter>().sharedMesh.name : "empty"));
            yield return TestBench.Screen("chess-mate-seated");
            ChessView.Leave();
            yield return Secs(0.6f);
            yield return b.Shot("chess-mate", b.Look(new Vector3(4.9f, -0.46f, 1.1f), at + new Vector3(0f, 0.82f, 0.05f)), 30);

            // ---- a jolt: the ship's velocity jumps (the real detector, watching the ship) ------
            b.Look(new Vector3(3f, -0.46f, 1.55f), at + new Vector3(0f, 0.8f, 0.3f));
            yield return Secs(0.3f);
            yield return Ask(b.Ship, table, ChessServer.KindSit, "w");
            yield return Ask(b.Ship, table, ChessServer.KindSit, "b");
            yield return Ask(b.Ship, table, ChessServer.KindNewGame, "");
            int events = ChessServer.KnockEvents;
            // really set going (let go of, for a moment): a velocity written onto a frozen deck lasts only until the next
            // physics step writes it back, so whether the watcher saw it at all was down to timing
            yield return b.Shove(new Vector3(25f, 0f, 0f), 0.12f);
            yield return Secs(0.8f);
            b.Pin();
            yield return Secs(0.5f);
            g = Game(b.Ship, table);
            int fell = ChessServer.CountBits(g.Knocked);
            check("a sudden jolt of the ship knocks pieces over (seen by the host watching the ship)", ChessServer.KnockEvents > events && fell > 0, fell + " piece(s) over - " + ChessServer.LastKnock);
            // (a real lurch shakes the players out of their seats too, with Momentum: sit back down)
            b.Look(new Vector3(3f, -0.46f, 1.55f), at + new Vector3(0f, 0.8f, 0.3f));
            yield return Ask(b.Ship, table, ChessServer.KindSit, "w");
            yield return Ask(b.Ship, table, ChessServer.KindSit, "b");
            yield return Ask(b.Ship, table, ChessServer.KindStandUp, "");

            // ---- which way, and how many: a crash while moving along the board's +x ----------
            StatefulPart tp;
            b.Ship.TryGetStatefulPart(table, out tp);
            ChessServer.Jolt(b.Ship, tp, b.Ship.Rotation * new Vector3(-45f, 0f, 0f));    // stopping hard while going +x
            yield return Secs(0.2f);
            g = Game(b.Ship, table);
            int soft = ChessServer.CountBits(g.Knocked);
            check("pieces fall the way the ship was going when it stopped", soft > 0 && Mathf.Abs(Mathf.DeltaAngle(g.KnockDir, 90f)) < 20f, soft + " over, towards " + g.KnockDir + " deg (travel was +x = 90 deg)");
            yield return Ask(b.Ship, table, ChessServer.KindStandUp, "");
            b.Ship.TryGetStatefulPart(table, out tp);
            ChessServer.Jolt(b.Ship, tp, b.Ship.Rotation * new Vector3(0f, 0f, -140f));   // a much harder stop going +z
            yield return Secs(0.2f);
            g = Game(b.Ship, table);
            int hard = ChessServer.CountBits(g.Knocked);
            check("... and a harder crash knocks more over", hard > soft && Mathf.Abs(Mathf.DeltaAngle(g.KnockDir, 0f)) < 20f, soft + " at 45 m/s2, " + hard + " at 140 m/s2 (towards " + g.KnockDir + " deg)");
            yield return TestBench.Screen("chess-knocked-seated");
            yield return Ask(b.Ship, table, ChessServer.KindMove, "e2e4");
            check("while pieces are down you can't play on", Game(b.Ship, table).Last == "" && ChessMod.LastReply != null && ChessMod.LastReply.Contains("Stand"), "reply: " + ChessMod.LastReply);
            yield return Ask(b.Ship, table, ChessServer.KindStandUp, "");
            yield return Secs(0.5f);
            check("standing them back up", Game(b.Ship, table).Knocked == 0, "knocked mask " + Game(b.Ship, table).Knocked.ToString("x"));
            ChessView.Leave();
            yield return Secs(0.5f);
            yield return b.Dispose();
        }

        // ---- the boards without a table: on the floor (2 x 2) and small (one tile) ---------------

        private static IEnumerator Boards(Action<string, bool, string> check, Action<string> note)
        {
            TrackedPlayerServer me = TestBench.Me();
            foreach (ushort id in new[] { ChessParts.FloorBoardId, ChessParts.SmallBoardId })
            {
                PartSettings s = TestBench.Part(p => p.id == id);
                ChessDims d = ChessParts.DimsOf(id);
                string name = s != null ? s.fullLabel : "part " + id;
                check(name + ": the part exists", s != null && s.part != null, name);
                if (s == null) continue;
                TestBench b = null;
                yield return TestBench.Create(6, x => b = x);
                Vector3 at = new Vector3(3f, -0.46f, 3f);
                ushort board = b.Place(s, at);
                yield return Secs(0.6f);
                PartContext ctx = new PartContext { ShipId = b.Ship.Id, PartId = board };
                ChessTableVisual vis = ChessView.VisualOf(ctx);
                float all, onTop;
                if (vis != null) Extents(vis, out all, out onTop); else { all = 9f; onTop = 9f; }
                float tile = s.size.x * 0.5f;
                check(name + ": fits its " + s.size.x + " x " + s.size.z + " space, every piece and tray on the board's base", vis != null && vis.Dims == d && all <= tile + 0.001f && onTop <= d.Extent + 0.001f,
                    vis == null ? "no visual" : "reaches " + all.ToString("F3") + " m from its centre (space half " + tile + "), pieces " + onTop.ToString("F3") + " (base half " + d.Extent + "), " + vis.PiecesOnBoard + " pieces");
                yield return b.Shot("chess-" + (id == ChessParts.FloorBoardId ? "floor" : "small") + "-board", b.Look(new Vector3(3f + 2.0f * d.View, -0.46f, 3f - 2.0f * d.View), at + Vector3.up * 0.2f), 30);

                // F at White's end: seated, looking down on it
                yield return b.HoverAt(new Vector3(3f, -0.46f, 3f - d.Extent - 0.45f), at + new Vector3(0f, d.BoardY, -d.Half * 0.3f), 30);
                yield return PressF(check, name);
                yield return Secs(1f);
                ChessGame g = Game(b.Ship, board);
                Camera cam = Camera.main;
                Transform tt = vis.transform;
                float pitch = Vector3.Angle(cam.transform.forward, -tt.up);
                float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
                float w = d.Half + d.Frame;
                foreach (float x in new[] { -w, w })
                    foreach (float z in new[] { -w, w })
                    {
                        Vector3 vp = cam.WorldToViewportPoint(tt.TransformPoint(new Vector3(x, d.BoardY, z)));
                        minX = Mathf.Min(minX, vp.x); maxX = Mathf.Max(maxX, vp.x); minY = Mathf.Min(minY, vp.y); maxY = Mathf.Max(maxY, vp.y);
                    }
                check(name + ": seated as White, looking down on the whole board", ChessView.Current == ChessView.Mode.White && g.White == me.Id && pitch < 30f && minX > 0.01f && maxX < ChessView.PanelLeft / Screen.width && minY > 0.01f && maxY < 0.99f,
                    "view " + ChessView.Current + ", " + pitch.ToString("F0") + " deg from straight down, board on screen x " + minX.ToString("F2") + ".." + maxX.ToString("F2") + " y " + minY.ToString("F2") + ".." + maxY.ToString("F2"));
                int e2 = Position.SquareOf("e2"), e4 = Position.SquareOf("e4");
                int under = ChessView.SquareUnder(vis, cam.ViewportPointToRay(cam.WorldToViewportPoint(tt.TransformPoint(d.SquareCenter(e2)))));
                ChessView.Click(e2); ChessView.Click(e4);
                yield return Secs(0.7f);
                g = Game(b.Ship, board);
                check(name + ": a pawn moves by clicking it and its square", under == e2 && g.Last == "e2e4", "under the cursor " + (under >= 0 ? Position.SquareName(under) : "none") + ", last move " + g.Last);
                yield return TestBench.Screen("chess-" + (id == ChessParts.FloorBoardId ? "floor" : "small") + "-seated");
                ChessView.Leave();
                yield return Secs(0.6f);

                // pick it up (empty hands, left click) and put it down 1 m along: the game goes with it
                UnityEngine.Object.FindFirstObjectByType<Players.Inventory.ToolBelt>()?.ClearSelection();
                yield return Secs(0.5f);
                yield return b.HoverAt(new Vector3(3f, -0.46f, 3f - d.Extent - 0.5f), at + new Vector3(0f, d.BoardY, 0f), 30);
                string onIt = TestBench.Describe(TestBench.Hovered());
                bool picked = TestBench.Hovered() is ChessInteraction && TestBench.PressPickUp();
                yield return Secs(0.5f);
                bool holding = me.State.Type == PlayerStateType.WalkingHoldingShipPart && me.State.PartId == board;
                check(name + ": left click picks it up", picked && holding, "crosshair on " + onIt + ", player " + me.State.Type);
                if (holding)
                {
                    Vector3 newAt = at + new Vector3(-1f, 0f, 0f);
                    Svc.Get<Managers.Interface.Services.IShipInteractionNetworkService>().PlaceHeldPart(new Managers.Interface.Model.PlaceHeldPartNetworkRequest
                    {
                        ShipId = b.Ship.Id,
                        Placement = new Ships.Interface.Model.Placement.FloatingPartPlacement(newAt, Ships.Interface.Model.Placement.PartOrientation.Forward, Ships.Interface.Model.Placement.PartOrientation.Up),
                    });
                    yield return Secs(0.8f);
                    StatefulPart moved = b.Ship.StatefulParts.FirstOrDefault(p => p.Settings != null && p.Settings.id == id);
                    ChessGame mg = moved != null ? Game(b.Ship, moved.Id) : null;
                    check(name + ": ... and put down elsewhere, with the game still on it", moved != null && (moved.LocalPosition - newAt).magnitude < 0.05f && mg != null && mg.Last == "e2e4" && me.State.Type == PlayerStateType.Walking,
                        moved != null ? "now at " + moved.LocalPosition.ToString("F2") + ", last move " + (mg != null ? mg.Last : "-") + ", player " + me.State.Type : "gone");
                }
                yield return b.Dispose();
            }
        }

        // ---- free play: the rules switched off for one board ---------------------------------

        private static IEnumerator Free(Action<string, bool, string> check, Action<string> note)
        {
            PartSettings s = TestBench.Part(p => p.id == ChessParts.SmallBoardId);
            TestBench b = null;
            yield return TestBench.Create(6, x => b = x);
            Vector3 at = new Vector3(3f, -0.46f, 3f);
            ushort board = b.Place(s, at);
            yield return Secs(0.6f);
            ChessDims d = ChessParts.DimsOf(ChessParts.SmallBoardId);
            yield return b.HoverAt(new Vector3(3f, -0.46f, 3f - d.Extent - 0.45f), at + new Vector3(0f, d.BoardY, -d.Half * 0.3f), 30);
            yield return PressF(check, "Small Chess Board");
            yield return Secs(1f);
            ChessGame g = Game(b.Ship, board);
            check("seated as White at the small board", ChessView.Current == ChessView.Mode.White, "view " + ChessView.Current);
            // the panel's switch
            ChessView.Send(ChessServer.KindFree, "1");
            yield return Secs(0.6f);
            g = Game(b.Ship, board);
            check("the panel's Free play switch turns the rules off for this board", g.Free && g.StatusText().StartsWith("Free play"), "free " + g.Free + ", '" + g.StatusText() + "'");
            // the white king two squares up the board - never allowed by the rules
            int e1 = Position.SquareOf("e1"), e3 = Position.SquareOf("e3"), d7 = Position.SquareOf("d7"), d5 = Position.SquareOf("d5"), d1 = Position.SquareOf("d1"), d8 = Position.SquareOf("d8");
            ChessView.Click(e1); ChessView.Click(e3);
            yield return Secs(0.7f);
            g = Game(b.Ship, board);
            bool kingMoved = g.Pos().Sq[e3] == Position.K && g.Pos().Sq[e1] == 0;
            // a black pawn, moved by White's player (no turns)
            ChessView.Click(d7); ChessView.Click(d5);
            yield return Secs(0.7f);
            g = Game(b.Ship, board);
            bool pawnMoved = g.Pos().Sq[d5] == -Position.P;
            // the white queen straight through to take the black queen
            ChessView.Click(d1); ChessView.Click(d8);
            yield return Secs(0.7f);
            g = Game(b.Ship, board);
            bool took = g.Pos().Sq[d8] == Position.Q && g.Captured == 'q';
            check("in free play any piece goes anywhere: king e1-e3, a black pawn by White's player, the queen through to take on d8", kingMoved && pawnMoved && took,
                "king " + kingMoved + ", pawn " + pawnMoved + ", took the queen " + took + " (" + g.Fen + ")");
            yield return TestBench.Screen("chess-free-play");
            // saved with the board
            ChessGame again;
            bool kept = ChessGame.TryDecode(g.Encode(), out again) && again.Free && again.Fen == g.Fen;
            check("... free play is part of the board's saved state", kept, "decoded free " + (again != null && again.Free));
            // rules back on: a new game from the start
            ChessView.Send(ChessServer.KindFree, "0");
            yield return Secs(0.6f);
            g = Game(b.Ship, board);
            check("switching the rules back on starts a new game", !g.Free && g.Fen == Position.StartFen, "free " + g.Free + ", " + g.Fen);
            ChessView.Leave();
            yield return Secs(0.5f);
            yield return b.Dispose();
        }

        // ---- knocked over: the pieces fall, then lie loose and roll when the ship lurches -----------

        /// <summary>
        /// How far pieces go into each other, measured two ways: by the drawn meshes themselves
        /// (convex hulls of each piece's mesh, laid over it, pushed apart by the physics engine's own
        /// ComputePenetration - which knows nothing of Chess's shapes), and by Chess's own contact shapes.
        /// Only pairs where at least one piece is knocked over count.
        /// </summary>
        private class Meter
        {
            /// <summary>A piece's mesh cut into thin horizontal slabs (its own frame), each slab's part of the surface as a convex piece.</summary>
            private class Slab { public Mesh Mesh; public Bounds Box; }
            private static readonly Dictionary<Mesh, List<Slab>> _slabs = new Dictionary<Mesh, List<Slab>>();
            public const int Slabs = 48;

            private static List<Slab> SlabsOf(Mesh m)
            {
                List<Slab> list;
                if (_slabs.TryGetValue(m, out list)) return list;
                list = new List<Slab>();
                Vector3[] v = m.vertices; int[] tri = m.triangles;
                float y0 = m.bounds.min.y, y1 = m.bounds.max.y, h = (y1 - y0) / Slabs;
                for (int k = 0; k < Slabs; k++)
                {
                    float lo = y0 + k * h - 1e-6f, hi = y0 + (k + 1) * h + 1e-6f;
                    List<Vector3> pts = new List<Vector3>();
                    for (int i = 0; i < tri.Length; i += 3)
                        Clip(new List<Vector3> { v[tri[i]], v[tri[i + 1]], v[tri[i + 2]] }, lo, hi, pts);
                    if (pts.Count < 4) continue;
                    Mesh sm = new Mesh();
                    sm.SetVertices(pts);
                    int[] t = new int[(pts.Count - 2) * 3];
                    for (int i = 0; i < pts.Count - 2; i++) { t[i * 3] = 0; t[i * 3 + 1] = i + 1; t[i * 3 + 2] = i + 2; }
                    sm.SetTriangles(t, 0);
                    sm.RecalculateBounds();
                    list.Add(new Slab { Mesh = sm, Box = sm.bounds });
                }
                return _slabs[m] = list;
            }

            /// <summary>The part of a triangle between two heights (its corners added to <paramref name="into"/>).</summary>
            private static void Clip(List<Vector3> poly, float lo, float hi, List<Vector3> into)
            {
                poly = Cut(poly, lo, true);
                if (poly.Count > 0) poly = Cut(poly, hi, false);
                into.AddRange(poly);
            }

            private static List<Vector3> Cut(List<Vector3> poly, float y, bool keepAbove)
            {
                List<Vector3> o = new List<Vector3>();
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector3 a = poly[i], b = poly[(i + 1) % poly.Count];
                    bool ia = keepAbove ? a.y >= y : a.y <= y, ib = keepAbove ? b.y >= y : b.y <= y;
                    if (ia) o.Add(a);
                    if (ia != ib) o.Add(Vector3.Lerp(a, b, (y - a.y) / (b.y - a.y)));
                }
                return o;
            }

            private readonly Dictionary<Transform, List<MeshCollider>> _cols = new Dictionary<Transform, List<MeshCollider>>();
            private readonly Dictionary<Transform, Mesh> _colMesh = new Dictionary<Transform, Mesh>();
            private int _made;
            public float Hull, Model;
            public Vector3 Where;   // (set frame) between the two that go deepest into each other
            public string HullAt = "", ModelAt = "";
            public int Frames;

            private List<MeshCollider> CollidersOf(Transform t)
            {
                MeshFilter mf = t.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) return null;
                List<MeshCollider> cs;
                Mesh had;
                if (_cols.TryGetValue(t, out cs) && _colMesh.TryGetValue(t, out had) && had == mf.sharedMesh && cs.Count > 0 && cs[0] != null && cs[0].transform.localScale == t.lossyScale) return cs;
                if (cs != null) foreach (MeshCollider c in cs) if (c != null) UnityEngine.Object.Destroy(c.gameObject);
                cs = new List<MeshCollider>();
                foreach (Slab s in SlabsOf(mf.sharedMesh))
                {
                    GameObject go = new GameObject("ChessProbeSlab");
                    go.layer = 2;
                    go.transform.position = new Vector3(0f, -10000f - (_made++) * 0.5f, 0f);
                    go.transform.localScale = t.lossyScale;
                    MeshCollider c = go.AddComponent<MeshCollider>();
                    c.convex = true; c.isTrigger = true; c.sharedMesh = s.Mesh;
                    cs.Add(c);
                }
                _cols[t] = cs; _colMesh[t] = mf.sharedMesh;
                return cs;
            }

            /// <summary>How deep two drawn pieces go into each other (0 if not at all): the deepest pair of their slabs.</summary>
            public float Depth(Transform a, Transform b)
            {
                List<MeshCollider> ca = CollidersOf(a), cb = CollidersOf(b);
                if (ca == null || cb == null) return 0f;
                List<Slab> sa = SlabsOf(a.GetComponent<MeshFilter>().sharedMesh), sb = SlabsOf(b.GetComponent<MeshFilter>().sharedMesh);
                Bounds[] wb = new Bounds[sb.Count];
                for (int j = 0; j < sb.Count; j++) wb[j] = WorldBox(b, sb[j].Box);
                float most = 0f;
                for (int i = 0; i < ca.Count; i++)
                {
                    Bounds wa = WorldBox(a, sa[i].Box);
                    for (int j = 0; j < cb.Count; j++)
                    {
                        if (!wa.Intersects(wb[j])) continue;
                        Vector3 dir; float dist;
                        if (Physics.ComputePenetration(ca[i], a.position, a.rotation, cb[j], b.position, b.rotation, out dir, out dist)) most = Mathf.Max(most, dist);
                    }
                }
                return most;
            }

            private static Bounds WorldBox(Transform t, Bounds local)
            {
                Bounds w = new Bounds(t.TransformPoint(local.center), Vector3.zero);
                Vector3 e = local.extents;
                for (int i = 0; i < 8; i++) w.Encapsulate(t.TransformPoint(local.center + new Vector3((i & 1) != 0 ? e.x : -e.x, (i & 2) != 0 ? e.y : -e.y, (i & 4) != 0 ? e.z : -e.z)));
                return w;
            }

            public void Measure(ChessTableVisual vis)
            {
                Frames++;
                string w;
                float m = vis.DeepestOverlap(out w);
                if (m > Model) { Model = m; ModelAt = w; }
                HashSet<Transform> loose = vis.LooseObjects();
                List<Transform> ps = vis.PieceObjects().ToList();
                for (int i = 0; i < ps.Count; i++)
                    for (int j = i + 1; j < ps.Count; j++)
                    {
                        Transform a = ps[i], b = ps[j];
                        if (!loose.Contains(a) && !loose.Contains(b)) continue;
                        Renderer ra = a.GetComponent<Renderer>(), rb = b.GetComponent<Renderer>();
                        if (ra == null || rb == null || !ra.bounds.Intersects(rb.bounds)) continue;
                        float d = Depth(a, b);
                        if (d > Hull) { Hull = d; HullAt = vis.NameOf(a) + "/" + vis.NameOf(b); Where = vis.transform.InverseTransformPoint((a.position + b.position) * 0.5f); }
                    }
            }

            /// <summary>The meter itself: two kings a known distance apart, side by side, must overlap by what they should.</summary>
            public float SelfTest(Transform piece, float apart)
            {
                GameObject twin = new GameObject("ChessProbeTwin");
                twin.transform.SetPositionAndRotation(piece.position + piece.right * apart, piece.rotation);
                twin.transform.localScale = piece.lossyScale;
                twin.AddComponent<MeshFilter>().sharedMesh = piece.GetComponent<MeshFilter>().sharedMesh;
                float d = Depth(piece, twin.transform);
                if (_cols.ContainsKey(twin.transform)) { foreach (MeshCollider c in _cols[twin.transform]) if (c != null) UnityEngine.Object.Destroy(c.gameObject); _cols.Remove(twin.transform); }
                UnityEngine.Object.Destroy(twin);
                return d;
            }

            public string Report() { return "drawn meshes " + (Hull * 1000f).ToString("F1") + " mm (" + (HullAt.Length > 0 ? HullAt : "none") + "), contact shapes " + (Model * 1000f).ToString("F1") + " mm (" + (ModelAt.Length > 0 ? ModelAt : "none") + "), " + Frames + " frames"; }

            public void Dispose() { foreach (List<MeshCollider> cs in _cols.Values) foreach (MeshCollider c in cs) if (c != null) UnityEngine.Object.Destroy(c.gameObject); _cols.Clear(); }
        }

        /// <summary>
        /// Each slab of the drawn mesh is taken whole (its convex hull), which is a little fuller than the
        /// piece where it narrows inside the slab: two pieces whose slabs overlap by up to this may only touch.
        /// </summary>
        private const float HullSlack = 0.0015f;

        /// <summary>A photo from close up: the camera's view narrowed (a zoom) while it is taken.</summary>
        private static IEnumerator Close(TestBench b, string name, Vector3 feet, Vector3 target, float fov)
        {
            Quaternion aim = b.Look(feet, target);
            Camera cam = Camera.main;
            float was = cam.fieldOfView;
            for (int i = 0; i < 20; i++) { b.Aim(aim); cam.fieldOfView = fov; yield return null; }
            cam.fieldOfView = fov;
            yield return TestBench.Screen(name);
            cam.fieldOfView = was;
        }

        private static IEnumerator Tumble(Action<string, bool, string> check, Action<string> note)
        {
            PartSettings s = TestBench.Part(p => p.id == ChessParts.TableId);
            TestBench b = null;
            yield return TestBench.Create(6, x => b = x);
            Vector3 at = new Vector3(3f, -0.46f, 3f);
            ushort table = b.Place(s, at);
            yield return Secs(0.6f);
            ChessTableVisual vis = ChessView.VisualOf(new PartContext { ShipId = b.Ship.Id, PartId = table });
            Quaternion view = b.Look(new Vector3(4.6f, -0.46f, 1.4f), at + new Vector3(0f, 0.85f, 0f));
            yield return Secs(0.4f);
            StatefulPart tp;
            b.Ship.TryGetStatefulPart(table, out tp);
            int dom0 = vis.Dominoes, con0 = vis.Contacts;
            Meter fall = new Meter();
            {
                // (cut into slabs, the meter reads how far apart they must be pushed: at most about a slab's thickness
                // - a lower bound for deep overlaps - but it never reads an overlap that isn't there)
                Transform k = vis.PieceAt(Position.SquareOf("e1"));
                float width = 2f * 0.024f * ChessModels.PieceScale, into = fall.SelfTest(k, width - 0.01f), clear = fall.SelfTest(k, width + 0.003f);
                check("the overlap meter works: two kings' meshes 1 cm into each other read as overlapping, 3 mm apart as not", into > 0.002f && clear == 0f, "1 cm in: " + (into * 1000f).ToString("F1") + " mm, 3 mm apart: " + (clear * 1000f).ToString("F1") + " mm");
            }
            ChessServer.Jolt(b.Ship, tp, b.Ship.Rotation * new Vector3(0f, 0f, -140f));
            note("tumble: " + ChessServer.LastKnock);
            // watch one knocked piece fall: it must move over several frames, not jump
            Transform king = vis.PieceAt(Position.SquareOf("e1"));
            List<float> angles = new List<float>();
            float t0 = Time.time, flat = -1f;
            bool shot = false;
            while (Time.time < t0 + 2.4f)
            {
                b.Aim(view);
                if (king != null) { float a = Vector3.Angle(king.up, vis.transform.up); angles.Add(a); if (flat < 0f && a > 80f) flat = Time.time - t0; }
                fall.Measure(vis);
                yield return null;
                if (!shot && Time.time > t0 + 0.12f) { shot = true; yield return TestBench.Screen("chess-tumble-falling"); }
            }
            int steps = 0;
            for (int i = 1; i < angles.Count; i++) if (angles[i] - angles[i - 1] > 0.5f) steps++;
            float kingEnd = angles.Count > 0 ? angles[angles.Count - 1] : 0f;
            // (it ends flat, or propped on a piece lying in front of it)
            check("knocked pieces topple over (a fall, frame by frame - not a jump)", vis.LooseCount >= 24 && steps >= 5 && ((flat > 0.1f && flat < 1.6f) || (kingEnd > 30f && vis.LeaningCount > 0)),
                vis.LooseCount + " pieces down; the king tipped over " + steps + " frames and was flat after " + (flat >= 0f ? flat.ToString("F2") + " s" : "(never: propped at " + kingEnd.ToString("F0") + " deg)") + ", most " + (angles.Count > 0 ? angles.Max().ToString("F0") : "?") + " deg");
            check("falling pieces hit the ones in their way: pushed over, shoved along, or leant on", vis.Dominoes > dom0 || vis.Contacts > con0,
                (vis.Dominoes - dom0) + " set falling by the piece behind, " + (vis.Contacts - con0) + " pushes between pieces; " + vis.LyingCount + " lying, " + vis.LeaningCount + " leaning on another");
            check("... and while they fall, no piece goes into another", fall.Hull <= HullSlack && fall.Model <= 0.001f, "deepest overlap " + fall.Report());
            yield return Close(b, "chess-tumble-fallen-white", new Vector3(3.1f, -0.46f, 1.7f), at + new Vector3(0f, 0.83f, -0.35f), 30f);
            yield return Close(b, "chess-tumble-fallen-white-near", new Vector3(2.4f, -0.46f, 1.8f), at + new Vector3(-0.2f, 0.83f, -0.4f), 18f);
            yield return Close(b, "chess-tumble-fallen-side", new Vector3(4.3f, -0.46f, 3.1f), at + new Vector3(0.1f, 0.83f, 0f), 30f);
            yield return Close(b, "chess-tumble-fallen-black", new Vector3(2.9f, -0.46f, 4.3f), at + new Vector3(0f, 0.83f, 0.45f), 30f);
            view = b.Look(new Vector3(4.6f, -0.46f, 1.4f), at + new Vector3(0f, 0.85f, 0f));
            yield return Secs(0.3f);
            List<Vector3> before = vis.LoosePositions().ToList();
            // the ship lurches sideways (a hard shove, then held again)
            Meter roll = new Meter();
            int con1 = vis.Contacts;
            yield return b.Shove(new Vector3(4f, 0f, 0f), 0.35f, () => { b.Aim(view); roll.Measure(vis); });
            for (int i = 0; i < 60; i++) { b.Aim(view); roll.Measure(vis); yield return null; if (i == 10) yield return TestBench.Screen("chess-tumble-rolling"); }
            List<Vector3> after = vis.LoosePositions().ToList();
            float most = 0f;
            for (int i = 0; i < Mathf.Min(before.Count, after.Count); i++) most = Mathf.Max(most, (after[i] - before[i]).magnitude);
            check("... and the fallen pieces roll when the ship lurches", before.Count >= 24 && most > 0.02f,
                before.Count + " down; the furthest moved " + (most * 100f).ToString("F1") + " cm (push read from " + (vis.FromMomentum ? "Momentum" : "the set's own motion") + "; gravity for the physics engine " + Physics.gravity.ToString("F1") + ")");
            check("... knocking into each other as they go, still never through each other", roll.Hull <= HullSlack && roll.Model <= 0.001f,
                (vis.Contacts - con1) + " pushes between pieces; deepest overlap " + roll.Report());
            for (int i = 0; i < 90; i++) { b.Aim(view); yield return null; }
            List<Vector3> rest1 = vis.LoosePositions().ToList();
            yield return Secs(0.8f);
            List<Vector3> rest2 = vis.LoosePositions().ToList();
            float drift = 0f; int driftAt = -1;
            for (int i = 0; i < Mathf.Min(rest1.Count, rest2.Count); i++) if ((rest2[i] - rest1[i]).magnitude > drift) { drift = (rest2[i] - rest1[i]).magnitude; driftAt = i; }
            check("... then come to rest when the ship is still again", drift < 0.005f, "moved at most " + (drift * 1000f).ToString("F1") + " mm in 0.8 s" + (driftAt >= 0 ? " (" + vis.LooseInfo(driftAt) + ")" : ""));
            Meter rest = new Meter();
            rest.Measure(vis);
            check("... lying side by side, not through each other", vis.Overlaps() == 0 && rest.Hull <= HullSlack, vis.Overlaps() + " pair(s) of lying pieces overlapping; at rest " + rest.Report());
            yield return b.Shot("chess-tumble-rest", view, 5);
            if (rest.Hull > 0f)
            {
                // the worst pair, from close up (from the side nearer the player)
                Vector3 w = rest.Where + at;
                Vector3 from = w + new Vector3(0f, 0f, -0.9f);
                from.y = -0.46f;
                yield return Close(b, "chess-tumble-worst-pair", from, w, 9f);
                yield return Close(b, "chess-tumble-worst-pair-side", w + new Vector3(0.9f, -0.46f - w.y, 0f), w, 9f);
            }
            yield return Close(b, "chess-tumble-rest-close", new Vector3(3.1f, -0.46f, 1.7f), at + new Vector3(0f, 0.83f, -0.2f), 30f);
            fall.Dispose(); roll.Dispose(); rest.Dispose();
            if (MomentumLink.Present) check("with Momentum: the pieces feel the push Momentum gives the people aboard", vis.FromMomentum, "push read from " + (vis.FromMomentum ? "Momentum" : "the set's own motion"));
            yield return b.Dispose();
        }

        // ---- touching: a piece leans on one standing in its way, a row falls like dominoes ----------

        private static IEnumerator SetBoard(TrackedShipServer ship, ushort table, string fen, ulong knocked, int dir)
        {
            StatefulPart tp;
            if (!ship.TryGetStatefulPart(table, out tp)) yield break;
            ChessGame g = Game(ship, table);
            g.Fen = fen; g.Free = true; g.Knocked = knocked; g.KnockDir = dir;
            g.Last = ""; g.LastSan = ""; g.Status = ""; g.Captured = '-';
            ChessServer.Save(ship, tp, g);
            yield return null;
        }

        private static ulong Bits(params string[] squares) { ulong m = 0; foreach (string q in squares) m |= 1UL << Position.SquareOf(q); return m; }

        private static IEnumerator Contact(Action<string, bool, string> check, Action<string> note)
        {
            PartSettings s = TestBench.Part(p => p.id == ChessParts.TableId);
            TestBench b = null;
            yield return TestBench.Create(6, x => b = x);
            Vector3 at = new Vector3(3f, -0.46f, 3f);
            ushort table = b.Place(s, at);
            yield return Secs(0.6f);
            ChessTableVisual vis = ChessView.VisualOf(new PartContext { ShipId = b.Ship.Id, PartId = table });
            // looking along the e-file from the h side
            Quaternion view = b.Look(new Vector3(4.2f, -0.46f, 2.55f), at + new Vector3(0.08f, 0.86f, -0.3f));

            // 1. the king falls onto the queen, which stays up: it leans on her
            yield return SetBoard(b.Ship, table, "8/8/8/8/8/8/4Q3/4K3 w - - 0 1", 0, 0);
            yield return Secs(0.6f);
            yield return SetBoard(b.Ship, table, "8/8/8/8/8/8/4Q3/4K3 w - - 0 1", Bits("e1"), 0);
            Meter lean = new Meter();
            for (float t = Time.time; Time.time < t + 1.5f;) { b.Aim(view); lean.Measure(vis); yield return null; }
            float kingAt = vis.TippedAt(Position.SquareOf("e1")), queenAt = vis.TippedAt(Position.SquareOf("e2"));
            check("a piece falling onto one that stays up leans on it (it doesn't fall through it)", vis.LeaningCount == 1 && kingAt > 10f && kingAt < 85f && queenAt < 1f && lean.Hull <= HullSlack && lean.Model <= 0.001f,
                "king over " + kingAt.ToString("F0") + " deg, queen " + queenAt.ToString("F0") + " deg; deepest overlap " + lean.Report());
            yield return Close(b, "chess-contact-lean", new Vector3(4.2f, -0.46f, 2.4f), at + new Vector3(0.08f, 0.86f, -0.48f), 20f);
            view = b.Look(new Vector3(4.2f, -0.46f, 2.55f), at + new Vector3(0.08f, 0.86f, -0.3f));
            lean.Dispose();

            // 2. a row, all knocked at once: each waits for the one behind to hit it
            string row = "8/8/8/4B3/4R3/4N3/4Q3/4K3 w - - 0 1";
            yield return SetBoard(b.Ship, table, row, 0, 0);
            yield return Secs(0.9f);
            int dom0 = vis.Dominoes;
            yield return SetBoard(b.Ship, table, row, Bits("e1", "e2", "e3", "e4", "e5"), 0);
            Meter row1 = new Meter();
            string track = "";
            for (float t = Time.time; Time.time < t + 2.5f;)
            {
                b.Aim(view); row1.Measure(vis);
                if (Time.frameCount % 6 == 0) track += " " + (Time.time - t).ToString("F2") + ":" + string.Join("/", new[] { "e1", "e2", "e3", "e4", "e5" }.Select(q => vis.TippedAt(Position.SquareOf(q)).ToString("F0")).ToArray());
                yield return null;
            }
            note("contact: the row over time (degrees over, e1..e5):" + track);
            check("a row knocked over falls like dominoes: each is set going by the piece behind", vis.Dominoes - dom0 >= 3 && vis.LyingCount + vis.LeaningCount == 5 && row1.Hull <= HullSlack && row1.Model <= 0.001f,
                (vis.Dominoes - dom0) + " hit into falling, " + vis.LyingCount + " lying, " + vis.LeaningCount + " leaning; deepest overlap " + row1.Report());
            yield return Close(b, "chess-contact-dominoes", new Vector3(4.2f, -0.46f, 2.6f), at + new Vector3(0.08f, 0.84f, -0.25f), 24f);
            yield return Close(b, "chess-contact-dominoes-above", new Vector3(3.1f, -0.46f, 1.7f), at + new Vector3(0.08f, 0.83f, -0.25f), 24f);
            view = b.Look(new Vector3(4.2f, -0.46f, 2.55f), at + new Vector3(0.08f, 0.86f, -0.3f));
            // shoved sideways: they roll into each other and trade their momentum
            int con = vis.Contacts;
            Meter roll = new Meter();
            yield return b.Shove(new Vector3(-4f, 0f, 0f), 0.3f, () => { b.Aim(view); roll.Measure(vis); });
            for (int i = 0; i < 90; i++) { b.Aim(view); roll.Measure(vis); yield return null; }
            check("... and rolling, they push each other along, never through each other", roll.Hull <= HullSlack && roll.Model <= 0.001f, (vis.Contacts - con) + " pushes; deepest overlap " + roll.Report());
            row1.Dispose(); roll.Dispose();

            // 3. the host: a falling piece knocks over the one it lands on (a coin toss, more likely for heavy pieces)
            StatefulPart tp;
            int dominoes = 0, tries = 0;
            string got = "";
            for (; tries < 15 && dominoes == 0; tries++)
            {
                yield return SetBoard(b.Ship, table, row, 0, 0);
                b.Ship.TryGetStatefulPart(table, out tp);
                ChessServer.Jolt(b.Ship, tp, b.Ship.Rotation * new Vector3(0f, 0f, -ChessServer.KnockAt * 1.4f));
                got = ChessServer.LastKnock;
                ChessGame g = Game(b.Ship, table);
                if (g.Knocked != 0) dominoes = ChessServer.LastDominoes;
                yield return Secs(0.1f);
            }
            check("the host: a piece knocked over can take down the one it falls onto", dominoes > 0, "after " + tries + " jolt(s): " + got);
            yield return Secs(2f);
            yield return b.Shot("chess-contact-host-dominoes", view, 5);
            yield return b.Dispose();
        }

        // ---- a lurch too small to knock them over rocks the standing pieces ------------------------

        private static IEnumerator Rock(Action<string, bool, string> check, Action<string> note)
        {
            PartSettings s = TestBench.Part(p => p.id == ChessParts.TableId);
            TestBench b = null;
            yield return TestBench.Create(6, x => b = x);
            Vector3 at = new Vector3(3f, -0.46f, 3f);
            ushort table = b.Place(s, at);
            yield return Secs(0.6f);
            ChessTableVisual vis = ChessView.VisualOf(new PartContext { ShipId = b.Ship.Id, PartId = table });
            Quaternion view = b.Look(new Vector3(4.6f, -0.46f, 1.4f), at + new Vector3(0f, 0.85f, 0f));
            yield return Secs(0.5f);
            float knockAt = ChessServer.KnockAt;
            ChessServer.KnockAt = 100000f;   // (only the rocking here: nothing may be knocked over)
            int mostRocking = 0;
            float mostAngle = 0f, mostPush = 0f;
            bool shot = false;
            Action watch = () =>
            {
                b.Aim(view);
                mostRocking = Mathf.Max(mostRocking, vis.Rocking); mostAngle = Mathf.Max(mostAngle, vis.MostRock); mostPush = Mathf.Max(mostPush, vis.PlaneForce.magnitude);
            };
            // back and forth, as a ship bumping over something
            yield return b.Shove(new Vector3(0f, 0f, 3f), 0.12f, watch);
            yield return b.Shove(new Vector3(0f, 0f, -3f), 0.12f, watch);
            yield return b.Shove(new Vector3(0f, 0f, 3f), 0.12f, watch);
            for (int i = 0; i < 20; i++) { watch(); yield return null; if (!shot && vis.Rocking > 0) { shot = true; yield return TestBench.Screen("chess-rocking"); } }
            ChessServer.KnockAt = knockAt;
            check("a lurch too small to knock pieces over rocks them on their bases", mostRocking > 0 && mostAngle > 0.5f && mostAngle <= ChessTableVisual.MaxRock + 0.01f,
                mostRocking + " rocking at once, the most " + mostAngle.ToString("F1") + " deg over; strongest push along the board " + mostPush.ToString("F1") + " m/s2 (read from " + (vis.FromMomentum ? "Momentum" : "the set's own motion") + ")");
            yield return Secs(2f);
            float off = 0f;
            for (int sq = 0; sq < 64; sq++)
            {
                Transform t = vis.PieceAt(sq);
                if (t != null) off = Mathf.Max(off, (t.localPosition - ChessDims.Table.SquareCenter(sq)).magnitude);
            }
            ChessGame g = Game(b.Ship, table);
            check("... then they settle back onto their squares, none knocked over", vis.Rocking == 0 && off < 0.0005f && g.Knocked == 0 && vis.PiecesOnBoard == 32,
                vis.Rocking + " still rocking, furthest off its square " + (off * 1000f).ToString("F1") + " mm, knocked " + ChessServer.CountBits(g.Knocked));
            yield return b.Dispose();
        }

        // ---- a low ceiling: the camera stays under it ------------------------------------------------

        private static IEnumerator Ceiling(Action<string, bool, string> check, Action<string> note)
        {
            PartSettings s = TestBench.Part(p => p.id == ChessParts.TableId);
            HullPartSettings floor = TestBench.Part(p => p.name == "IronFloor") as HullPartSettings;
            TestBench b = null;
            yield return TestBench.Create(6, x => b = x);
            Vector3 at = new Vector3(3f, -0.46f, 3f);
            ushort table = b.Place(s, at);
            // a ceiling 2 m over the deck (floor tiles one storey up, voxel y 2), the lowest a person fits under
            for (int x = 0; x < 6; x++)
                for (int z = 0; z < 6; z++)
                    b.Ship.AddHullPart(floor, new Ships.Interface.Model.Placement.HullPartPlacement(new Ships.Interface.Model.Placement.HullPartPosition { Type = Ships.Interface.Model.VoxelPartType.Floor, VoxelCoord = new Vector3Int(x, 2, z) },
                        Ships.Interface.Model.Placement.PartOrientation.Up, Ships.Interface.Model.Placement.PartOrientation.Forward));
            yield return Secs(1f);
            PartContext ctx = new PartContext { ShipId = b.Ship.Id, PartId = table };
            ChessTableVisual vis = ChessView.VisualOf(ctx);
            Transform shipT;
            Svc.Get<Ships.Interface.Services.IShipsClientProvider>().TryGetShipVisualTransform(b.Ship.Id, out shipT);
            float before = 0f, after = 0f;
            string beforeAt = "", afterAt = "";
            foreach (bool fixOn in new[] { false, true })
            {
                ChessView.CeilingOff = !fixOn;
                b.Look(new Vector3(3f, -0.46f, 1.55f), at + new Vector3(0f, 0.8f, 0.3f));
                yield return Secs(0.4f);
                yield return PressF(check, fixOn ? "under a low ceiling" : "under a low ceiling, the old camera");
                yield return Secs(1.3f);
                Camera cam = Camera.main;
                Vector3 camShip = shipT.InverseTransformPoint(cam.transform.position);
                // the board seen, or the ceiling's top: light and dark squares side by side, near and far (empty squares)
                Texture2D tex = null;
                yield return new WaitForEndOfFrame();
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                TestBench.Save(fixOn ? "chess-ceiling-seated" : "chess-ceiling-old-camera", tex);
                float contrast = float.MaxValue;
                string where = "";
                foreach (string[] pair in new[] { new[] { "d3", "e3" }, new[] { "d6", "e6" } })
                {
                    Color dark = Patch(tex, cam, vis.transform.TransformPoint(ChessDims.Table.SquareCenter(Position.SquareOf(pair[0])))), light = Patch(tex, cam, vis.transform.TransformPoint(ChessDims.Table.SquareCenter(Position.SquareOf(pair[1]))));
                    float c = (Mathf.Abs(light.r - dark.r) + Mathf.Abs(light.g - dark.g) + Mathf.Abs(light.b - dark.b)) / 3f;
                    contrast = Mathf.Min(contrast, c);
                    where += " " + pair[0] + "/" + pair[1] + " " + c.ToString("F3");
                }
                UnityEngine.Object.Destroy(tex);
                float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f, w = ChessModels.Half + ChessModels.Frame;
                foreach (float x in new[] { -w, w })
                    foreach (float z in new[] { -w, w })
                    {
                        Vector3 vp = cam.WorldToViewportPoint(vis.transform.TransformPoint(new Vector3(x, ChessModels.BoardY, z)));
                        minX = Mathf.Min(minX, vp.x); maxX = Mathf.Max(maxX, vp.x); minY = Mathf.Min(minY, vp.y); maxY = Mathf.Max(maxY, vp.y);
                    }
                string desc = "camera at " + camShip.y.ToString("F2") + " m (deck -0.50, ceiling tiles' floor level 1.50), " + ChessView.CeilingNote + "; light/dark square contrast" + where
                    + "; board on screen x " + minX.ToString("F2") + ".." + maxX.ToString("F2") + " y " + minY.ToString("F2") + ".." + maxY.ToString("F2");
                if (fixOn)
                {
                    after = contrast; afterAt = desc;
                    check("under a 2 m ceiling the chess camera stays below it and sees the board", camShip.y < 1.5f - 0.1f && contrast > 0.06f && minX > 0.01f && maxX < ChessView.PanelLeft / Screen.width && minY > 0.01f && maxY < 0.99f, desc);
                }
                else { before = contrast; beforeAt = desc; note("ceiling: the old camera - " + desc); }
                ChessView.Leave();
                yield return Secs(0.8f);
            }
            ChessView.CeilingOff = false;
            check("... where the old camera went up through it (the measure tells the two apart)", before < after * 0.5f, "old camera contrast " + before.ToString("F3") + ", now " + after.ToString("F3"));
            yield return b.Dispose();
        }

        /// <summary>The mean colour of a small patch of the screenshot round a world point.</summary>
        private static Color Patch(Texture2D tex, Camera cam, Vector3 world)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return Color.black;
            int cx = Mathf.RoundToInt(sp.x * tex.width / Screen.width), cy = Mathf.RoundToInt(sp.y * tex.height / Screen.height), r = Mathf.Max(2, tex.width / 400);
            Color sum = Color.clear; int n = 0;
            for (int x = cx - r; x <= cx + r; x++)
                for (int y = cy - r; y <= cy + r; y++)
                    if (x >= 0 && y >= 0 && x < tex.width && y < tex.height) { sum += tex.GetPixel(x, y); n++; }
            return n > 0 ? sum / n : Color.black;
        }

        // ---- leaving and coming back: persist-set, then the harness's "reload", then persist-check ----

        private static string _expectFen, _expectLast;
        private static uint _expectShip;
        private static ushort _expectTable;

        private static IEnumerator PersistSet(Action<string, bool, string> check, Action<string> note)
        {
            PartSettings s = TestBench.Part(p => p.id == ChessParts.TableId);
            TestBench b = null;
            yield return TestBench.Create(4, x => b = x, -9f);
            Vector3 at = new Vector3(2f, -0.46f, 2f);
            ushort table = b.Place(s, at);
            yield return Secs(0.6f);
            PartContext ctx = new PartContext { ShipId = b.Ship.Id, PartId = table };
            b.Look(new Vector3(2f, -0.46f, 0.55f), at + new Vector3(0f, 0.8f, 0.3f));
            yield return Secs(0.4f);
            yield return PressF(check, "before leaving");
            yield return Secs(1f);
            ChessView.Click(Position.SquareOf("d2"));
            ChessView.Click(Position.SquareOf("d4"));
            yield return Secs(0.5f);
            yield return Ask(b.Ship, table, ChessServer.KindSit, "b");
            foreach (string m in new[] { "g8f6", "c2c4", "e7e6" }) yield return Ask(b.Ship, table, ChessServer.KindMove, m);
            yield return Secs(0.5f);
            ChessGame g = Game(b.Ship, table);
            _expectFen = g.Fen; _expectLast = g.Last; _expectShip = b.Ship.Id; _expectTable = table;
            check("before leaving: four moves played, still seated at the table", g.Last == "e7e6" && TestBench.Me().State.Type == PlayerStateType.InteractingWithGenericPart, g.Fen + ", state " + StateOf(TestBench.Me()));
            // left as it is (seated): the harness saves, quits to the menu and loads the save again
        }

        private static IEnumerator PersistCheck(Action<string, bool, string> check, Action<string> note)
        {
            if (_expectFen == null) { check("chess save: persist-set ran first", false, "no game was set up before the reload"); yield break; }
            yield return Secs(1.5f);
            ShipsServerTracker ships = Svc.Get<ShipsServerTracker>();
            TrackedShipServer ship = null; StatefulPart part = null;
            foreach (TrackedShipServer sh in ships.Ships)
                foreach (StatefulPart p in sh.GetAllStatefulParts<BeaconState>())
                    if (p.Settings != null && p.Settings.id == ChessParts.TableId) { ship = sh; part = p; }
            check("after leaving and loading the save, the table is still there", part != null, part != null ? "ship " + ship.Id + " (was " + _expectShip + "), part " + part.Id + " (was " + _expectTable + ")" : "no chess table in the world");
            if (part == null) yield break;
            ChessGame g = Game(ship, part.Id);
            TrackedPlayerServer me = TestBench.Me();
            check("... with the game exactly where it was left", g.Fen == _expectFen && g.Last == _expectLast, g.Fen + " last " + g.Last + " (expected " + _expectFen + " last " + _expectLast + ")");
            check("... you come back standing, and the seats you held are free again", me.State.Type == PlayerStateType.Walking && g.White == 0 && g.Black == 0 && !ChessView.Active,
                "state " + StateOf(me) + ", seats " + g.White + "/" + g.Black + ", view " + ChessView.Current);
            PartContext ctx = new PartContext { ShipId = ship.Id, PartId = part.Id };
            ChessTableVisual vis = ChessView.VisualOf(ctx);
            check("... and the board shows it", vis != null && vis.PiecesOnBoard == 32 && vis.PieceAt(Position.SquareOf("d4")) != null && vis.PieceAt(Position.SquareOf("d2")) == null,
                vis == null ? "no visual" : vis.PiecesOnBoard + " pieces, d4 " + (vis.PieceAt(Position.SquareOf("d4")) != null) + ", d2 empty " + (vis.PieceAt(Position.SquareOf("d2")) == null));
            // sit down again and play on
            TestBench b = TestBench.For(ship);
            Vector3 at = part.LocalPosition;
            b.Look(at + part.LocalRotation * new Vector3(0f, 0f, -1.45f), at + part.LocalRotation * new Vector3(0f, 0.8f, 0.3f));
            yield return Secs(0.5f);
            yield return PressF(check, "after the reload");
            yield return Secs(1f);
            if (Game(ship, part.Id).Knocked != 0)
            {
                // the deck was saved hovering where the test built it; loading, it drops onto the ground
                note("chess save: the reloaded deck dropped and knocked " + ChessServer.CountBits(Game(ship, part.Id).Knocked) + " piece(s) over; standing them up");
                yield return Ask(ship, part.Id, ChessServer.KindStandUp, "");
            }
            ChessView.Click(Position.SquareOf("b1"));
            ChessView.Click(Position.SquareOf("c3"));
            yield return Secs(0.6f);
            g = Game(ship, part.Id);
            check("... sit down again and play on from there", g.Last == "b1c3" && g.White == me.Id, "last " + g.Last + ", white " + g.White + ", view " + ChessView.Current + " - " + b.Where());
            yield return TestBench.Screen("chess-after-reload");
            ChessView.Leave();
            yield return Secs(0.5f);
            foreach (ushort id in ship.StatefulParts.Select(p => p.Id).ToList()) ship.RemoveStatefulPart(id);
            foreach (var hp in ship.GetHullParts().Keys.ToList()) ship.RemoveHullPart(hp);
        }
    }
}
