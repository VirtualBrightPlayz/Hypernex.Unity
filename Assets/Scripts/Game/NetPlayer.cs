﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hypernex.CCK;
using Hypernex.Configuration;
using Hypernex.Networking.Messages;
using Hypernex.Player;
using Hypernex.Sandboxing;
using Hypernex.Tools;
using Hypernex.UI.Templates;
using HypernexSharp.API;
using HypernexSharp.API.APIResults;
using HypernexSharp.APIObjects;
using HypernexSharp.Socketing.SocketMessages;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hypernex.Game
{
    public class NetPlayer : MonoBehaviour
    {
        [HideInInspector] public string UserId;
        private GameInstance instance;
        private Scene scene;
        public User User;
        public AvatarCreator Avatar;
        public Transform MainCameraTransform;

        private string AvatarId;
        private SharedAvatarToken avatarFileToken;
        private AvatarMeta avatarMeta;
        private Builds avatarBuild;
        internal NameplateTemplate nameplateTemplate;
        private Transform MainCamera;
        private Transform MainCameraNametagAlign;

        public int interpolationFramesCount = 120;
        private int elapsedFrames;

        public float volume
        {
            get
            {
                if (ConfigManager.SelectedConfigUser == null)
                    return 1.0f;
                if (!ConfigManager.SelectedConfigUser.UserVolumes.ContainsKey(UserId))
                    return 1.0f;
                return ConfigManager.SelectedConfigUser.UserVolumes[UserId];
            }
        }
        private AudioClip voice;

        [HideInInspector] public List<string> LastPlayerTags = new();
        public Dictionary<string, object> LastExtraneousObjects = new();

        private void CreateNameplate()
        {
            GameObject np = Instantiate(DontDestroyMe.GetNotDestroyedObject("UXTemplates").transform.Find("Nameplate")
                .gameObject);
            SceneManager.MoveGameObjectToScene(np, instance.loadedScene);
            // ReSharper disable once Unity.InstantiateWithoutParent
            np.transform.SetParent(transform);
            np.gameObject.SetActive(true);
            nameplateTemplate = np.GetComponent<NameplateTemplate>();
            nameplateTemplate.Render(User);
        }

        private void OnUser(CallbackResult<GetUserResult> result)
        {
            if (!result.success)
            {
                APIPlayer.APIObject.GetUser(OnUser, UserId, isUserId: true);
                return;
            }

            QuickInvoke.InvokeActionOnMainThread(new Action(() =>
            {
                User = result.result.UserData;
                CreateNameplate();
            }));
        }

        private void OnAvatarDownload(Stream stream)
        {
            waitingForAvatarToken = false;
            if (stream == Stream.Null)
            {
                if (avatarFileToken == null)
                    APIPlayer.APIObject.GetFile(OnAvatarDownload, avatarMeta.OwnerId, avatarBuild.FileId);
                else
                    APIPlayer.APIObject.GetFile(OnAvatarDownload, avatarMeta.OwnerId, avatarBuild.FileId,
                        avatarFileToken.avatarToken);
                return;
            }

            QuickInvoke.InvokeActionOnMainThread(new Action(() =>
            {
                string path = Path.Combine(DownloadTools.DownloadsPath, $"{AvatarId}.hna");
                if (File.Exists(path))
                    File.Delete(path);
                using (FileStream fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                           FileShare.ReadWrite | FileShare.Delete))
                {
                    stream.CopyTo(fs);
                    StartCoroutine(AssetBundleTools.LoadAvatarFromFile(path, a =>
                    {
                        if (a == null)
                            return;
                        Avatar?.Dispose();
                        avatarUpdates.Clear();
                        Avatar = new AvatarCreator(this, a);
                        foreach (NexboxScript localAvatarScript in a.LocalAvatarScripts)
                            Avatar.localAvatarSandboxes.Add(new Sandbox(localAvatarScript, transform));
                        if (nameplateTemplate != null)
                            nameplateTemplate.transform.SetLocalPositionAndRotation(
                                new Vector3(0, transform.localScale.y + 0.9f, 0),
                                Quaternion.identity);
                    }));
                }
            }));
        }

        private void OnAvatar(CallbackResult<MetaCallback<AvatarMeta>> result)
        {
            if (!result.success)
            {
                APIPlayer.APIObject.GetAvatarMeta(OnAvatar, AvatarId);
                return;
            }

            if (result.result.Meta.Id == AvatarId)
                QuickInvoke.InvokeActionOnMainThread(new Action(() =>
                {
                    avatarMeta = result.result.Meta;
                    Builds b = null;
                    foreach (Builds metaBuild in result.result.Meta.Builds)
                        if (metaBuild.BuildPlatform == AssetBundleTools.Platform)
                            b = metaBuild;
                    if (b == null)
                        return;
                    avatarBuild = b;
                    if (avatarMeta.Publicity == AvatarPublicity.OwnerOnly)
                    {
                        if (SocketManager.SharedAvatarTokens.Count(x =>
                                x.avatarId == AvatarId && x.fromUserId == UserId) > 0)
                        {
                            SharedAvatarToken sharedAvatarToken = SocketManager.SharedAvatarTokens.First(x =>
                                x.avatarId == AvatarId && x.fromUserId == UserId);
                            avatarFileToken = sharedAvatarToken;
                        }
                        else
                        {
                            waitingForAvatarToken = true;
                            return;
                        }
                    }

                    if (avatarFileToken == null)
                        APIPlayer.APIObject.GetFile(OnAvatarDownload, result.result.Meta.OwnerId, b.FileId);
                    else
                        APIPlayer.APIObject.GetFile(OnAvatarDownload, result.result.Meta.OwnerId, b.FileId,
                            avatarFileToken.avatarToken);
                }));
        }

        private bool waitingForAvatarToken;

        public void Start()
        {
            GameObject g = new GameObject("VoicePosition");
            g.transform.SetParent(transform, false);
            SocketManager.OnAvatarToken += token =>
            {
                if (waitingForAvatarToken && token.fromUserId == UserId && token.avatarId == AvatarId)
                {
                    waitingForAvatarToken = false;
                    avatarFileToken = token;
                    APIPlayer.APIObject.GetFile(OnAvatarDownload, avatarMeta.OwnerId, avatarBuild.FileId,
                        avatarFileToken.avatarToken);
                }
            };
            GameObject co = new GameObject("Camera Offset");
            co.transform.parent = transform;
            MainCamera = new GameObject("Main Camera").transform;
            MainCamera.parent = co.transform;
            MainCameraNametagAlign = new GameObject("nametagalign_" + Guid.NewGuid()).transform;
            MainCameraNametagAlign.parent = MainCamera.transform;
            MainCameraNametagAlign.localPosition = new Vector3(MainCameraNametagAlign.localPosition.x,
                MainCameraNametagAlign.localPosition.y + 1.6f, MainCameraNametagAlign.localPosition.z);
        }

        public void Update()
        {
            float interpolationRatio = (float)elapsedFrames / interpolationFramesCount;
            if (instance != null && User == null)
            {
                foreach (User instanceConnectedUser in instance.ConnectedUsers)
                {
                    if (instanceConnectedUser.Id == UserId)
                        User = instanceConnectedUser;
                }
            }
            if (nameplateTemplate != null)
            {
                /*Transform bone = Avatar.GetBoneFromHumanoid(HumanBodyBones.Head);
                if (bone != null)
                {
                    Vector3 newPos = bone.position;
                    newPos.y += 0.9f;
                    nameplateTemplate.transform.position = newPos;
                }*/
                if (Avatar != null && Avatar.nametagAlign != null)
                    nameplateTemplate.FollowTransform = Avatar.nametagAlign;
                else
                    nameplateTemplate.FollowTransform = MainCameraNametagAlign;
                nameplateTemplate.transform.localScale = new Vector3(0.003f, 0.003f, 0.003f);
            }
            else if (nameplateTemplate != null && (Avatar == null || Avatar.nametagAlign == null))
                nameplateTemplate.FollowTransform = MainCameraTransform;
            foreach (string key in new List<string>(avatarUpdates.Keys))
                UpdatePlayerObjectUpdate(key, interpolationRatio);
            elapsedFrames = (elapsedFrames + 1) % (interpolationFramesCount + 1);
        }

        private Dictionary<string, PlayerObjectUpdateHolder> avatarUpdates = new();

        private void UpdatePlayerObjectUpdate(string key, float interpolationRatio)
        {
            Vector3 position = NetworkConversionTools.float3ToVector3(avatarUpdates[key].Object.Object.Position);
            Quaternion rotation = Quaternion.Euler(new Vector3(avatarUpdates[key].Object.Object.Rotation.x,
                avatarUpdates[key].Object.Object.Rotation.y, avatarUpdates[key].Object.Object.Rotation.z));
            if (string.IsNullOrEmpty(avatarUpdates[key].Object.Object.ObjectLocation))
            {
                avatarUpdates[key].ExpectedPosition =
                    Vector3.Lerp(avatarUpdates[key].t.position, position, interpolationRatio);
                avatarUpdates[key].ExpectedRotation =
                    Quaternion.Lerp(avatarUpdates[key].t.rotation, rotation, interpolationRatio);
            }
            else
            {
                avatarUpdates[key].ExpectedPosition =
                    Vector3.Lerp(avatarUpdates[key].t.localPosition, position, interpolationRatio);
                avatarUpdates[key].ExpectedRotation =
                    Quaternion.Lerp(avatarUpdates[key].t.localRotation, rotation, interpolationRatio);
            }
            avatarUpdates[key].ExpectedSize =
                Vector3.Lerp(avatarUpdates[key].t.localScale,
                    NetworkConversionTools.float3ToVector3(avatarUpdates[key].Object.Object.Size), interpolationRatio);
        }

        private void UpdatePlayerUpdate(string path, PlayerObjectUpdate playerObjectUpdate, Transform p = null)
        {
            if (!avatarUpdates.ContainsKey(path))
            {
                if (p == null)
                    return;
                avatarUpdates.Add(path, new PlayerObjectUpdateHolder
                {
                    t = p,
                    Object = playerObjectUpdate
                });
            }
            avatarUpdates[path].Object = playerObjectUpdate;
        }

        private void LateUpdate()
        {
            /*float interpolationRatio = Time.frameCount / Time.time;
            interpolationRatio *= interpolationAmount;
            foreach (KeyValuePair<string,PlayerObjectUpdate> playerObjectUpdate in avatarUpdates)
            {
                Transform target = transform.Find(playerObjectUpdate.Key);
                NetworkedObject networkedObject = playerObjectUpdate.Value.Object;
                if (target != null)
                {
                    Vector3 position = NetworkConversionTools.float3ToVector3(networkedObject.Position);
                    //Quaternion rotation = NetworkConversionTools.float4ToQuaternion(networkedObject.Rotation);
                    //Vector3 localSize = NetworkConversionTools.float3ToVector3(networkedObject.Size);
                    if (string.IsNullOrEmpty(networkedObject.ObjectLocation))
                    {
                        /*target.position = position;
                        target.rotation = Quaternion.Euler(new Vector3(networkedObject.Rotation.x, networkedObject.Rotation.y,
                            networkedObject.Rotation.z));#1#
                        target.position = Vector3.Lerp(target.position, position, interpolationRatio);
                        target.rotation = Quaternion.Lerp(target.rotation, Quaternion.Euler(new Vector3(
                            networkedObject.Rotation.x, networkedObject.Rotation.y,
                            networkedObject.Rotation.z)), interpolationRatio);
                    }
                    else
                    {
                        /*target.localPosition = position;
                        target.localRotation = Quaternion.Euler(new Vector3(networkedObject.Rotation.x, networkedObject.Rotation.y,
                            networkedObject.Rotation.z));#1#
                        target.localPosition = Vector3.Lerp(target.localPosition, position, interpolationRatio);
                        target.localRotation = Quaternion.Lerp(target.localRotation, Quaternion.Euler(new Vector3(
                            networkedObject.Rotation.x, networkedObject.Rotation.y,
                            networkedObject.Rotation.z)), interpolationRatio);

                    }
                    target.localScale = Vector3.Lerp(target.localScale,
                        NetworkConversionTools.float3ToVector3(networkedObject.Size), interpolationRatio);
                }
            }*/
            //float interpolationRatio = (float)elapsedFrames / interpolationFramesCount;
            foreach (string key in new List<string>(avatarUpdates.Keys))
            {
                PlayerObjectUpdateHolder playerObjectUpdateHolder = avatarUpdates[key];
                //UpdatePlayerObjectUpdate(key, interpolationRatio);
                if (string.IsNullOrEmpty(playerObjectUpdateHolder.Object.Object.ObjectLocation))
                {
                    playerObjectUpdateHolder.t.position = playerObjectUpdateHolder.ExpectedPosition;
                    playerObjectUpdateHolder.t.rotation = playerObjectUpdateHolder.ExpectedRotation;
                }
                else
                {
                    playerObjectUpdateHolder.t.localPosition = playerObjectUpdateHolder.ExpectedPosition;
                    playerObjectUpdateHolder.t.localRotation = playerObjectUpdateHolder.ExpectedRotation;
                }
                playerObjectUpdateHolder.t.localScale = playerObjectUpdateHolder.ExpectedSize;
            }
            //elapsedFrames = (elapsedFrames + 1) % (interpolationFramesCount + 1);
        }

        public void Init(string userid, GameInstance gameInstance)
        {
            UserId = userid;
            scene = gameInstance.loadedScene;
            instance = gameInstance;
            APIPlayer.APIObject.GetUser(OnUser, UserId, isUserId: true);
        }

        public void VoiceUpdate(PlayerVoice playerVoice)
        {
            if (Avatar != null && Avatar.Avatar.gameObject.scene == scene)
                Avatar.opusHandler.DecodeFromVoice(playerVoice);
        }

        public void NetworkUpdate(PlayerUpdate playerUpdate)
        {
            if (!string.IsNullOrEmpty(playerUpdate.AvatarId) && (string.IsNullOrEmpty(AvatarId) ||
                playerUpdate.AvatarId != AvatarId))
            {
                AvatarId = playerUpdate.AvatarId;
                APIPlayer.APIObject.GetAvatarMeta(OnAvatar, AvatarId);
            }
            if (Avatar != null && Avatar.Avatar.transform.parent == transform)
            {
                /*foreach (KeyValuePair<string,float> weightedObject in playerUpdate.WeightedObjects)
                    Avatar.HandleNetParameter(weightedObject.Key, weightedObject.Value);*/
                LastPlayerTags = new List<string>(playerUpdate.PlayerAssignedTags);
                LastExtraneousObjects = new Dictionary<string, object>(playerUpdate.ExtraneousData);
                Avatar.audioSource.volume = playerUpdate.IsSpeaking ? volume : 0f;
            }
        }

        public void WeightedObject(WeightedObjectUpdate weightedObjectUpdate) =>
            Avatar?.HandleNetParameter(weightedObjectUpdate);

        /*public void NetworkObjectUpdate(PlayerObjectUpdate playerObjectUpdate)
        {
            float interpolationRatio = interpolationFramesCount * Time.deltaTime;
            if (Avatar != null && Avatar.Avatar.transform.parent == transform)
            {
                NetworkedObject networkedObject = playerObjectUpdate.Object;
                Transform target;
                if (string.IsNullOrEmpty(networkedObject.ObjectLocation))
                    target = transform;
                else
                    target = transform.Find(networkedObject.ObjectLocation);
                if (target != null)
                {
                    Vector3 position = NetworkConversionTools.float3ToVector3(networkedObject.Position);
                    Quaternion rotation = NetworkConversionTools.float4ToQuaternion(networkedObject.Rotation);
                    Vector3 localSize = NetworkConversionTools.float3ToVector3(networkedObject.Size);
                    if (string.IsNullOrEmpty(networkedObject.ObjectLocation))
                    {
                        /*target.position = position;
                        target.rotation = Quaternion.Euler(new Vector3(networkedObject.Rotation.x, networkedObject.Rotation.y,
                            networkedObject.Rotation.z));#1#
                        target.position = Vector3.Lerp(target.position, position, interpolationRatio);
                        target.rotation = Quaternion.Lerp(target.rotation, Quaternion.Euler(new Vector3(
                            networkedObject.Rotation.x, networkedObject.Rotation.y,
                            networkedObject.Rotation.z)), interpolationRatio);
                    }
                    else
                    {
                        /*target.localPosition = position;
                        target.localRotation = Quaternion.Euler(new Vector3(networkedObject.Rotation.x, networkedObject.Rotation.y,
                            networkedObject.Rotation.z));#1#
                        target.localPosition = Vector3.Lerp(target.localPosition, position, interpolationRatio);
                        target.localRotation = Quaternion.Lerp(target.localRotation, Quaternion.Euler(new Vector3(
                            networkedObject.Rotation.x, networkedObject.Rotation.y,
                            networkedObject.Rotation.z)), interpolationRatio);

                    }
                    //target.localScale = localSize;
                }
            }
        }*/

        public void NetworkObjectUpdate(PlayerObjectUpdate playerObjectUpdate)
        {
            if (string.IsNullOrEmpty(playerObjectUpdate.Object.ObjectLocation))
                playerObjectUpdate.Object.ObjectLocation = "";
            if (!avatarUpdates.ContainsKey(playerObjectUpdate.Object.ObjectLocation))
            {
                Transform p = transform.Find(playerObjectUpdate.Object.ObjectLocation);
                if (p == null)
                    return;
                UpdatePlayerUpdate(playerObjectUpdate.Object.ObjectLocation, playerObjectUpdate, p);
            }
            else
                UpdatePlayerUpdate(playerObjectUpdate.Object.ObjectLocation, playerObjectUpdate);
        }
        
        private class PlayerObjectUpdateHolder
        {
            public Transform t;
            public PlayerObjectUpdate Object;
            public Vector3 ExpectedPosition;
            public Quaternion ExpectedRotation;
            public Vector3 ExpectedSize;
        }
    }
}