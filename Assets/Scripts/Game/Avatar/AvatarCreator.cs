﻿using System;
using System.Collections.Generic;
using System.Linq;
using Hypernex.CCK.Unity;
using Hypernex.CCK.Unity.Internals;
using Hypernex.Networking.Messages;
using Hypernex.Sandboxing;
using Hypernex.Tools;
using HypernexSharp.APIObjects;
#if FINAL_IK
using RootMotion.FinalIK;
#endif
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;
using Security = Hypernex.CCK.Unity.Security;

namespace Hypernex.Game.Avatar
{
    public abstract class AvatarCreator : IDisposable
    {
        public const string PARAMETER_ID = "parameter";
        public const string BLENDSHAPE_ID = "blendshape";
        public const string MAIN_ANIMATOR = "*main";
        public const string ALL_ANIMATOR_LAYERS = "*all";
        protected const float CHARACTER_HEIGHT = 1.36144f;
        
        public CCK.Unity.Avatar Avatar { get; protected set; }
        public AvatarMeta AvatarMeta { get; private set; }
        public Animator MainAnimator { get; protected set; }
        public FaceTrackingDescriptor FaceTrackingDescriptor { get; protected set; }
        public List<AnimatorPlayable> AnimatorPlayables = new();
        public bool Calibrated { get; protected set; }

        internal List<WeightedObjectUpdate> DefaultWeights;

#if FINAL_IK
        private VRIKCalibrator.Settings vrikSettings = new()
        {
            handOffset = new Vector3(0, 0.01f, -0.1f),
            pelvisPositionWeight = 0f,
            pelvisRotationWeight = 0f
        };
#endif
        
        private Dictionary<string, Transform> cachedTransforms = new();
        private Dictionary<Transform, SkinnedMeshRenderer> cachedSkinnedMeshRenderers = new();

        private List<AnimatorControllerParameter> _parameters;
        public List<AnimatorControllerParameter> Parameters
        {
            get
            {
                if (_parameters == null)
                {
                    List<AnimatorControllerParameter> allParameters = new();
                    foreach (AnimatorPlayable animatorPlayable in AnimatorPlayables)
                    {
                        List<AnimatorControllerParameter> playableParameters =
                            GetAllParameters(animatorPlayable.AnimatorControllerPlayable);
                        allParameters = allParameters.Concat(playableParameters).ToList();
                    }
                    _parameters = allParameters;
                }
                return _parameters;
            }
        }

        private AnimatorControllerParameter[] _mainParameters;

        public AnimatorControllerParameter[] MainAnimatorParameters
        {
            get
            {
                if (_mainParameters == null)
                {
                    if (MainAnimator == null) return Array.Empty<AnimatorControllerParameter>();
                    _mainParameters = MainAnimator.parameters;
                }
                return _mainParameters;
            }
        }
        
        protected GameObject HeadAlign;
        internal GameObject VoiceAlign;

        protected readonly RuntimeAnimatorController animatorController =
            Object.Instantiate(Init.Instance.DefaultAvatarAnimatorController);
        private List<SkinnedMeshRenderer> skinnedMeshRenderers = new();
        protected List<OVRLipSyncContextMorphTarget> morphTargets = new();
        internal OVRLipSyncContext lipSyncContext;
        internal AudioSource audioSource;
        internal List<Sandbox> localAvatarSandboxes = new();
#if FINAL_IK
        protected VRIK vrik;
#else
        protected IKSystem2 vrik;
#endif
        internal RotationOffsetDriver headRotator;

        protected void OnCreate(CCK.Unity.Avatar a, int layer, AllowedAvatarComponent allowedAvatarComponent, AvatarMeta meta)
        {
            Security.RemoveOffendingItems(a, allowedAvatarComponent,
                SecurityTools.AdditionalAllowedAvatarTypes.ToArray());
            Security.ApplyComponentRestrictions(a);
            AvatarMeta = meta;
            FaceTrackingDescriptor = a.gameObject.GetComponent<FaceTrackingDescriptor>();
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
            MainAnimator.runtimeAnimatorController = null;
            MainAnimator.applyRootMotion = false;
            Transform head = MainAnimator.GetBoneTransform(HumanBodyBones.Head);
            if(head == null) return;
            headRotator = new RotationOffsetDriver(head, a.transform);
#if FINAL_IK
            vrikSettings.headOffset = head.position - HeadAlign.transform.position;
            vrikSettings.scaleMlp = a.transform.localScale.y;
#endif
        }

