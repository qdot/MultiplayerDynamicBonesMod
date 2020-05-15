using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MelonLoader;
using Unity;
using UnityEngine;
using Harmony;
using NET_SDK;
using UnityEngine.SceneManagement;
using UnhollowerBaseLib;
using UnityEngine.Experimental.PlayerLoop;
using VRC.Core;
using VRC;
using Viveport;
using System.Reflection;
using Microsoft.CSharp.RuntimeBinder;
using System.Runtime.InteropServices;
using Il2CppSystem.Reflection;
using NET_SDK.Reflection;
using VRCSDK2;
using Il2CppSystem;
using IntPtr = System.IntPtr;
using ConsoleColor = System.ConsoleColor;
using UnhollowerRuntimeLib;
using IL2CPP = UnhollowerBaseLib.IL2CPP;
using VRC.SDKBase;
using UnityEngine.Events;
using VRC.UI;
using UnityEngine.UI;
using System.IO;
using Il2CppSystem.Threading;
using System.Diagnostics;
//using UnityEngine.UI;

namespace DBMod
{
    internal class NDB : MelonMod
    {
        private static class NDBConfig
        {
            public static float distanceToDisable;
            public static float colliderSizeLimit;
            public static int dynamicBoneUpdateRate;
            public static bool distanceDisable;
            public static bool enabledByDefault;
            public static bool disallowInsideColliders;
            public static bool onlyForMyBones;
            public static bool onlyForMeAndFriends;
            public static bool disallowDesktoppers;

        }
        private static NDB _Instance;

        private Dictionary<string, System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool>> old_avatarsInScene;
        private Dictionary<string, System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool>> avatarsInScene;
        private Dictionary<string, DynamicBone[]> old_originalSettings;
        private Dictionary<string, DynamicBone[]> originalSettings;
        private GameObject localPlayer;
        private GameObject old_localPlayer;

        private bool enabled = true;

        private static AvatarInstantiatedDelegate onAvatarInstantiatedDelegate;
        private static PlayerLeftDelegate onPlayerLeftDelegate;
        private static LeaveRoom leaveRoom;


        private static void Hook(IntPtr target, IntPtr detour)
        {
            typeof(Imports).GetMethod("Hook", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { target, detour });
        }

        public override void OnLevelWasLoaded(int level)
        {
            //originalSettings = new Dictionary<string, DynamicBone[]>();
            //avatarsInScene = new Dictionary<string, System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[]>>();
            //localPlayer = null;
            //MelonModLogger.Log(ConsoleColor.Blue, "New scene loaded; reseted");
        }

        //private void AddButton()
        //{
        //    //foreach (Transform t in QuickMenu.prop_QuickMenu_0.GetComponentsInChildren<Transform>()) MelonModLogger.Log(t.gameObject.name);
        //    GameObject button = UnityEngine.Object.Instantiate(QuickMenu.prop_QuickMenu_0.transform.Find("ShortcutMenu/WorldsButton").gameObject);
        //    button.transform.localScale *= 4f;
        //    button.name = "MDBT";
        //    button.GetComponentInChildren<Text>().text = "Dynamic Bones mod";
        //    button.GetComponentInChildren<UiTooltip>().text = "enables and disables the multiplyer dynamic bones mod";
        //    button.GetComponentInChildren<UiTooltip>().alternateText = "enables and disables the multiplyer dynamic bones mod";
        //    //button.transform.localPosition = QuickMenu.prop_QuickMenu_0.transform.Find("ShortcutMenu/WorldsButton").localPosition;
        //    //button.transform.localPosition.Set(button.transform.localPosition.x - 10, button.transform.localPosition.y, button.transform.localPosition.z);
        //    button.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
        //    button.GetComponent<Button>().onClick.AddListener(new System.Action(ToggleState));
        //}

        private delegate void AvatarInstantiatedDelegate(IntPtr @this, IntPtr avatarPtr, IntPtr avatarDescriptorPtr, bool loaded);
        private delegate void PlayerLeftDelegate(IntPtr @this, IntPtr vrcPlayerApiPtr);
        private delegate void LeaveRoom();

