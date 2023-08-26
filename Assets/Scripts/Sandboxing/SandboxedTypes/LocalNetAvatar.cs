﻿using System;
using System.Collections.Generic;
using Hypernex.Game;
using Hypernex.Tools;
using HypernexSharp.APIObjects;
using UnityEngine;

namespace Hypernex.Sandboxing.SandboxedTypes
{
    public class LocalNetAvatar
    {
        private GameInstance gameInstance;

        public LocalNetAvatar()
        {
            throw new Exception("Cannot instantiate LocalNetAvatar!");
        }
        internal LocalNetAvatar(GameInstance instance) => gameInstance = instance;
        
        public static string[] ActiveUserIds
        {
            get
            {
                if (GameInstance.FocusedInstance == null)
                    return Array.Empty<string>();
                List<User> l = GameInstance.FocusedInstance.ConnectedUsers;
                List<string> s = new List<string>();
                foreach (User user in l)
                    s.Add(user.Id);
                return s.ToArray();
            }
        }
        
        private NetPlayer GetNetPlayer(string userid)
        {
            if (gameInstance == null)
                return null;
            return PlayerManagement.GetNetPlayer(gameInstance, userid);
        }

        public ReadonlyItem GetAvatarObject(string userid, HumanBodyBones humanBodyBones)
        {
            NetPlayer netPlayer = GetNetPlayer(userid);
            if (netPlayer == null)
                return null;
            if (netPlayer.Avatar == null || netPlayer.Avatar == null)
                return null;
            Transform bone = netPlayer.Avatar.GetBoneFromHumanoid(humanBodyBones);
            if (bone == null)
                return null;
            return new ReadonlyItem(bone);
        }

        public ReadonlyItem GetAvatarObjectByPath(string userid, string path)
        {
            NetPlayer netPlayer = GetNetPlayer(userid);
            if (netPlayer == null)
                return null;
            if (netPlayer.Avatar == null)
                return null;
            Transform bone = netPlayer.Avatar.Avatar.transform.Find(path);
            if (bone == null)
                return null;
            return new ReadonlyItem(bone);
        }
        
        public static bool IsAvatarItem(Item item) =>
            AnimationUtility.GetRootOfChild(item.t).gameObject.GetComponent<NetPlayer>() != null;
        
        public static bool IsAvatarItem(ReadonlyItem item) =>
            AnimationUtility.GetRootOfChild(item.item.t).gameObject.GetComponent<NetPlayer>() != null;
        
        public ReadonlyItem[] GetAllChildrenInAvatar(string userid)
        {
            NetPlayer netPlayer = GetNetPlayer(userid);
            if (netPlayer == null)
                return null;
            if (netPlayer.Avatar == null)
                return null;
            List<ReadonlyItem> items = new List<ReadonlyItem>();
            foreach (Transform transform in netPlayer.Avatar.Avatar.GetComponentsInChildren<Transform>())
                items.Add(new ReadonlyItem(transform));
            return items.ToArray();
        }
        
        public string[] GetSelfAssignedTags(string userid)
        {
            NetPlayer netPlayer = GetNetPlayer(userid);
            if (netPlayer == null)
                return null;
            return netPlayer.LastPlayerTags.ToArray();
        }

        public object GetExtraneousObject(string userid, string key)
        {
            NetPlayer netPlayer = GetNetPlayer(userid);
            if (netPlayer == null)
                return null;
            if (netPlayer.LastExtraneousObjects.ContainsKey(key))
                return netPlayer.LastExtraneousObjects[key];
            return null;
        }

        public object GetParameterValue(string userid, string parameterName)
        {
            NetPlayer netPlayer = GetNetPlayer(userid);
            if (netPlayer == null)
                return null;
            if (netPlayer.Avatar == null)
                return null;
            return netPlayer.Avatar.GetParameter(parameterName);
        }

        public string GetUsername(string userid)
        {
            NetPlayer netPlayer = GetNetPlayer(userid);
            if (netPlayer == null)
                return String.Empty;
            return netPlayer.User?.Username ?? String.Empty;
        }
        
        public string GetDisplayName(string userid)
        {
            NetPlayer netPlayer = GetNetPlayer(userid);
            if (netPlayer == null || netPlayer.User == null || netPlayer.User.Bio == null)
                return String.Empty;
            return netPlayer.User.Bio.DisplayName ?? String.Empty;
        }

        public Pronouns GetPronouns(string userid)
        {
            NetPlayer netPlayer = GetNetPlayer(userid);
            if (netPlayer == null)
                return null;
            return netPlayer.User.Bio.Pronouns;
        }
    }
}