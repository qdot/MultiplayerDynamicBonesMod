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
using System.Reflection;
using System.Runtime.InteropServices;
using VRCSDK2;
using Il2CppSystem;
using IntPtr = System.IntPtr;
using ConsoleColor = System.ConsoleColor;
using UnhollowerRuntimeLib;
using IL2CPP = UnhollowerBaseLib.IL2CPP;
using UnityEngine.UI;
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
            public static bool enableModUI;
            public static int toggleButtonX;
            public static int toggleButtonY;
            public static bool enableBoundsCheck;
            public static float visiblityUpdateRate;
        }

        
        struct OriginalBoneInformation
        {
            public float updateRate;
            public float distanceToDisable;
            public List<DynamicBoneCollider> colliders;
            public DynamicBone referenceToOriginal;
            public bool distantDisable;
        }

        private static NDB _Instance;

        private Dictionary<string, System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool>> avatarsInScene;
        private Dictionary<string, List<OriginalBoneInformation>> originalSettings;
        public Dictionary<string, System.Tuple<Renderer, DynamicBone[]>> avatarRenderers;
        private GameObject localPlayer;
        //private Transform localPlayerReferenceTransform;
        private Transform toggleButton;
        private Transform onlyFriendsButton; //OnlyFans haha
        private bool enabled = true;
        
        private float nextUpdateVisibility = 0;
        private const float visiblityUpdateRate = 1f;

        private static AvatarInstantiatedDelegate onAvatarInstantiatedDelegate;
        private static PlayerLeftDelegate onPlayerLeftDelegate;
        private static JoinedRoom onJoinedRoom;

        private static void Hook(IntPtr target, IntPtr detour)
        {
            typeof(Imports).GetMethod("Hook", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { target, detour });
        }

        private Transform AddMenuButton(string butName, string butText, int butX, int butY, System.Action butAction)
        {
            Transform quickMenu = QuickMenu.prop_QuickMenu_0.transform;
            RecursiveHierarchyDump(quickMenu, 0);

            // clone of a standard button
            Transform butTransform = UnityEngine.Object.Instantiate(quickMenu.Find("CameraMenu/BackButton").gameObject).transform;
            if (butTransform == null) MelonModLogger.Log(ConsoleColor.Red, "Couldn't add button for dynamic bones");
            butTransform.name = butName;

            // set button's parent to quick menu
            butTransform.SetParent(quickMenu.Find("ShortcutMenu"), false);

            // set button's text
            butTransform.GetComponentInChildren<Text>().text = butText;

            // set position of new button based on existing menu buttons
            float buttonWidth = quickMenu.Find("UserInteractMenu/ForceLogoutButton").localPosition.x - quickMenu.Find("UserInteractMenu/BanButton").localPosition.x;
            float buttonHeight = quickMenu.Find("UserInteractMenu/ForceLogoutButton").localPosition.x - quickMenu.Find("UserInteractMenu/BanButton").localPosition.x;
            butTransform.localPosition = new Vector3(butTransform.localPosition.x + buttonWidth * butX, butTransform.localPosition.y + buttonHeight * butY, butTransform.localPosition.z);

            // Make it so the button does what we want
            butTransform.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
            butTransform.GetComponent<Button>().onClick.AddListener(butAction);

            // enable it just in case
            butTransform.gameObject.SetActive(true);

            return butTransform;
        }

        private void AddButtons()
        {
            toggleButton = this.AddMenuButton("NDBToggle", $"Press to {((enabled) ? "disable" : "enable")} Dynamic Bones mod", NDBConfig.toggleButtonX, NDBConfig.toggleButtonY, new System.Action(() =>
            {
                try
                {
                    ToggleState();
                }
                catch (System.Exception ex) { MelonModLogger.Log(ConsoleColor.Red, ex.ToString()); }
            }));
            /*
            onlyFriendsButton = this.AddMenuButton("NDBFriends", $"Press to allow {((NDBConfig.onlyForMeAndFriends) ? "everyone" : "only friends")} to have multiplayer dynamic bones.", 2, 1, new System.Action(() =>
            {
                try
                {
                    NDBConfig.onlyForMeAndFriends = !NDBConfig.onlyForMeAndFriends;
                    if (enabled)
                    {
                        ToggleState();
                        ToggleState();
                    }
                    onlyFriendsButton.GetComponentInChildren<Text>().text = $"Press to allow {((NDBConfig.onlyForMeAndFriends) ? "everyone" : "only friends")} to have multiplayer dynamic bones.";
                }
                catch (System.Exception ex) { MelonModLogger.Log(ConsoleColor.Red, ex.ToString()); }
            }));
            */
        }

        public override void VRChat_OnUiManagerInit()
        {
            if (NDBConfig.enableModUI) AddButtons();
        }

        private delegate void AvatarInstantiatedDelegate(IntPtr @this, IntPtr avatarPtr, IntPtr avatarDescriptorPtr, bool loaded);
        private delegate void PlayerLeftDelegate(IntPtr @this, IntPtr playerPtr);
        private delegate void JoinedRoom(IntPtr @this);

        public unsafe override void OnApplicationStart()
        {
            



            _Instance = this;

            ModPrefs.RegisterCategory("NDB", "Multiplayer Dynamic Bones");
            ModPrefs.RegisterPrefBool("NDB", "EnabledByDefault", true, "Enabled by default");
            ModPrefs.RegisterPrefBool("NDB", "OnlyMe", false, "Only I can interact with other bones");
            ModPrefs.RegisterPrefBool("NDB", "OnlyFriends", false, "Only me and friends can interact with my and friend's bones");
            ModPrefs.RegisterPrefBool("NDB", "DisallowDesktoppers", false, "Desktoppers's colliders and bones won't be multiplayer'd");
            ModPrefs.RegisterPrefBool("NDB", "DistanceDisable", true, "Disable bones if beyond a distance");
            ModPrefs.RegisterPrefFloat("NDB", "DistanceToDisable", 4f, "Distance limit");
            ModPrefs.RegisterPrefBool("NDB", "DisallowInsideColliders", true, "Disallow inside colliders");
            ModPrefs.RegisterPrefFloat("NDB", "ColliderSizeLimit", 1f, "Collider size limit");
            ModPrefs.RegisterPrefInt("NDB", "DynamicBoneUpdateRate", 60, "Dynamic bone update rate");
            ModPrefs.RegisterPrefBool("NDB", "EnableModUI", true, "Enables mod UI");
            ModPrefs.RegisterPrefInt("NDB", "ButtonPositionX", 1, "X position of button");
            ModPrefs.RegisterPrefInt("NDB", "ButtonPositionY", 1, "Y position of button");
            ModPrefs.RegisterPrefBool("NDB", "EnableJustIfVisible", true, "Enable dynamic bones just if they are visible");
            ModPrefs.RegisterPrefFloat("NDB", "VisibilityUpdateRate", 1f, "Visibility update rate (seconds)");

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
            NDBConfig.enableModUI = ModPrefs.GetBool("NDB", "EnableModUI");
            NDBConfig.toggleButtonX = ModPrefs.GetInt("NDB", "ButtonPositionX");
            NDBConfig.toggleButtonY = ModPrefs.GetInt("NDB", "ButtonPositionY");
            NDBConfig.enableBoundsCheck = ModPrefs.GetBool("NDB", "EnableJustIfVisible");
            NDBConfig.visiblityUpdateRate = ModPrefs.GetFloat("NDB", "VisibilityUpdateRate");


            enabled = NDBConfig.enabledByDefault;
            IntPtr funcToHook = (IntPtr)typeof(VRCAvatarManager.MulticastDelegateNPublicSealedVoGaVRBoObVoInBeInGaUnique).GetField("NativeMethodInfoPtr_Invoke_Public_Virtual_New_Void_GameObject_VRC_AvatarDescriptor_Boolean_0", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);

            Hook(funcToHook, new System.Action<IntPtr, IntPtr, IntPtr, bool>(OnAvatarInstantiated).Method.MethodHandle.GetFunctionPointer());
            onAvatarInstantiatedDelegate = Marshal.GetDelegateForFunctionPointer<AvatarInstantiatedDelegate>(*(IntPtr*)funcToHook);
            MelonModLogger.Log(ConsoleColor.Blue, $"Hooked OnAvatarInstantiated? {((onAvatarInstantiatedDelegate != null) ? "Yes!" : "No: critical error!!")}");

            funcToHook = (IntPtr)typeof(NetworkManager).GetField("NativeMethodInfoPtr_Method_Public_Void_Player_0", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            Hook(funcToHook, new System.Action<IntPtr, IntPtr>(OnPlayerLeft).Method.MethodHandle.GetFunctionPointer());
            onPlayerLeftDelegate = Marshal.GetDelegateForFunctionPointer<PlayerLeftDelegate>(*(IntPtr*)funcToHook);
            MelonModLogger.Log(ConsoleColor.Blue, $"Hooked OnPlayerLeft? {((onPlayerLeftDelegate != null) ? "Yes!" : "No: critical error!!")}");


            funcToHook = (IntPtr)typeof(NetworkManager).GetField("NativeMethodInfoPtr_OnJoinedRoom_Public_Virtual_Final_New_Void_2", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            Hook(funcToHook, new System.Action<IntPtr>(OnJoinedRoom).Method.MethodHandle.GetFunctionPointer());
            onJoinedRoom = Marshal.GetDelegateForFunctionPointer<JoinedRoom>(*(IntPtr*)funcToHook);
            MelonModLogger.Log(ConsoleColor.Blue, $"Hooked OnJoinedRoom? {((onJoinedRoom != null) ? "Yes!" : "No: critical error!!")}");

            MelonModLogger.Log(ConsoleColor.Green, $"NDBMod is {((enabled == true) ? "enabled" : "disabled")}");

            if (onPlayerLeftDelegate == null || onAvatarInstantiatedDelegate == null || onJoinedRoom == null)
            {
                this.enabled = false;
                MelonModLogger.Log(ConsoleColor.Red, "Multiplayer Dynamic Bones mod suffered a critical error! Please remove from the Mods folder to avoid game crashes! \nContact me for support.");
            }
        }

        private static void OnJoinedRoom(IntPtr @this)
        {
            _Instance.originalSettings = new Dictionary<string, List<OriginalBoneInformation>>();
            _Instance.avatarsInScene = new Dictionary<string, System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool>>();
            _Instance.avatarRenderers = new Dictionary<string, System.Tuple<Renderer, DynamicBone[]>>();
            _Instance.localPlayer = null;

            onJoinedRoom(@this);
            MelonModLogger.Log(ConsoleColor.Blue, "New scene loaded; reset");
        }

        private static void OnPlayerLeft(IntPtr @this, IntPtr playerPtr)
        {
            Player player = new Player(playerPtr);

            if (!_Instance.avatarsInScene.ContainsKey(player.field_Internal_VRCPlayer_0.namePlate.prop_String_0) && !_Instance.originalSettings.ContainsKey(player.field_Internal_VRCPlayer_0.namePlate.prop_String_0))
            {
                onPlayerLeftDelegate(@this, playerPtr);
                return;
                
            }

            _Instance.RemoveBonesOfGameObjectInAllPlayers(_Instance.avatarsInScene[player.field_Internal_VRCPlayer_0.namePlate.prop_String_0].Item4);
            _Instance.DeleteOriginalColliders(player.field_Internal_VRCPlayer_0.namePlate.prop_String_0);
            _Instance.RemovePlayerFromDict(player.field_Internal_VRCPlayer_0.namePlate.prop_String_0);
            _Instance.RemoveDynamicBonesFromVisibilityList(player.field_Internal_VRCPlayer_0.namePlate.prop_String_0);
            MelonModLogger.Log(ConsoleColor.Blue, $"Player {player.field_Internal_VRCPlayer_0.namePlate.prop_String_0} left the room so all his dynamic bones info was deleted");
            onPlayerLeftDelegate(@this, playerPtr);
        }

        private static void RecursiveHierarchyDump(Transform child, int c)
        {
            StringBuilder offs = new StringBuilder();
            for (int i = 0; i < c; i++) offs.Append('-');
            offs.Append(child.name);
            offs.Append("  Components: ");
            child.GetComponents<Component>().Do((b) => offs.Append(b.ToString() + " | "));
            MelonModLogger.Log(ConsoleColor.White, offs.ToString());
            for (int x = 0; x < child.childCount; x++)
            {
                RecursiveHierarchyDump(child.GetChild(x), c + 1);
                
            }
        }

        //private static bool hasDumpedIt = false;
        private static void OnAvatarInstantiated(IntPtr @this, IntPtr avatarPtr, IntPtr avatarDescriptorPtr, bool loaded)
        {
            onAvatarInstantiatedDelegate(@this, avatarPtr, avatarDescriptorPtr, loaded);

            try
            {
                if (loaded)
                {
                    GameObject avatar = new GameObject(avatarPtr);
                    //VRC.SDKBase.VRC_AvatarDescriptor avatarDescriptor = new VRC.SDKBase.VRC_AvatarDescriptor(avatarDescriptorPtr);


                    if (avatar.transform.root.gameObject.name.Contains("[Local]"))
                    {
                        _Instance.localPlayer = avatar;
                    }

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
                MelonModLogger.LogError("An exception was thrown while working!\n" + ex.ToString());
            }
        }

        public void AddOrReplaceWithCleanup(string key, System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool> newValue)
        {
            foreach(DynamicBoneCollider col in newValue.Item4)
            {
                if (NDBConfig.disallowInsideColliders && col.m_Bound == DynamicBoneCollider.EnumNPublicSealedvaOuIn3vUnique.Inside)
                {
                    newValue.Item3.Do((b) => b.m_Colliders.Remove(col));
                    MelonModLogger.Log(ConsoleColor.Yellow, $"Removing bone {col.transform.name} because settings disallow inside colliders");
                    GameObject.Destroy(col);
                }
            }

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
                RemoveDynamicBonesFromVisibilityList(key);
                MelonModLogger.Log(ConsoleColor.Blue, $"User {key} swapped avatar, system updated");
            }
            if (enabled) AddBonesOfGameObjectToAllPlayers(newValue);
             AddDynamicBonesToVisibilityList(key, newValue.Item3, newValue.Item1.GetComponentInChildren<SkinnedMeshRenderer>());
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
            bone.m_ReferenceObject = localPlayer?.transform ?? bone.m_ReferenceObject;// (localPlayer?.transform ?? localPlayerReferenceTransform) ?? bone.m_ReferenceObject;
        }

        private void AddAllCollidersToAllPlayers()
        {
            foreach(System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool> player in avatarsInScene.Values)
            {
                AddBonesOfGameObjectToAllPlayers(player);
            }
        }

        private void AddBonesOfGameObjectToAllPlayers(System.Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool> player)
        {
            if (player.Item1 != localPlayer)
            {
                if (NDBConfig.onlyForMeAndFriends)
                {
                    if (!player.Item5)
                    {
                        MelonModLogger.Log(ConsoleColor.DarkYellow, $"Not adding bones of player {avatarsInScene.First((x) => x.Value.Item1 == player.Item1).Key} because settings only allow friends");
                        return;
                    }
                }
                if (NDBConfig.disallowDesktoppers)
                {
                    MelonModLogger.Log(ConsoleColor.DarkYellow, $"Not adding bones of player {avatarsInScene.First((x) => x.Value.Item1 == player.Item1).Key} because settings disallow desktopper");
                    if (!player.Item2) return;
                }
            }

            foreach (DynamicBone db in player.Item3)
            {
                if (db == null) continue;
                ApplyBoneSettings(db);
            }

            foreach (DynamicBone[] playersColliders in avatarsInScene.Where((x) => SelectBonesWithRules(x) && x.Value.Item1 != player.Item1).Select((x) => x.Value.Item3))
            {
                foreach (DynamicBone playerBone in playersColliders)
                {
                    foreach (DynamicBoneCollider playerCollider in player.Item4)
                    {
                        AddColliderToBone(playerBone, playerCollider);
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
#if DEBUG
            MelonModLogger.Log(ConsoleColor.Cyan, $"Adding {bone.m_Root.name} to {dbc.gameObject.name}");
#endif
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

            AddColliderToDynamicBone(bone, collider);
        }

        private void AddDynamicBonesToVisibilityList(string player, DynamicBone[] dynamicBones, Renderer renderer)
        {
            avatarRenderers.Add(player, new System.Tuple<Renderer, DynamicBone[]>(renderer, dynamicBones));
        }

        private void RemoveDynamicBonesFromVisibilityList(string player)
        {
            avatarRenderers.Remove(player);
        }



        public override void OnUpdate()
        {
            if (avatarRenderers.Count != 0 && NDBConfig.enableBoundsCheck) EnableIfVisible();

            if (Input.GetKeyDown(KeyCode.F8))
            {
                MelonModLogger.Log(ConsoleColor.DarkMagenta, "My bones have the following colliders attached:");
                avatarsInScene.Values.First((tup) => tup.Item1 == localPlayer).Item3.DoIf((bone) => bone != null, (bone) =>
                {
                    bone.m_Colliders.ToArray().Do((dbc) =>
                    {
                        try
                        {
                            MelonModLogger.Log(ConsoleColor.DarkMagenta, $"Bone {bone?.m_Root.name ?? "null"} has {dbc?.gameObject.name ?? "null"}");
                        }
                        catch (System.Exception ex) { };
                    });
                });

                MelonModLogger.Log(ConsoleColor.DarkMagenta, $"There are {avatarsInScene.Values.Aggregate(0, (acc, tup) => acc += tup.Item3.Length)} Dynamic Bones in scene");
                MelonModLogger.Log(ConsoleColor.DarkMagenta, $"There are {avatarsInScene.Values.Aggregate(0, (acc, tup) => acc += tup.Item4.Length)} Dynamic Bones Colliders in scene");

            }

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

        }

        private void EnableIfVisible()
        {
            if (nextUpdateVisibility < Time.time)
            {
                foreach (System.Tuple<Renderer, DynamicBone[]> go in avatarRenderers.Values)
                {
                    if (go.Item1 == null) continue;
                    bool visible = go.Item1.isVisible;
                    foreach (DynamicBone db in go.Item2)
                    {
                        //if (db.enabled != visible) MelonModLogger.Log(ConsoleColor.DarkBlue, $"{db.gameObject.name} is now {((visible) ? "enabled" : "disabled")}");
                        db.enabled = visible;
                    }
                }
                nextUpdateVisibility = Time.time + NDBConfig.visiblityUpdateRate;
            }
        }

        private void ToggleState()
        {
            enabled = !enabled;
            MelonModLogger.Log(ConsoleColor.Green, $"NDBMod is now {((enabled == true) ? "enabled" : "disabled")}");
            if (!enabled)
            {
                RestoreOriginalColliderList();
            }
            else AddAllCollidersToAllPlayers();

            toggleButton.GetComponentInChildren<Text>().text = $"Press to {((enabled) ? "disable" : "enable")} Dynamic Bones mod";
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
            if (originalSettings.ContainsKey(name)) originalSettings.Remove(name);
            List<OriginalBoneInformation> ogInfo = new List<OriginalBoneInformation>(bones.Length);
            foreach (DynamicBone b in bones)
            {
                bones.Select((bone) =>
                {
                    return new OriginalBoneInformation() { distanceToDisable = bone.m_DistanceToObject, updateRate = bone.m_UpdateRate, distantDisable = bone.m_DistantDisable, colliders = new List<DynamicBoneCollider>(bone.m_Colliders.ToArrayExtension()), referenceToOriginal = bone };
                }).Do((info) => ogInfo.Add(info));
            }
            originalSettings.Add(name, ogInfo);
            MelonModLogger.Log(ConsoleColor.DarkGreen, $"Saved original dynamic bone info of player {name}");
        }

        private void RestoreOriginalColliderList()
        {
            foreach (KeyValuePair<string, Tuple<GameObject, bool, DynamicBone[], DynamicBoneCollider[], bool>> player in avatarsInScene)
            {
                MelonModLogger.Log(ConsoleColor.DarkBlue, $"Restoring original settings for player {player.Key}");
                foreach (DynamicBone db in player.Value.Item3)
                {
                    if (originalSettings.TryGetValue(player.Key, out List<OriginalBoneInformation> origList))
                    {
                        origList.DoIf((x) => ReferenceEquals(x, db), (origData) =>
                        {
                            db.m_Colliders.Clear();
                            origData.colliders.ForEach((dbc) => db.m_Colliders.Add(dbc));
                            db.m_DistanceToObject = origData.distanceToDisable;
                            db.m_UpdateRate = origData.updateRate;
                            db.m_DistantDisable = origData.distantDisable;
                        });
                    }
                    else
                    {
                        MelonModLogger.Log(ConsoleColor.DarkYellow, $"Warning: could not find original dynamic bone info for {player.Key}'s bone {db.gameObject.name} . This means his bones won't be disabled!");
                    }
                }
            }
        }
    }

    public static class ListExtensions
    {
        public static T[] ToArrayExtension<T>(this Il2CppSystem.Collections.Generic.List<T> list)
        {
            T[] arr = new T[list.Count];
            for (int x = 0; x < list.Count; x++)
            {
                arr[x] = list[x];
            }
            return arr;
        }
    }
}
