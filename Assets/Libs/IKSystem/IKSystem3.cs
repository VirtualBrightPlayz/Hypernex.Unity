using System;
using DitzelGames.FastIK;
using UnityEngine;

public class IKSystem3 : MonoBehaviour
{
    [Serializable]
    public struct IKData
    {
        public FastIKFabric ik;
        public Transform target;
        public Transform pole;
    }

    [Header("Settings")]
    public Animator humanoid;
    [Range(0f, 1f)]
    public float SnapBackStrength = 0.5f;
    public bool handIk = true;
    public bool footIk = true;
    public bool moveFeet = true;

    public float minStepHeight = 0.1f;
    public float maxStepHeight = 0.2f;

    public float minStepLength = -0.5f;
    public float maxStepLength = 0.5f;

    public float loopSize = 1f;

    [Min(0f)]
    public float stepDistance = 0.2f;
    [Min(0f)]
    public float reachDistance = 0.2f;
    [Min(0.01f)]
    public float footMoveSpeed = 1f;
    public AnimationCurve velCurve;

    [Header("Debug area")]
    public IKData leftHandData;
    public IKData rightHandData;
    public IKData leftFootData;
    public IKData rightFootData;

    public Transform head;
    public Transform headTarget;
    public Vector3 direction;
    public Transform hips;
    public Transform leftHand;
    public Transform rightHand;
    public Transform leftFoot;
    public Transform rightFoot;

    public float footDistance;

    public Vector3 leftFootPos;
    public Vector3 rightFootPos;

    public Vector3 hipsPos;

    private void OnEnable()
    {
        Init();
    }

    private static float MapValue(float value, float min1, float max1, float min2, float max2)
    {
        return (value - min1) / (max1 - min1) * (max2 - min2) + min2;
    }

    private void LateUpdate()
    {
        if (leftHandData.ik)
            leftHandData.ik.enabled = handIk;
        if (rightHandData.ik)
            rightHandData.ik.enabled = handIk;
        if (leftFootData.ik)
            leftFootData.ik.enabled = footIk;
        if (rightFootData.ik)
            rightFootData.ik.enabled = footIk;
        if (hips && head && headTarget)
        {
            hips.localPosition = hips.parent.InverseTransformPoint(headTarget.position) - hips.parent.InverseTransformVector(direction);
            head.position = headTarget.position;
            head.rotation = headTarget.rotation;
        }
    }

    public int GetChainLength(Transform parent, Transform child, int iter = 0)
    {
        if (!parent || !child.IsChildOf(parent))
        {
            return 2;
        }
        if (parent == child)
        {
            return iter;
        }
        return GetChainLength(parent, child.parent, iter + 1);
    }

