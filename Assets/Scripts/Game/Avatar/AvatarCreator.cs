﻿using System;
using System.Collections.Generic;
using System.Linq;
using Hypernex.CCK.Unity;
using Hypernex.CCK.Unity.Internals;
using Hypernex.Networking.Messages;
using Hypernex.Sandboxing;
using Hypernex.Tools;
// #if FINAL_IK
using RootMotion.FinalIK;
// #endif
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Hypernex.Game.Avatar
{
    public abstract class AvatarCreator : IDisposable
    {
        public const string PARAMETER_ID = "parameter";
        public const string BLENDSHAPE_ID = "blendshape";
        public const string MAIN_ANIMATOR = "*main";
        public const string ALL_ANIMATOR_LAYERS = "*all";
        
        public CCK.Unity.Avatar Avatar { get; protected set; }
        public Animator MainAnimator { get; protected set; }
        public FaceTrackingDescriptor FaceTrackingDescriptor { get; protected set; }
        public List<AnimatorPlayable> AnimatorPlayables => new (PlayableAnimators);
        public bool Calibrated { get; protected set; }
        
        protected GameObject HeadAlign;
        internal GameObject VoiceAlign;

        protected readonly RuntimeAnimatorController animatorController =
            Object.Instantiate(Init.Instance.DefaultAvatarAnimatorController);
        private List<AnimatorPlayable> PlayableAnimators = new();
        private List<SkinnedMeshRenderer> skinnedMeshRenderers = new();
        protected List<OVRLipSyncContextMorphTarget> morphTargets = new();
        internal OVRLipSyncContext lipSyncContext;
        internal AudioSource audioSource;
        internal List<Sandbox> localAvatarSandboxes = new();
        protected VRIK vrik;
        protected IKSystem iksystem;
        internal Quaternion headRef;

        protected void OnCreate(CCK.Unity.Avatar a, int layer)
        {
            FaceTrackingDescriptor = a.gameObject.GetComponent<FaceTrackingDescriptor>();
            a.gameObject.AddComponent<AvatarBehaviour>();
            foreach (SkinnedMeshRenderer skinnedMeshRenderer in a.gameObject
                         .GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                skinnedMeshRenderer.updateWhenOffscreen = true;
                skinnedMeshRenderers.Add(skinnedMeshRenderer);
            }
            foreach (MaterialDescriptor materialDescriptor in a.transform.GetComponentsInChildren<MaterialDescriptor>())
                materialDescriptor.SetMaterials(AssetBundleTools.Platform);
            foreach (Transform transform in a.transform.GetComponentsInChildren<Transform>())
                transform.gameObject.layer = layer;
            Animator an = a.transform.GetComponent<Animator>();
            if(an != null)
                an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if(MainAnimator == null || MainAnimator.avatar == null) return;
            Transform head = MainAnimator.GetBoneTransform(HumanBodyBones.Head);
            if(head == null) return;
            headRef = Quaternion.Inverse(a.transform.rotation) * head.rotation;
        }

        protected void DriveCamera(Transform head, Transform cam)
        {
            if(vrik != null) return;
            head.rotation = cam.rotation * headRef;
        }
        
        protected void SetupAnimators()
        {
            foreach (CustomPlayableAnimator customPlayableAnimator in Avatar.Animators)
            {
                if (customPlayableAnimator == null || customPlayableAnimator.AnimatorController == null) continue;
                if (customPlayableAnimator.AnimatorOverrideController != null)
                    customPlayableAnimator.AnimatorOverrideController.runtimeAnimatorController =
                        customPlayableAnimator.AnimatorController;
                PlayableGraph playableGraph = PlayableGraph.Create(customPlayableAnimator.AnimatorController.name);
                AnimatorControllerPlayable animatorControllerPlayable =
                    AnimatorControllerPlayable.Create(playableGraph, customPlayableAnimator.AnimatorController);
                PlayableOutput playableOutput = AnimationPlayableOutput.Create(playableGraph,
                    customPlayableAnimator.AnimatorController.name, MainAnimator);
                playableOutput.SetSourcePlayable(animatorControllerPlayable);
                PlayableAnimators.Add(new AnimatorPlayable
                {
                    CustomPlayableAnimator = customPlayableAnimator,
                    PlayableGraph = playableGraph,
                    AnimatorControllerPlayable = animatorControllerPlayable,
                    PlayableOutput = playableOutput,
                    AnimatorControllerParameters = GetAllParameters(animatorControllerPlayable)
                });
                playableGraph.Play();
            }
        }
        
        // Here's an idea Unity.. EXPOSE THE PARAMETERS??
        protected List<AnimatorControllerParameter> GetAllParameters(AnimatorControllerPlayable animatorControllerPlayable)
        {
            List<AnimatorControllerParameter> parameters = new();
            bool c = true;
            int i = 0;
            while (c)
            {
                try
                {
                    AnimatorControllerParameter animatorControllerParameter =
                        animatorControllerPlayable.GetParameter(i);
                    parameters.Add(animatorControllerParameter);
                    i++;
                }
                catch (IndexOutOfRangeException) {c = false;}
            }
            return parameters;
        }

        protected AnimatorControllerParameter GetParameterByName(string name, AnimatorPlayable animatorPlayable)
        {
            foreach (AnimatorControllerParameter animatorPlayableAnimatorControllerParameter in animatorPlayable.AnimatorControllerParameters)
            {
                if (animatorPlayableAnimatorControllerParameter.name == name)
                    return animatorPlayableAnimatorControllerParameter;
            }
            return null;
        }

        protected AnimatorPlayable? GetPlayable(CustomPlayableAnimator customPlayableAnimator) =>
            AnimatorPlayables.Find(x => x.CustomPlayableAnimator == customPlayableAnimator);

        public T GetParameter<T>(string parameterName, CustomPlayableAnimator target = null)
        {
            if (target != null)
            {
                AnimatorPlayable? animatorPlayable = GetPlayable(target);
                if (animatorPlayable != null)
                {
                    switch (Type.GetTypeCode(typeof(T)))
                    {
                        case TypeCode.Boolean:
                            return (T) Convert.ChangeType(
                                animatorPlayable.Value.AnimatorControllerPlayable.GetBool(parameterName), typeof(T));
                        case TypeCode.Int32:
                            return (T) Convert.ChangeType(
                                animatorPlayable.Value.AnimatorControllerPlayable.GetInteger(parameterName), typeof(T));
                        case TypeCode.Double:
                        case TypeCode.Decimal:
                            return (T) Convert.ChangeType(
                                animatorPlayable.Value.AnimatorControllerPlayable.GetFloat(parameterName), typeof(T));
                        default:
                            if(typeof(T) == typeof(float))
                                return (T) Convert.ChangeType(
                                    animatorPlayable.Value.AnimatorControllerPlayable.GetFloat(parameterName), typeof(T));
                            return default;
                    }
                }
            }
            foreach (AnimatorPlayable playableAnimator in AnimatorPlayables)
            {
                if (playableAnimator.AnimatorControllerParameters.Count(x => x.name == parameterName) > 0)
                {
                    switch (Type.GetTypeCode(typeof(T)))
                    {
                        case TypeCode.Boolean:
                            return (T) Convert.ChangeType(
                                playableAnimator.AnimatorControllerPlayable.GetBool(parameterName), typeof(T));
                        case TypeCode.Int32:
                            return (T) Convert.ChangeType(
                                playableAnimator.AnimatorControllerPlayable.GetInteger(parameterName), typeof(T));
                        case TypeCode.Double:
                        case TypeCode.Decimal:
                            return (T) Convert.ChangeType(
                                playableAnimator.AnimatorControllerPlayable.GetFloat(parameterName), typeof(T));
                        default:
                            if(typeof(T) == typeof(float))
                                return (T) Convert.ChangeType(
                                    playableAnimator.AnimatorControllerPlayable.GetFloat(parameterName), typeof(T));
                            return default;
                    }
                }
            }
            return default;
        }
        
        public object GetParameter(string parameterName, CustomPlayableAnimator target = null)
        {
            if (target != null)
            {
                AnimatorPlayable? animatorPlayable = GetPlayable(target);
                if (animatorPlayable != null)
                {
                    AnimatorControllerParameter animatorControllerParameter =
                        GetParameterByName(parameterName, animatorPlayable.Value);
                    if (animatorControllerParameter != null)
                    {
                        switch (animatorControllerParameter.type)
                        {
                            case AnimatorControllerParameterType.Bool:
                                return animatorPlayable.Value.AnimatorControllerPlayable.GetBool(parameterName);
                            case AnimatorControllerParameterType.Int:
                                return animatorPlayable.Value.AnimatorControllerPlayable.GetInteger(parameterName);
                            case AnimatorControllerParameterType.Float:
                                return animatorPlayable.Value.AnimatorControllerPlayable.GetFloat(parameterName);
                        }
                    }
                }
            }
            foreach (AnimatorPlayable playableAnimator in AnimatorPlayables)
            {
                AnimatorControllerParameter animatorControllerParameter =
                    GetParameterByName(parameterName, playableAnimator);
                if (animatorControllerParameter != null)
                {
                    switch (animatorControllerParameter.type)
                    {
                        case AnimatorControllerParameterType.Bool:
                            return playableAnimator.AnimatorControllerPlayable.GetBool(parameterName);
                        case AnimatorControllerParameterType.Int:
                            return playableAnimator.AnimatorControllerPlayable.GetInteger(parameterName);
                        case AnimatorControllerParameterType.Float:
                            return playableAnimator.AnimatorControllerPlayable.GetFloat(parameterName);
                    }
                }
            }
            return default;
        }

        public void SetParameter<T>(string parameterName, T value, CustomPlayableAnimator target = null, 
            bool force = false)
        {
            if (target != null)
            {
                AnimatorPlayable? animatorPlayable = GetPlayable(target);
                if (animatorPlayable != null)
                {
                    AnimatorControllerParameter animatorControllerParameter =
                        GetParameterByName(parameterName, animatorPlayable.Value);
                    if (animatorControllerParameter != null)
                    {
                        switch (animatorControllerParameter.type)
                        {
                            case AnimatorControllerParameterType.Bool:
                                animatorPlayable.Value.AnimatorControllerPlayable.SetBool(parameterName,
                                    (bool) Convert.ChangeType(value, typeof(bool)));
                                break;
                            case AnimatorControllerParameterType.Int:
                                animatorPlayable.Value.AnimatorControllerPlayable.SetInteger(parameterName,
                                    (int) Convert.ChangeType(value, typeof(int)));
                                break;
                            case AnimatorControllerParameterType.Float:
                                animatorPlayable.Value.AnimatorControllerPlayable.SetFloat(parameterName,
                                    (float) Convert.ChangeType(value, typeof(float)));
                                break;
                        }
                    }
                }
                return;
            }
            foreach (AnimatorPlayable playableAnimator in AnimatorPlayables)
            {
                if (force)
                {
                    foreach (AnimatorControllerParameter animatorControllerParameter in playableAnimator
                                 .AnimatorControllerParameters.Where(x => x.name == parameterName))
                    {
                        switch (animatorControllerParameter.type)
                        {
                            case AnimatorControllerParameterType.Bool:
                                playableAnimator.AnimatorControllerPlayable.SetBool(parameterName,
                                    (bool) Convert.ChangeType(value, typeof(bool)));
                                break;
                            case AnimatorControllerParameterType.Int:
                                playableAnimator.AnimatorControllerPlayable.SetInteger(parameterName,
                                    (int) Convert.ChangeType(value, typeof(int)));
                                break;
                            case AnimatorControllerParameterType.Float:
                                playableAnimator.AnimatorControllerPlayable.SetFloat(parameterName,
                                    (float) Convert.ChangeType(value, typeof(float)));
                                break;
                        }
                    }
                }
                else
                {
                    if (playableAnimator.AnimatorControllerParameters.Count(x => x.name == parameterName) > 0)
                    {
                        switch (Type.GetTypeCode(typeof(T)))
                        {
                            case TypeCode.Boolean:
                                playableAnimator.AnimatorControllerPlayable.SetBool(parameterName,
                                    (bool) Convert.ChangeType(value, typeof(bool)));
                                break;
                            case TypeCode.Int32:
                                playableAnimator.AnimatorControllerPlayable.SetInteger(parameterName,
                                    (int) Convert.ChangeType(value, typeof(int)));
                                break;
                            case TypeCode.Double:
                            case TypeCode.Decimal:
                                playableAnimator.AnimatorControllerPlayable.SetFloat(parameterName,
                                    (float) Convert.ChangeType(value, typeof(float)));
                                break;
                            default:
                                if (typeof(T) == typeof(float))
                                    playableAnimator.AnimatorControllerPlayable.SetFloat(parameterName,
                                        (float) Convert.ChangeType(value, typeof(float)));
                                break;
                        }
                    }
                }
            }
        }

        internal List<WeightedObjectUpdate> GetAnimatorWeights()
        {
            List<WeightedObjectUpdate> weights = new();
            foreach (AnimatorControllerParameter animatorControllerParameter in MainAnimator.parameters)
            {
                float weight;
                switch (animatorControllerParameter.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        weight = MainAnimator.GetBool(animatorControllerParameter.name) ? 1f : 0f;
                        break;
                    case AnimatorControllerParameterType.Int:
                        weight = MainAnimator.GetInteger(animatorControllerParameter.name);
                        break;
                    case AnimatorControllerParameterType.Float:
                        weight = MainAnimator.GetFloat(animatorControllerParameter.name);
                        break;
                    default:
                        continue;
                }
                WeightedObjectUpdate weightedObjectUpdate = new WeightedObjectUpdate
                {
                    PathToWeightContainer = MAIN_ANIMATOR,
                    TypeOfWeight = PARAMETER_ID,
                    WeightIndex = animatorControllerParameter.name,
                    Weight = weight
                };
                weights.Add(weightedObjectUpdate);
            }
            foreach (AnimatorPlayable playableAnimator in AnimatorPlayables)
            {
                foreach (AnimatorControllerParameter playableAnimatorControllerParameter in playableAnimator.AnimatorControllerParameters)
                {
                    WeightedObjectUpdate weightedObjectUpdate = new WeightedObjectUpdate
                    {
                        PathToWeightContainer = playableAnimator.CustomPlayableAnimator.AnimatorController.name,
                        TypeOfWeight = PARAMETER_ID,
                        WeightIndex = playableAnimatorControllerParameter.name
                    };
                    switch (playableAnimatorControllerParameter.type)
                    {
                        case AnimatorControllerParameterType.Bool:
                            weightedObjectUpdate.Weight =
                                playableAnimator.AnimatorControllerPlayable.GetBool(playableAnimatorControllerParameter
                                    .name)
                                    ? 1.00f
                                    : 0.00f;
                            break;
                        case AnimatorControllerParameterType.Int:
                            weightedObjectUpdate.Weight =
                                playableAnimator.AnimatorControllerPlayable.GetInteger(
                                    playableAnimatorControllerParameter.name);
                            break;
                        case AnimatorControllerParameterType.Float:
                            weightedObjectUpdate.Weight =
                                playableAnimator.AnimatorControllerPlayable.GetFloat(playableAnimatorControllerParameter
                                    .name);
                            break;
                        default:
                            continue;
                    }
                    weights.Add(weightedObjectUpdate);
                }
            }
            foreach (SkinnedMeshRenderer skinnedMeshRenderer in skinnedMeshRenderers)
            {
                PathDescriptor p = skinnedMeshRenderer.gameObject.GetComponent<PathDescriptor>();
                if(p == null) continue;
                for (int i = 0; i < skinnedMeshRenderer.sharedMesh.blendShapeCount; i++)
                {
                    // Exclude Visemes
                    if (Avatar.UseVisemes && Avatar.VisemesDict.Count(x =>
                            x.Value.SkinnedMeshRenderer == skinnedMeshRenderer && x.Value.BlendshapeIndex == i) >
                        0) continue;
                    // Exclude ShadowClones
                    if(skinnedMeshRenderer.gameObject.name.Contains("shadowclone_")) continue;
                    float w = skinnedMeshRenderer.GetBlendShapeWeight(i);
                    WeightedObjectUpdate weightedObjectUpdate = new WeightedObjectUpdate
                    {
                        PathToWeightContainer = p.path,
                        TypeOfWeight = BLENDSHAPE_ID,
                        WeightIndex = i.ToString(),
                        Weight = w
                    };
                    weights.Add(weightedObjectUpdate);
                }
            }
            return weights;
        }
        
        public void GetBlendshapeWeight(SkinnedMeshRenderer skinnedMeshRenderer, int blendshapeIndex) =>
            skinnedMeshRenderer.GetBlendShapeWeight(blendshapeIndex);

        public void SetBlendshapeWeight(SkinnedMeshRenderer skinnedMeshRenderer, int blendshapeIndex, float weight) =>
            skinnedMeshRenderer.SetBlendShapeWeight(blendshapeIndex, weight);
        
        public Transform GetBoneFromHumanoid(HumanBodyBones humanBodyBones)
        {
            if (MainAnimator == null)
                return null;
            return MainAnimator.GetBoneTransform(humanBodyBones);
        }
        
        internal void ApplyAudioClipToLipSync(float[] data)
        {
            if (lipSyncContext == null)
                return;
            lipSyncContext.PreprocessAudioSamples(data, (int) Mic.NumChannels);
            lipSyncContext.ProcessAudioSamples(data, (int) Mic.NumChannels);
            lipSyncContext.PostprocessAudioSamples(data, (int) Mic.NumChannels);
        }
        
        protected OVRLipSyncContextMorphTarget GetMorphTargetBySkinnedMeshRenderer(
            SkinnedMeshRenderer skinnedMeshRenderer)
        {
            foreach (OVRLipSyncContextMorphTarget morphTarget in new List<OVRLipSyncContextMorphTarget>(morphTargets))
            {
                if (morphTarget == null)
                    morphTargets.Remove(morphTarget);
                else if (morphTarget.skinnedMeshRenderer == skinnedMeshRenderer)
                    return morphTarget;
            }
            OVRLipSyncContextMorphTarget m = VoiceAlign.AddComponent<OVRLipSyncContextMorphTarget>();
            m.skinnedMeshRenderer = skinnedMeshRenderer;
            morphTargets.Add(m);
            return m;
        }

        protected void SetVisemeAsBlendshape(ref OVRLipSyncContextMorphTarget morphTarget, Viseme viseme,
            BlendshapeDescriptor blendshapeDescriptor)
        {
            int indexToInsert = (int) viseme;
            int[] currentBlendshapes = new int[15];
            Array.Copy(morphTarget.visemeToBlendTargets, currentBlendshapes, 15);
            currentBlendshapes[indexToInsert] = blendshapeDescriptor.BlendshapeIndex;
            morphTarget.visemeToBlendTargets = currentBlendshapes;
        }
        
        protected void RelaxWrists(Transform leftLowerArm, Transform rightLowerArm, Transform leftHand,
            Transform rightHand)
        {
            if (leftLowerArm == null || rightLowerArm == null || leftHand == null || rightHand == null)
                return;
#if FINAL_IK
            TwistRelaxer twistRelaxer = Avatar.gameObject.AddComponent<TwistRelaxer>();
            twistRelaxer.ik = vrik;
            TwistSolver leftSolver = new TwistSolver { transform = leftLowerArm, children = new []{leftHand} };
            TwistSolver rightSolver = new TwistSolver { transform = rightLowerArm, children = new []{rightHand} };
            twistRelaxer.twistSolvers = new[] { leftSolver, rightSolver };
#endif
        }
        
        internal void FixedUpdate() => localAvatarSandboxes.ForEach(x => x.Runtime.FixedUpdate());
        internal void Update() => localAvatarSandboxes.ForEach(x => x.Runtime.Update());
        internal void LateUpdate() => localAvatarSandboxes.ForEach(x => x.Runtime.LateUpdate());

        public virtual void Dispose()
        {
            foreach (AnimatorPlayable playableAnimator in AnimatorPlayables)
                try
                {
                    playableAnimator.PlayableGraph.Destroy();
                }
                catch(ArgumentException){}
            foreach (Sandbox localAvatarSandbox in new List<Sandbox>(localAvatarSandboxes))
            {
                localAvatarSandboxes.Remove(localAvatarSandbox);
                localAvatarSandbox.Dispose();
            }
            Object.Destroy(Avatar.gameObject);
        }
        
        public class AvatarBehaviour : MonoBehaviour
        {
            private void OnFootstep(AnimationEvent animationEvent)
            {
            
            }

            private void OnLand(AnimationEvent animationEvent)
            {
            
            }
        }
    }
}