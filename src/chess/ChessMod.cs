using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Chess.Common;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Serializing;
using HarmonyLib;
using Managers.Server.Services;
using MelonLoader;
using Players;
using Players.Services;
using Players.Visuals.Camera;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.State;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Services;
using Ships.Services;
using UnityEngine;

[assembly: MelonInfo(typeof(Chess.ChessMod), "Chess", "1.0.0", "EndlessSkys")]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]

namespace Chess
{
    public struct ChessMsg : IBroadcast
    {
        public byte Kind;
        public uint Ship;
        public ushort Part;
        public string Text;
    }

    /// <summary>
    /// Chess: a buildable chess table (next to the bed in the build menu).
    /// Stand at White's or Black's end and press E to sit and play; from the
    /// sides you watch. The full rules, every move animated, the game saved
    /// with the ship - and a hard enough jolt of the ship knocks the pieces
    /// over, the way it threw them.
    /// </summary>
    public class ChessMod : MelonMod
    {
        public static ModChannel<ChessMsg> Net;
        public static MelonPreferences_Category Cat;

        public override void OnInitializeMelon()
        {
            Log.Logger = LoggerInstance;
            Cat = MelonPreferences.CreateCategory("Chess");
            var knock = Cat.CreateEntry("KnockOverAt", 30f, description: "How hard a jolt (m/s^2, about 3 g) it takes to start knocking pieces over.");
            ChessServer.KnockAt = Mathf.Max(5f, knock.Value);

            ChessParts.Register();
            HarmonyLib.Harmony h = HarmonyInstance;
            PartKit.Patch(h);
            h.Patch(AccessTools.Method(typeof(ShipTargetProvisionService), "GetTargetsInWorld"), postfix: new HarmonyMethod(typeof(ChessMod), nameof(TargetsPostfix)));
            MethodInfo interact = typeof(ShipInteractionService).GetMethods(Refl.Any).FirstOrDefault(m => m.Name.StartsWith("RpcLogic___StartInteract_ServerRPC"));
            if (interact != null) h.Patch(interact, prefix: new HarmonyMethod(typeof(ChessMod), nameof(StartInteractPrefix)));
            h.Patch(AccessTools.Method(typeof(PlayerCamerasUpdater), "PlaceWalkingCamera"), prefix: new HarmonyMethod(typeof(ChessView), nameof(ChessView.CameraPrefix)));
            // left click picks a set up and puts it down elsewhere, game and all
            Pickable.Patch(h, s => ChessParts.IsChess(s.id));

            Net = new ModChannel<ChessMsg>(Write, Read);
            Net.OnServer = OnServerMsg;
            Net.OnClient = m => { if ((m.Kind & 0x80) != 0) ChessView.OnReply(m); };
            ChessView.Init();
            LoggerInstance.Msg("ready - build a Chess Table, Chess Board or Small Chess Board (next to the bed); F at White's or Black's end to play, F again to get up.");
        }

        private static void Write(Writer w, ChessMsg m)
        {
            w.WriteUInt8Unpacked(m.Kind);
            w.WriteUInt32(m.Ship);
            w.WriteUInt16(m.Part);
            w.WriteString(m.Text ?? "");
        }

        private static ChessMsg Read(Reader r)
        {
            return new ChessMsg { Kind = r.ReadUInt8Unpacked(), Ship = r.ReadUInt32(), Part = r.ReadUInt16(), Text = r.ReadStringAllocated() };
        }

        private static void OnServerMsg(NetworkConnection conn, ChessMsg m)
        {
            TrackedPlayerServer p = Svc.Get<PlayersServerTracker>()?.GetTrackedPlayerFromConnectionId(conn.ClientId);
            if (p == null) return;
            string reply;
            try { reply = ChessServer.Handle(p, m); }
            catch (Exception e) { reply = "That did not work."; Log.Error("chess request " + m.Kind + ": " + e); }
            LastReply = reply;
            if (reply != null) Net.ToClient(conn, new ChessMsg { Kind = (byte)(m.Kind | 0x80), Ship = m.Ship, Part = m.Part, Text = reply });
        }

        public static string LastReply;

        // A chess table is a beacon underneath (that is where its game is kept):
        // keep it off targeting computers, and never open the beacon's name editor on it.
        private static void TargetsPostfix(IReadOnlyCollection<Target> __result)
        {
            List<Target> list = __result as List<Target>;
            if (list == null) return;
            IShipsClientProvider ships = Svc.Get<IShipsClientProvider>();
            if (ships == null) return;
            list.RemoveAll(t =>
            {
                if (t.TargetType != TargetType.Beacon) return false;
                BeaconState b;
                return ships.TryGetPartState<BeaconState>(t.Beacon, out b) && ChessGame.IsChess(b.Name);
            });
        }

        private static bool StartInteractPrefix(Managers.Interface.Model.PartInteractNetworkRequest __0)
        {
            try
            {
                IShipsServerProvider ships = Svc.Get<IShipsServerProvider>();
                StatefulPart part;
                if (ships != null && ships.TryGetStatefulPart(new PartContext { ShipId = __0.ShipId, PartId = __0.PartId }, out part) && ChessParts.IsChess(part.Settings.id))
                    return false;
            }
            catch { }
            return true;
        }

        public override void OnUpdate()
        {
            try { Net.Update(); } catch (Exception e) { Log.Once("net", "network: " + e.Message); }
            try { PartKit.Tick(); } catch (Exception e) { Log.Once("partkit-tick", "research/menu placement: " + e.Message); }
            try { ChessServer.Update(); } catch (Exception e) { Log.Once("server", "chess host: " + e); }
            try { ChessView.Update(); } catch (Exception e) { Log.Once("view", "chess view: " + e); }
        }

        public override void OnGUI()
        {
            try { ChessView.OnGUI(); } catch (Exception e) { Log.Once("gui", "chess panel: " + e); }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName) { Svc.Clear(); ChessView.Reset(); }

        public static IEnumerator VerifyProbe(string probe, Action<string, bool, string> check, Action<string> note)
        {
            return ChessProbe.Run(probe, check, note);
        }
    }
}
