using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace LateJoin
{
    [BepInPlugin("rebateman.latejoin", MOD_NAME, "0.1.2")]
    internal sealed class Entry : BaseUnityPlugin
    {
        private const string MOD_NAME = "Late Join";

        internal static readonly ManualLogSource logger = BepInEx.Logging.Logger.CreateLogSource(MOD_NAME);
        
        private static readonly FieldInfo removeFilterFieldInfo = AccessTools.Field(typeof(PhotonNetwork), "removeFilter");
        private static readonly FieldInfo keyByteSevenFieldInfo = AccessTools.Field(typeof(PhotonNetwork), "keyByteSeven");
        private static readonly FieldInfo serverCleanOptionsFieldInfo = AccessTools.Field(typeof(PhotonNetwork), "ServerCleanOptions");
        private static readonly MethodInfo raiseEventInternalMethodInfo = AccessTools.Method(typeof(PhotonNetwork), "RaiseEventInternal");

        public ConfigEntry<bool>? AllowInShop;
        public ConfigEntry<bool>? AllowInTruck;
        public ConfigEntry<bool>? AllowInLevel;
        public ConfigEntry<bool>? AllowInArea;

        private void RunManager_ChangeLevelHook(Action<RunManager, bool, bool, RunManager.ChangeLevelType> orig, RunManager self, bool _completedLevel, bool _levelFailed, RunManager.ChangeLevelType _changeLevelType)
        {
            if (_levelFailed || !PhotonNetwork.IsMasterClient)
            {
                orig.Invoke(self, _completedLevel, _levelFailed, _changeLevelType);
                return;
            }

            var runManagerPUN = AccessTools.Field(typeof(RunManager), "runManagerPUN").GetValue(self);
            var runManagerPhotonView = AccessTools.Field(typeof(RunManagerPUN), "photonView").GetValue(runManagerPUN) as PhotonView;

            PhotonNetwork.RemoveBufferedRPCs(runManagerPhotonView!.ViewID);

            foreach (var photonView in FindObjectsOfType<PhotonView>())
            {
                if (photonView.gameObject.scene.buildIndex == -1)
                    continue;

                ClearPhotonCache(photonView);
            }

            orig.Invoke(self, _completedLevel, false, _changeLevelType);


            if (SemiFunc.RunIsShop() && AllowInShop.Value)
            {
                logger.LogDebug("opening the room at shop");
                SteamManager.instance.UnlockLobby();
                PhotonNetwork.CurrentRoom.IsOpen = true;
            }
            else if (SemiFunc.RunIsLobby() && AllowInTruck.Value)
            {
                logger.LogDebug("opening the room at lobby");
                SteamManager.instance.UnlockLobby();
                PhotonNetwork.CurrentRoom.IsOpen = true;
            }
            else if (SemiFunc.RunIsLevel() && AllowInLevel.Value)
            {
                logger.LogInfo("opening the room at level");
                SteamManager.instance.UnlockLobby();
                PhotonNetwork.CurrentRoom.IsOpen = true;
            }
            else if (SemiFunc.RunIsArena() && AllowInArea.Value)
            {
                logger.LogInfo("opening the room at arena");
                SteamManager.instance.UnlockLobby();
                PhotonNetwork.CurrentRoom.IsOpen = true;
            }
            else
            {
                SteamManager.instance.LockLobby();
                logger.LogInfo("closing the room");
                PhotonNetwork.CurrentRoom.IsOpen = false;
            }
            
        }

        private static void PlayerAvatar_SpawnHook(Action<PlayerAvatar, Vector3, Quaternion> orig, PlayerAvatar self, Vector3 position, Quaternion rotation)
        {

            if (!(bool)AccessTools.Field(typeof(PlayerAvatar), "spawned").GetValue(self))
            {
                PunManager.instance.SyncAllDictionaries();
                orig.Invoke(self, position, rotation);

            }

        }
        private static void PlayerAvatar_StartHook(Action<PlayerAvatar> orig, PlayerAvatar self)
        {
            orig.Invoke(self);

            if (!PhotonNetwork.IsMasterClient)
                return;
            
            self.photonView.RPC("LoadingLevelAnimationCompletedRPC", RpcTarget.AllBuffered);
        }

        private static void ClearPhotonCache(PhotonView photonView)
        {
            var removeFilter = removeFilterFieldInfo.GetValue(null) as ExitGames.Client.Photon.Hashtable;
            var keyByteSeven = keyByteSevenFieldInfo.GetValue(null);
            var serverCleanOptions = serverCleanOptionsFieldInfo.GetValue(null) as RaiseEventOptions;
            
            removeFilter![keyByteSeven] = photonView.InstantiationId;
            serverCleanOptions!.CachingOption = EventCaching.RemoveFromRoomCache;
            raiseEventInternalMethodInfo.Invoke(null, [(byte) 202, removeFilter, serverCleanOptions, SendOptions.SendReliable]);
        }
        
        private void Awake()
        {
            AllowInShop = Config.Bind("General", "Allow in shop", true, "Determines whether someone can join your room when you are in the shop.");
            AllowInTruck = Config.Bind("General", "Allow in truck", true, "Determines whether someone can join your room when you are in the truck.");
            AllowInLevel = Config.Bind("General", "Allow in level", true, "Determines whether someone can join your room when you are in a level ( active game ).");
            AllowInArea = Config.Bind("General", "Allow in area", true, "Determines whether someone can join your room when you are in the fighting area (the room were losers go).");


            logger.LogDebug("Hooking `RunManager.ChangeLevel`");
            new Hook(AccessTools.Method(typeof(RunManager), "ChangeLevel"), RunManager_ChangeLevelHook);
            
            logger.LogDebug("Hooking `PlayerAvatar.Spawn`");
            new Hook(AccessTools.Method(typeof(PlayerAvatar), "Spawn"), PlayerAvatar_SpawnHook);

            logger.LogDebug("Hooking `PlayerAvatar.Start`");
            new Hook(AccessTools.Method(typeof(PlayerAvatar), "Start"), PlayerAvatar_StartHook);
        }
    }
}