        public unsafe override void OnApplicationStart()
        {
            _Instance = this;

            ModPrefs.RegisterCategory("NDB", "Multiplayer Dynamic Bones");
            ModPrefs.RegisterPrefBool("NDB", "EnabledByDefault", true, "Enabled by default");
            ModPrefs.RegisterPrefBool("NDB", "OnlyMe", false, "Only I can interact with other bones");
            ModPrefs.RegisterPrefBool("NDB", "OnlyFriends", false, "Only me and friends can interact with my and friend's bones");
            ModPrefs.RegisterPrefBool("NDB", "DisallowDesktoppers", false, "Desktoppers's colliders and bones won't be multiplayer'd");
            ModPrefs.RegisterPrefBool("NDB", "DistanceDisable", true, "Disable bones if beyond a distance");
            ModPrefs.RegisterPrefFloat("NDB", "DistanceToDisable", 2f, "Distance limit");
            ModPrefs.RegisterPrefBool("NDB", "DisallowInsideColliders", true, "Disallow inside colliders");
            ModPrefs.RegisterPrefFloat("NDB", "ColliderSizeLimit", 1f, "Collider size limit");
            ModPrefs.RegisterPrefInt("NDB", "DynamicBoneUpdateRate", 60, "Dynamic bone update rate");

            MelonModLogger.Log(ConsoleColor.DarkGreen, "Saved default configuration");

            NDBConfig.enabledByDefault = ModPrefs.GetBool("NDB", "EnabledByDefault");
            NDBConfig.disallowInsideColliders = ModPrefs.GetBool("NDB", "DisallowInsideColliders");
            NDBConfig.distanceToDisable = ModPrefs.GetFloat("NDB", "DistanceToDisable");
            NDBConfig.distanceDisable = ModPrefs.GetBool("NDB", "DistanceDisable");
            NDBConfig.colliderSizeLimit = ModPrefs.GetFloat("NDB", "ColliderSizeLimit");
            NDBConfig.onlyForMyBones = ModPrefs.GetBool("NDB", "OnlyMe");
            NDBConfig.onlyForMeAndFriends = ModPrefs.GetBool("NDB", "OnlyFriends");
            NDBConfig.dynamicBoneUpdateRate = ModPrefs.GetInt("NDB", "DynamicBoneUpdateRate");
            NDBConfig.disallowDesktoppers = ModPrefs.GetBool("NDB", "DisallowDesktoppers");


            enabled = NDBConfig.enabledByDefault;
            IntPtr funcToHook = (IntPtr)typeof(VRCAvatarManager.MulticastDelegateNPublicSealedVoGaVRBoObVoInBeInGaUnique).GetField("NativeMethodInfoPtr_Invoke_Public_Virtual_New_Void_GameObject_VRC_AvatarDescriptor_Boolean_0", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);

            Hook(funcToHook, new System.Action<IntPtr, IntPtr, IntPtr, bool>(OnAvatarInstantiated).Method.MethodHandle.GetFunctionPointer());
            onAvatarInstantiatedDelegate = Marshal.GetDelegateForFunctionPointer<AvatarInstantiatedDelegate>(*(IntPtr*)funcToHook);
            MelonModLogger.Log(ConsoleColor.Blue, $"Hooked OnAvatarInstantiated? {((onAvatarInstantiatedDelegate != null) ? "Yes!" : "No: critical error!!")}");

            funcToHook = (IntPtr)typeof(VRCUiCursor).GetField("NativeMethodInfoPtr_OnPlayerLeft_Private_Void_VRCPlayerApi_0", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            Hook(funcToHook, new System.Action<IntPtr, IntPtr>(OnPlayerLeft).Method.MethodHandle.GetFunctionPointer());
            onPlayerLeftDelegate = Marshal.GetDelegateForFunctionPointer<PlayerLeftDelegate>(*(IntPtr*)funcToHook);
            MelonModLogger.Log(ConsoleColor.Blue, $"Hooked OnPlayerLeft? {((onPlayerLeftDelegate != null) ? "Yes!" : "No: critical error!!")}");

            funcToHook = (IntPtr)typeof(RoomManagerBase).GetField("NativeMethodInfoPtr_Method_Public_Static_Void_0", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            Hook(funcToHook, new System.Action(OnLeavingRoom).Method.MethodHandle.GetFunctionPointer());
            leaveRoom = Marshal.GetDelegateForFunctionPointer<LeaveRoom>(*(IntPtr*)funcToHook);
            MelonModLogger.Log(ConsoleColor.Blue, $"Hooked LeaveRoom? {((leaveRoom != null) ? "Yes!" : "No: critical error!!")}");

            //funcToHook = (IntPtr)typeof(VRCSimpleUiPageFooter).GetField("NativeMethodInfoPtr_Exit_Public_Void_31", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            //Hook(funcToHook, new System.Action<IntPtr>((@this) =>
            //{
            //    MelonModLogger.Log(ConsoleColor.Cyan, "Goodbye!");
            //    Thread.Sleep(3000);
            //    System.Environment.Exit(0);
            //    //System.Environment.FailFast("VRChat Exit Fix");
            //}).Method.MethodHandle.GetFunctionPointer());

            MelonModLogger.Log(ConsoleColor.Green, $"NDBMod is {((enabled == true) ? "enabled" : "disabled")}");

            if (onPlayerLeftDelegate == null || onAvatarInstantiatedDelegate == null || leaveRoom == null)
            {
                this.enabled = false;
                MelonModLogger.Log(ConsoleColor.Red, "Multiplayer Dynamic Bones mod suffered a critical error! Please remove from the Mods folder to avoid game crashes! \nContact me for support.");
            }


        }

        private static void OnLeavingRoom()
        {
            _Instance.originalSettings = new Dictionary<string, DynamicBone[]>();
            _Instance.avatarsInScene = new Dictionary<string, System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool>>();
            _Instance.localPlayer = null;
            MelonModLogger.Log(ConsoleColor.Blue, "New scene loaded; reset");
            leaveRoom();
        }

        private static void OnPlayerLeft(IntPtr @this, IntPtr vrcPlayerApiPtr)
        {
            VRCPlayerApi playerApi = new VRCPlayerApi(vrcPlayerApiPtr);

            _Instance.old_avatarsInScene = _Instance.avatarsInScene;
            _Instance.old_originalSettings = _Instance.originalSettings;
            _Instance.old_localPlayer = _Instance.localPlayer;

            _Instance.RemoveBonesOfGameObjectInAllPlayers(_Instance.avatarsInScene[playerApi.gameObject.GetComponent<VRCPlayer>().namePlate.prop_String_0].Item4);
            _Instance.DeleteOriginalColliders(playerApi.gameObject.GetComponent<VRCPlayer>().namePlate.prop_String_0);
            _Instance.RemovePlayerFromDict(playerApi.gameObject.GetComponent<VRCPlayer>().namePlate.prop_String_0);
            MelonModLogger.Log(ConsoleColor.Blue, $"Player {playerApi.gameObject.GetComponent<VRCPlayer>().namePlate.prop_String_0} left the room so all his dynamic bones info was deleted");
            onPlayerLeftDelegate(@this, vrcPlayerApiPtr);
        }

        private static void OnAvatarInstantiated(IntPtr @this, IntPtr avatarPtr, IntPtr avatarDescriptorPtr, bool loaded)
        {
            onAvatarInstantiatedDelegate(@this, avatarPtr, avatarDescriptorPtr, loaded);

            try
            {
                if (loaded)
                {
                    GameObject avatar = new GameObject(avatarPtr);
                    VRC.SDKBase.VRC_AvatarDescriptor avatarDescriptor = new VRC.SDKBase.VRC_AvatarDescriptor(avatarDescriptorPtr);


                    if (avatar.transform.root.gameObject.name.Contains("[Local]")) _Instance.localPlayer = avatar;
                    _Instance.AddOrReplaceWithCleanup(
                        avatar.transform.root.GetComponentInChildren<VRCPlayer>().namePlate.prop_String_0,
                        new System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool>(
                            avatar,
                            avatar.transform.root.GetComponentInChildren<VRCPlayer>().prop_VRCPlayerApi_0.IsUserInVR(),
                            avatar.GetComponentsInChildren<DynamicBone>(),
                            avatar.GetComponentsInChildren<DynamicBoneCollider>(),
                            APIUser.IsFriendsWith(avatar.transform.root.GetComponentInChildren<Player>().prop_APIUser_0.id)));

                    MelonModLogger.Log(ConsoleColor.Blue, "New avatar loaded, added to avatar list");
                    MelonModLogger.Log(ConsoleColor.Green, $"Added {avatar.transform.root.GetComponentInChildren<VRCPlayer>().namePlate.prop_String_0}");
                }
            }
            catch (System.Exception ex)
            {
                MelonModLogger.LogError("An exception was thrown while working!\n" + ex.ToString() + "\nStack trace:\n" + ex.StackTrace);
            }
        }

        public void AddOrReplaceWithCleanup(string key, System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool> newValue)
        {
            if (!avatarsInScene.ContainsKey(key))
            {
                SaveOriginalColliderList(key, newValue.Item3);
                AddToPlayerDict(key, newValue);
            }
            else
            {
                DeleteOriginalColliders(key);
                SaveOriginalColliderList(key, newValue.Item3);
                DynamicBoneCollider[] oldColliders = avatarsInScene[key].Item4;
                RemovePlayerFromDict(key);
                AddToPlayerDict(key, newValue);
                RemoveBonesOfGameObjectInAllPlayers(oldColliders);
                MelonModLogger.Log(ConsoleColor.Blue, $"User {key} swapped avatar, system updated");
            }
            AddBonesOfGameObjectToAllPlayers(newValue);
        }

        private bool SelectBonesWithRules(KeyValuePair<string, System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool>> item)
        {
            bool valid = true;
            if (NDBConfig.onlyForMyBones) valid &= item.Value.Item1 == localPlayer;
            if (NDBConfig.onlyForMeAndFriends) valid &= item.Value.Item5 || (item.Value.Item1 == localPlayer);
            if (NDBConfig.disallowDesktoppers) valid &= item.Value.Item2 || (item.Value.Item1 == localPlayer);
            return valid;
        }

        private bool SelectCollidersWithRules(KeyValuePair<string, System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool>> item)
        {
            bool valid = true;
            if (NDBConfig.onlyForMeAndFriends) valid &= item.Value.Item5 || (item.Value.Item1 == localPlayer);
            if (NDBConfig.disallowDesktoppers) valid &= item.Value.Item2;
            return valid;
        }

        private void ApplyBoneSettings(DynamicBone bone)
        {
            bone.m_DistantDisable = NDBConfig.distanceDisable;
            bone.m_DistanceToObject = NDBConfig.distanceToDisable;
            bone.m_UpdateRate = NDBConfig.dynamicBoneUpdateRate;
            bone.m_ReferenceObject = localPlayer.transform;
        }

        private void AddAllCollidersToAllPlayers()
        {
            foreach (DynamicBone[] bones in avatarsInScene.Where((x) => SelectBonesWithRules(x)).Select((x) => x.Value.Item3))
            {
                foreach (DynamicBone db in bones)
                {
                    ApplyBoneSettings(db);
                }
                foreach (DynamicBoneCollider[] colliders in avatarsInScene.Where((x) => SelectCollidersWithRules(x)).Select((x) => x.Value.Item4))
                {
                    foreach (DynamicBone b in bones)
                    {
                        foreach (DynamicBoneCollider dbc in colliders)
                        {
                            AddColliderToDynamicBone(b, dbc);
                        }
                    }
                }
            }
        }

        private void AddBonesOfGameObjectToAllPlayers(System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool> player)
        {
            if (player.Item1 != localPlayer) return;
            if (NDBConfig.onlyForMeAndFriends)
            {
                if (!player.Item5) return;
            }
            if (NDBConfig.disallowDesktoppers)
            {
                if (!player.Item2) return;
            }

            foreach (DynamicBone[] dbs in avatarsInScene.Where((x) => SelectBonesWithRules(x)).Select((x) => x.Value.Item3))
            {
                foreach (DynamicBone db in dbs)
                {
                    ApplyBoneSettings(db);
                    foreach (DynamicBoneCollider dbc in player.Item4)
                    {
                        AddColliderToDynamicBone(db, dbc);
                    }
                }
            }
        }

        private void RemoveBonesOfGameObjectInAllPlayers(DynamicBoneCollider[] colliders)
        {
            foreach (DynamicBone[] dbs in avatarsInScene.Values.Select((x) => x.Item3))
            {
                foreach (DynamicBone db in dbs)
                {
                    foreach (DynamicBoneCollider dbc in colliders)
                    {
                        db.m_Colliders.Remove(dbc);
                    }
                }
            }
        }

        private void AddColliderToDynamicBone(DynamicBone bone, DynamicBoneCollider dbc)
        {
            if (!bone.m_Colliders.Contains(dbc)) bone.m_Colliders.Add(dbc);
        }

        private void AddColliderToBone(DynamicBone bone, DynamicBoneCollider collider)
        {
            if (NDBConfig.disallowInsideColliders && collider.m_Bound == DynamicBoneCollider.EnumNPublicSealedvaOuIn3vUnique.Inside)
            {
                return;
            }

            if (collider.m_Radius > NDBConfig.colliderSizeLimit || collider.m_Height > NDBConfig.colliderSizeLimit)
            {
                return;
            }

            bone.m_Colliders.Add(collider);
        }

        private void RestoreFromBackUp()
        {
            avatarsInScene = old_avatarsInScene;
            originalSettings = old_originalSettings;
            localPlayer = old_localPlayer;
        }


        public override void OnUpdate()
        {

            if (Input.GetKeyDown(KeyCode.F1))
            {
                ToggleState();
            }

            if (Input.GetKeyDown(KeyCode.F4))
            {
                MelonModLogger.Log(ConsoleColor.Red, "List of avatar in dict:");
                foreach (string name in avatarsInScene.Keys)
                {
                    MelonModLogger.Log(ConsoleColor.DarkGreen, name);
                }
            }

            if (Input.GetKeyDown(KeyCode.F5))
            {
                if (old_localPlayer != null && old_avatarsInScene != null && old_originalSettings != null)
                {
                    RestoreFromBackUp();
                }
            }
        }

        private void ToggleState()
        {
            enabled = !enabled;
            if (!enabled)
            {
                RestoreOriginalColliderList();
            }
            else AddAllCollidersToAllPlayers();
            MelonModLogger.Log(ConsoleColor.Green, $"NDBMod is now {((enabled == true) ? "enabled" : "disabled")}");
        }

        private void RemovePlayerFromDict(string name)
        {
            avatarsInScene.Remove(name);
        }

        private void AddToPlayerDict(string name, System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool> value)
        {
            avatarsInScene.Add(name, value);
        }

        private void DeleteOriginalColliders(string name)
        {
            originalSettings.Remove(name);
        }

        private void SaveOriginalColliderList(string name, DynamicBone[] bones)
        {
            if (!originalSettings.ContainsKey(name)) originalSettings.Add(name, bones);
            else originalSettings[name] = bones;
        }

        private void RestoreOriginalColliderList()
        {
            foreach (var player in avatarsInScene)
            {
                foreach (DynamicBone original in originalSettings[player.Key])
                {
                    for (int i = 0; i < player.Value.Item3.Length; i++)
                    {
                        player.Value.Item3[i] = original;
                    }
                }
            }
        }
    }
}