    public void Init()
    {
        OnDisable();
        if (humanoid && humanoid.avatar && humanoid.avatar.isHuman)
        {
            head = humanoid.GetBoneTransform(HumanBodyBones.Head);
            hips = humanoid.GetBoneTransform(HumanBodyBones.Hips);
            leftHand = humanoid.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = humanoid.GetBoneTransform(HumanBodyBones.RightHand);
            leftFoot = humanoid.GetBoneTransform(HumanBodyBones.LeftFoot);
            rightFoot = humanoid.GetBoneTransform(HumanBodyBones.RightFoot);
            direction = humanoid.GetBoneTransform(HumanBodyBones.Head).position - humanoid.GetBoneTransform(HumanBodyBones.Hips).position;
            float scl = direction.magnitude;
            footDistance = Vector3.Distance(leftFoot.position, rightFoot.position);

            // hips and head
            {
                headTarget = new GameObject("Head Target").transform;
                headTarget.SetParent(transform);
                headTarget.position = head.position;
                headTarget.rotation = head.rotation;
                hipsPos = hips.position;
            }

            // left hand
            {
                leftHandData.target = new GameObject("LeftHand Target").transform;
                leftHandData.target.SetParent(transform);
                leftHandData.target.position = leftHand.position;
                leftHandData.target.rotation = leftHand.rotation;
                leftHandData.pole = new GameObject("LeftHand Pole").transform;
                leftHandData.pole.SetParent(head);
                leftHandData.pole.position = head.position - transform.right * scl - transform.forward * scl + transform.up * 0.5f * scl;

                FastIKFabric ik = leftHand.gameObject.AddComponent<FastIKFabric>();
                ik.Target = leftHandData.target;
                ik.Pole = leftHandData.pole;
                if (humanoid.GetBoneTransform(HumanBodyBones.LeftShoulder))
                    ik.ChainLength = 3;
                // else
                    ik.ChainLength = 2;
                ik.ChainLength = GetChainLength(humanoid.GetBoneTransform(HumanBodyBones.LeftShoulder), leftHand);
                ik.SnapBackStrength = SnapBackStrength;
                ik.Init();
                leftHandData.ik = ik;
            }
            // right hand
            {
                rightHandData.target = new GameObject("RightHand Target").transform;
                rightHandData.target.SetParent(transform);
                rightHandData.target.position = rightHand.position;
                rightHandData.target.rotation = rightHand.rotation;
                rightHandData.pole = new GameObject("RightHand Pole").transform;
                rightHandData.pole.SetParent(head);
                rightHandData.pole.position = head.position + transform.right * scl - transform.forward * scl + transform.up * 0.5f * scl;

                FastIKFabric ik = rightHand.gameObject.AddComponent<FastIKFabric>();
                ik.Target = rightHandData.target;
                ik.Pole = rightHandData.pole;
                if (humanoid.GetBoneTransform(HumanBodyBones.RightShoulder))
                    ik.ChainLength = 3;
                // else
                    ik.ChainLength = 2;
                ik.ChainLength = GetChainLength(humanoid.GetBoneTransform(HumanBodyBones.RightShoulder), rightHand);
                ik.SnapBackStrength = SnapBackStrength;
                ik.Init();
                rightHandData.ik = ik;
            }

            // left foot
            {
                leftFootData.target = new GameObject("LeftFoot Target").transform;
                leftFootData.target.SetParent(transform);
                leftFootData.target.position = leftFoot.position;
                leftFootData.target.rotation = leftFoot.rotation;
                leftFootData.pole = new GameObject("LeftFoot Pole").transform;
                leftFootData.pole.SetParent(hips);
                leftFootData.pole.position = humanoid.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position - transform.right * 0.25f * scl + transform.forward * 1.5f * scl + transform.up * 0.9f * scl;

                FastIKFabric ik = leftFoot.gameObject.AddComponent<FastIKFabric>();
                ik.Target = leftFootData.target;
                ik.Pole = leftFootData.pole;
                ik.ChainLength = 2;
                ik.ChainLength = GetChainLength(humanoid.GetBoneTransform(HumanBodyBones.LeftUpperLeg), leftFoot);
                ik.SnapBackStrength = SnapBackStrength;
                ik.Init();
                leftFootData.ik = ik;
                leftFootPos = leftFoot.position;
            }
            // right foot
            {
                rightFootData.target = new GameObject("RightFoot Target").transform;
                rightFootData.target.SetParent(transform);
                rightFootData.target.position = rightFoot.position;
                rightFootData.target.rotation = rightFoot.rotation;
                rightFootData.pole = new GameObject("RightFoot Pole").transform;
                rightFootData.pole.SetParent(hips);
                rightFootData.pole.position = humanoid.GetBoneTransform(HumanBodyBones.RightUpperLeg).position + transform.right * 0.25f * scl + transform.forward * 1.5f * scl + transform.up * 0.9f * scl;

                FastIKFabric ik = rightFoot.gameObject.AddComponent<FastIKFabric>();
                ik.Target = rightFootData.target;
                ik.Pole = rightFootData.pole;
                ik.ChainLength = 2;
                ik.ChainLength = GetChainLength(humanoid.GetBoneTransform(HumanBodyBones.RightUpperLeg), rightFoot);
                ik.SnapBackStrength = SnapBackStrength;
                ik.Init();
                rightFootData.ik = ik;
                rightFootPos = rightFoot.position;
            }
        }
    }

    private void OnDisable()
    {
        void DestroyIKData(IKData data)
        {
            if (data.target)
                DestroyImmediate(data.target.gameObject);
            if (data.pole)
                DestroyImmediate(data.pole.gameObject);
            if (data.ik)
                DestroyImmediate(data.ik);
        }
        DestroyIKData(leftHandData);
        DestroyIKData(rightHandData);
        DestroyIKData(leftFootData);
        DestroyIKData(rightFootData);
    }

    private void OnDrawGizmosSelected()
    {
        void DrawIKData(IKData data)
        {
            Gizmos.color = Color.red;
            if (data.target)
                Gizmos.DrawSphere(data.target.position, 0.1f);
            Gizmos.color = Color.blue;
            if (data.pole)
                Gizmos.DrawSphere(data.pole.position, 0.1f);
        }
        DrawIKData(leftHandData);
        DrawIKData(rightHandData);
        DrawIKData(leftFootData);
        DrawIKData(rightFootData);
    }
}
