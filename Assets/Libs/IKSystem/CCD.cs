using System.Collections.Generic;
using UnityEngine;

namespace IK
{
    public class CCD : MonoBehaviour
    {
        public int ChainLength = 3;
        public float targetDistance = 0.1f;
        public Transform start;
        public Transform end;
        public Transform target;

        public void Update()
        {
            SolveNow();
        }

        [ContextMenu(nameof(SolveNow))]
        public void SolveNow()
        {
            for (int i = 0; i < ChainLength; i++)
            {
                SolveUp(end);
            }
        }

        public void SolveUp(Transform xform)
        {
            if ((end.position - target.position).sqrMagnitude <= targetDistance * targetDistance)
                return;
            var pivotPos = xform.worldToLocalMatrix * end.position;
            var basePos = xform.worldToLocalMatrix * xform.position;
            var targetPos = xform.worldToLocalMatrix * target.position;
            var basePivotVec = (pivotPos - basePos).normalized;
            var baseTargetVec = (targetPos - basePos).normalized;
            var dot = Vector3.Dot(basePivotVec, baseTargetVec);
            var cross = Vector3.Cross(basePivotVec, baseTargetVec);
            // Debug.Log($"xform={xform} dot={dot} cross={cross}", xform);
            if (cross.sqrMagnitude < 1f && 1f - Mathf.Abs(dot) < float.Epsilon)
                return;
            var localRot = xform.localRotation * Quaternion.AngleAxis(Mathf.Acos(dot), cross);
            xform.localRotation = localRot;
            if (xform == start)
                return;
            SolveUp(xform.parent);
        }

        public void SolveFor(Transform xform, int iter)
        {
            if (iter < ChainLength)
            {
                for (int i = 0; i < xform.childCount; i++)
                {
                    SolveFor(xform.GetChild(i), iter + 1);
                }
            }
            var pivotPos = xform.worldToLocalMatrix * end.position;
            var basePos = xform.worldToLocalMatrix * xform.position;
            var targetPos = xform.worldToLocalMatrix * target.position;
            var basePivotVec = (pivotPos - basePos).normalized;
            var baseTargetVec = (targetPos - basePos).normalized;
            var dot = Vector3.Dot(basePivotVec, baseTargetVec);
            var cross = Vector3.Cross(basePivotVec, baseTargetVec);
            Debug.Log($"xform={xform} dot={dot} cross={cross}", xform);
            xform.localRotation *= Quaternion.AngleAxis(Mathf.Acos(dot), cross);
        }

        public void Solve()
        {
            Queue<Transform> stack = new Queue<Transform>();
            stack.Enqueue(transform);
            while (stack.TryDequeue(out var xform))
            {
                for (int i = 0; i < xform.childCount; i++)
                {
                    stack.Enqueue(xform.GetChild(i));
                }
            }
        }
    }
}