        protected void DriveCamera(Transform cam)
        {
            if(vrik != null || headRotator == null) return;
            headRotator.Rotate(cam.rotation);
        }
        
        protected virtual void SetupAnimators()
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
                AnimatorPlayables.Add(new AnimatorPlayable
                {
                    CustomPlayableAnimator = customPlayableAnimator,
                    PlayableGraph = playableGraph,
                    AnimatorControllerPlayable = animatorControllerPlayable,
                    PlayableOutput = playableOutput,
                    AnimatorControllerParameters = GetAllParameters(animatorControllerPlayable)
                });
                playableGraph.Play();
            }
            DefaultWeights = GetAnimatorWeights(true);
        }

        private Bounds GetAvatarBounds()
        {
            Bounds bounds = new Bounds(MainAnimator.transform.position, Vector3.zero);
            foreach (Renderer renderer in MainAnimator.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        protected void AlignAvatar(bool isVR)
        {
            Bounds b = GetAvatarBounds();
            float avatarBottom = MainAnimator.transform.parent.InverseTransformPoint(b.min).y - MainAnimator.transform.localPosition.y;
            float requiredOffset = -avatarBottom;
            Vector3 pos = new Vector3(0, requiredOffset - (CHARACTER_HEIGHT / 2f), 0);
            Avatar.transform.localPosition = isVR ? Vector3.zero : pos;
            Avatar.transform.localRotation = Quaternion.identity;
        }

        protected IKSystem2 AddVRIK(GameObject avatar)
        {
            /*Transform leftHand = GetBoneFromHumanoid(HumanBodyBones.LeftHand);
            Transform rightHand = GetBoneFromHumanoid(HumanBodyBones.RightHand);
            Transform leftHandTemp = new GameObject("templefthandalign_" + Guid.NewGuid()).transform;
            Transform rightHandTemp = new GameObject("temprighthandalign_" + Guid.NewGuid()).transform;
            leftHandTemp.SetParent(leftHand);
            leftHandTemp.localPosition = Vector3.zero;
            leftHandTemp.localRotation = Quaternion.identity;
            rightHandTemp.SetParent(rightHand);
            rightHandTemp.localPosition = Vector3.zero;
            rightHandTemp.localRotation = Quaternion.identity;
            leftHandRot = leftHandTemp.rotation;
            rightHandRot = rightHandTemp.rotation;
            Object.Destroy(leftHandTemp.gameObject);
            Object.Destroy(rightHandTemp.gameObject);*/
            IKSystem2 v = avatar.AddComponent<IKSystem2>();
            v.humanoid = MainAnimator;
            v.minStepHeight = 0.1f;
            v.minStepDistance = 0.1f;
            v.footMoveSpeed = 5f;
            v.timeoutTime = 0.25f;
            v.footAnimCurve = new AnimationCurve();
            v.footAnimCurve.AddKey(0f, 0f);
            v.footAnimCurve.AddKey(0.5f, 1f);
            v.footAnimCurve.AddKey(1f, 0f);
            v.Init();
            v.maxStepDistance = v.footDistance;
            v.handIk = false;
            v.hipIk = false;
            return v;
        }

#if FINAL_IK
        private void SetCalibrationMeta()
        {
            vrik.solver.locomotion.stepThreshold = 0.01f;
            vrik.solver.locomotion.angleThreshold = 20;
            vrik.solver.plantFeet = false;
        }
        
        /*private Quaternion leftHandRot;
        private Quaternion rightHandRot;*/

        protected VRIK AddVRIK(GameObject avatar)
        {
            /*Transform leftHand = GetBoneFromHumanoid(HumanBodyBones.LeftHand);
            Transform rightHand = GetBoneFromHumanoid(HumanBodyBones.RightHand);
            Transform leftHandTemp = new GameObject("templefthandalign_" + Guid.NewGuid()).transform;
            Transform rightHandTemp = new GameObject("temprighthandalign_" + Guid.NewGuid()).transform;
            leftHandTemp.SetParent(leftHand);
            leftHandTemp.localPosition = Vector3.zero;
            leftHandTemp.localRotation = Quaternion.identity;
            rightHandTemp.SetParent(rightHand);
            rightHandTemp.localPosition = Vector3.zero;
            rightHandTemp.localRotation = Quaternion.identity;
            leftHandRot = leftHandTemp.rotation;
            rightHandRot = rightHandTemp.rotation;
            Object.Destroy(leftHandTemp.gameObject);
            Object.Destroy(rightHandTemp.gameObject);*/
            VRIK v = avatar.AddComponent<VRIK>();
            return v;
        }

        protected VRIKCalibrator.CalibrationData CalibrateVRIK(Transform cameraTransform, Transform LeftHandReference, Transform RightHandReference)
        {
            VRIKCalibrator.CalibrationData calibrationData = VRIKCalibrator.Calibrate(vrik, vrikSettings,
                cameraTransform, null, LeftHandReference.transform, RightHandReference.transform);
            // TODO: Rotate Correctly
            /*LeftHandReference.GetComponent<RotationConstraint>().rotationOffset = leftHandRot.eulerAngles;
            RightHandReference.GetComponent<RotationConstraint>().rotationOffset = rightHandRot.eulerAngles;
            vrik.solver.leftArm.target = LeftHandReference;
            vrik.solver.rightArm.target = RightHandReference;*/
            SetCalibrationMeta();
            return calibrationData;
        }

        protected VRIKCalibrator.CalibrationData CalibrateVRIK(Transform cameraTransform, Transform bodyTracker,
            Transform LeftHandReference, Transform RightHandReference, Transform leftFootTracker,
            Transform rightFootTracker) => VRIKCalibrator.Calibrate(vrik, vrikSettings, cameraTransform, bodyTracker,
            LeftHandReference, RightHandReference, leftFootTracker, rightFootTracker);

        protected void CalibrateVRIK(VRIKCalibrator.CalibrationData calibrationData, Transform headReference, Transform leftHandReference, Transform rightHandReference)
        {
            VRIKCalibrator.Calibrate(vrik, calibrationData, headReference, null, leftHandReference,
                rightHandReference);
            SetCalibrationMeta();
        }

        protected void CalibrateVRIK(VRIKCalibrator.CalibrationData calibrationData, Transform headReference,
            Transform body, Transform leftHandReference, Transform rightHandReference, Transform leftFootTracker,
            Transform rightFootTracker) => VRIKCalibrator.Calibrate(vrik, calibrationData, headReference, body,
            leftHandReference, rightHandReference, leftFootTracker, rightFootTracker);

        protected void UpdateVRIK(bool fbt, bool isMoving, float scale)
        {
            if(fbt)
            {
                vrik.solver.spine.pelvisPositionWeight = 1f;
                vrik.solver.spine.pelvisRotationWeight = 1f;
            }
            else
            {
                vrik.solver.locomotion.footDistance = 0.1f * scale * CHARACTER_HEIGHT;
                vrik.solver.locomotion.stepThreshold = 0.2f * scale * CHARACTER_HEIGHT;
                vrik.solver.spine.pelvisPositionWeight = 0f;
                vrik.solver.spine.pelvisRotationWeight = 0f;
            }
            vrik.solver.locomotion.weight = isMoving || fbt ? 0f : 1f;
        }
#endif
        
        // Here's an idea Unity.. EXPOSE THE PARAMETERS??
        private List<AnimatorControllerParameter> GetAllParameters(AnimatorControllerPlayable animatorControllerPlayable)
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

        /// <summary>
        /// Sets the value of a Parameter
        /// </summary>
        /// <param name="parameterName">The name of the Parameter</param>
        /// <param name="value">The value to set the parameter to</param>
        /// <param name="target">The target Playable, null for all</param>
        /// <param name="force">Forces the value to be casted to all types</param>
        /// <param name="mainAnimator">Drive the parameter for the MainAnimator</param>
        /// <typeparam name="T">A valid parameter type (float, int, or bool)</typeparam>
        public void SetParameter<T>(string parameterName, T value, CustomPlayableAnimator target = null, 
            bool force = false, bool mainAnimator = false)
        {
            if (mainAnimator)
            {
                try
                {
                    AnimatorControllerParameter animatorControllerParameter =
                        MainAnimatorParameters.First(x => x.name == parameterName);
                    switch (animatorControllerParameter.type)
                    {
                        case AnimatorControllerParameterType.Bool:
                            MainAnimator.SetBool(parameterName, (bool) Convert.ChangeType(value, typeof(bool)));
                            break;
                        case AnimatorControllerParameterType.Int:
                            MainAnimator.SetInteger(parameterName, (int) Convert.ChangeType(value, typeof(int)));
                            break;
                        case AnimatorControllerParameterType.Float:
                            MainAnimator.SetFloat(parameterName, (float) Convert.ChangeType(value, typeof(float)));
                            break;
                    }
                }catch(Exception){}
            }
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
        
        public void SetParameter(WeightedObjectUpdate weight)
        {
            switch (weight.TypeOfWeight.ToLower())
            {
                case PARAMETER_ID:
                {
                    string parameterName = weight.WeightIndex;
                    if (weight.PathToWeightContainer == MAIN_ANIMATOR)
                    {
                        if(MainAnimator == null || MainAnimator.runtimeAnimatorController == null) break;
                        try
                        {
                            AnimatorControllerParameter parameter =
                                MainAnimator.parameters.First(x => x.name == weight.WeightIndex);
                            switch (parameter.type)
                            {
                                case AnimatorControllerParameterType.Bool:
                                    MainAnimator.SetBool(parameter.name, Math.Abs(weight.Weight - 1.00f) < 0.01);
                                    break;
                                case AnimatorControllerParameterType.Int:
                                    MainAnimator.SetInteger(parameter.name, (int) weight.Weight);
                                    break;
                                case AnimatorControllerParameterType.Float:
                                    MainAnimator.SetFloat(parameter.name, weight.Weight);
                                    break;
                            }
                        } catch(Exception){}
                        break;
                    }
                    foreach (AnimatorPlayable playableAnimator in AnimatorPlayables)
                    {
                        if (playableAnimator.CustomPlayableAnimator.AnimatorController.name !=
                            weight.PathToWeightContainer && weight.PathToWeightContainer != ALL_ANIMATOR_LAYERS) continue;
                        try
                        {
                            AnimatorControllerParameter parameter = GetParameterByName(parameterName, playableAnimator);
                            if (parameter != null)
                            {
                                switch (parameter.type)
                                {
                                    case AnimatorControllerParameterType.Bool:
                                        playableAnimator.AnimatorControllerPlayable.SetBool(parameterName,
                                            Math.Abs(weight.Weight - 1.00f) < 0.01);
                                        break;
                                    case AnimatorControllerParameterType.Int:
                                        playableAnimator.AnimatorControllerPlayable.SetInteger(parameterName,
                                            (int) weight.Weight);
                                        break;
                                    case AnimatorControllerParameterType.Float:
                                        playableAnimator.AnimatorControllerPlayable.SetFloat(parameterName,
                                            weight.Weight);
                                        break;
                                }
                            }
                        } catch(Exception){}
                    }
                    break;
                }
                case BLENDSHAPE_ID:
                {
                    try
                    {
                        if(!cachedTransforms.TryGetValue(weight.PathToWeightContainer, out Transform t))
                        {
                            t = Avatar.transform.parent.Find(weight.PathToWeightContainer);
                            if(t == null) break;
                            cachedTransforms.Add(weight.PathToWeightContainer, t);
                        }
                        if(!cachedSkinnedMeshRenderers.TryGetValue(t, out SkinnedMeshRenderer s))
                        {
                            s = t.gameObject.GetComponent<SkinnedMeshRenderer>();
                            if (s == null) break;
                            cachedSkinnedMeshRenderers.Add(t, s);
                        }
                        s.SetBlendShapeWeight(Convert.ToInt32(weight.WeightIndex), weight.Weight);
                    } catch(Exception){}
                    break;
                }
            }
        }

        public void SetParameters(WeightedObjectUpdate[] weights)
        {
            foreach (WeightedObjectUpdate weight in weights)
                SetParameter(weight);
        }

        internal List<WeightedObjectUpdate> GetAnimatorWeights(bool skipMain = false)
        {
            List<WeightedObjectUpdate> weights = new();
            if(MainAnimator != null && !skipMain)
            {
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

        /// <summary>
        /// Gets the current Index for the current Viseme using the Oculus Viseme Index
        /// </summary>
        /// <returns></returns>
        public int GetVisemeIndex()
        {
            try
            {
                // This uses the Oculus Viseme Index
                float[] visemes = lipSyncContext.GetCurrentPhonemeFrame()?.Visemes ?? Array.Empty<float>();
                (int, float)? biggest = null;
                for (int i = 0; i < visemes.Length; i++)
                {
                    float visemeWeight = visemes[i];
                    if (biggest == null || visemeWeight > biggest.Value.Item2)
                        biggest = (i, visemeWeight);
                }

                if (biggest == null) return -1;
                if (biggest.Value.Item2 <= 0f) return -1;
                return biggest.Value.Item1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>
        /// Gets all Visemes and their values
        /// </summary>
        /// <returns>Key: Name of Viseme, Value: Weight of Viseme</returns>
        public Dictionary<string, float> GetVisemes()
        {
            try
            {
                Dictionary<string, float> allVisemes = new();
                float[] visemes = lipSyncContext.GetCurrentPhonemeFrame()?.Visemes ?? Array.Empty<float>();
                for (int i = 0; i < visemes.Length; i++)
                {
                    string name = Enum.GetNames(typeof(OVRLipSync.Viseme))[i];
                    allVisemes.Add(name, visemes[i]);
                }

                return allVisemes;
            }
            catch (Exception)
            {
                return new Dictionary<string, float>();
            }
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

#if FINAL_IK
        protected void RelaxWrists(Transform leftLowerArm, Transform rightLowerArm, Transform leftHand,
            Transform rightHand)
        {
            if (leftLowerArm == null || rightLowerArm == null || leftHand == null || rightHand == null)
                return;
            TwistRelaxer twistRelaxer = Avatar.gameObject.AddComponent<TwistRelaxer>();
            twistRelaxer.ik = vrik;
            TwistSolver leftSolver = new TwistSolver { transform = leftLowerArm, children = new []{leftHand} };
            TwistSolver rightSolver = new TwistSolver { transform = rightLowerArm, children = new []{rightHand} };
            twistRelaxer.twistSolvers = new[] { leftSolver, rightSolver };
        }
#endif
        
        public Transform GetTargetChild(Transform tracker)
        {
            for (int i = 0; i < tracker.childCount; i++)
            {
                Transform t = tracker.GetChild(0);
                if (t.gameObject.name.Contains("Target")) return t;
            }
            return null;
        }
        
        internal void FixedUpdate() => localAvatarSandboxes.ForEach(x => x.InstanceContainer.Runtime.FixedUpdate());

        internal void Update()
        {
            SetParameter("Viseme", GetVisemeIndex(), null, true);
            foreach (KeyValuePair<string, float> viseme in GetVisemes())
                SetParameter(viseme.Key, viseme.Value, null, true);
            localAvatarSandboxes.ForEach(x => x.InstanceContainer.Runtime.Update());
        }
        
        internal void LateUpdate() => localAvatarSandboxes.ForEach(x => x.InstanceContainer.Runtime.LateUpdate());

        protected void DisposeScripts()
        {
            foreach (Sandbox localAvatarSandbox in new List<Sandbox>(localAvatarSandboxes))
            {
                localAvatarSandboxes.Remove(localAvatarSandbox);
                localAvatarSandbox.Dispose();
            }
        }

        public virtual void Dispose()
        {
            foreach (AnimatorPlayable playableAnimator in AnimatorPlayables)
                try
                {
                    playableAnimator.PlayableGraph.Destroy();
                }
                catch(ArgumentException){}
            DisposeScripts();
            cachedTransforms.Clear();
            cachedSkinnedMeshRenderers.Clear();
            Object.Destroy(Avatar.gameObject);
        }
    }
}