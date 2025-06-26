﻿using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.WorldLocking.Core;
using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

public class SpawnManager : MonoBehaviourPun, IMixedRealityPointerHandler
{
    public GameObject cubePrefab; // 생성할 프리팹
    
    private List<int> spawnedObjectIDs = new List<int>(); // 선택한 오브젝트들
    public SplineContainer splineContainer; // 스플라인 연결용

    public Transform spawnRootObject; // 생성할 루트 오브젝트

    public GameObject carPrefab;
    private bool hasCar = false;

    private void Awake()
    {
        CoreServices.InputSystem.RegisterHandler<IMixedRealityPointerHandler>(this);
    }

    public void OnPointerClicked(MixedRealityPointerEventData eventData)
    {

    }
    public void OnPointerDragged(MixedRealityPointerEventData eventData)
    {

    }
    public void OnPointerUp(MixedRealityPointerEventData eventData)
    {

    }

    // 에어탭 했을 때
    public void OnPointerDown(MixedRealityPointerEventData eventData)
    {
        // 예외 처리
        if (!AnchorTransferStatus.isAnchorImported) return;
        if (eventData.Pointer == null || eventData.Pointer.Result == null || eventData.Pointer.Result.Details.Object == null || eventData.Handedness.IsLeft())
        {
            return;
        }


        // 충돌한 오브젝트가 Spatial Awareness일 때
        if (eventData.Pointer.Result.Details.Object.layer.Equals(LayerMask.NameToLayer("Spatial Awareness")))
        {
            Vector3 lockedPosition = eventData.Pointer.Result.Details.Point; // Locked 공간 기준 위치
            Quaternion lockedRotation = Quaternion.LookRotation(eventData.Pointer.Result.Details.Normal);

            // FrozenFromLocked 행렬 얻기
            var manager = WorldLockingManager.GetInstance();
            var frozenFromLocked = manager.FrozenFromLocked;
            Matrix4x4 frozenFromLockedMatrix = Matrix4x4.TRS(frozenFromLocked.position, frozenFromLocked.rotation, Vector3.one);

            // 클릭한 위치를 직접 Frozen 공간으로 변환
            Vector3 frozenPosition = frozenFromLockedMatrix.MultiplyPoint3x4(lockedPosition);

            // 물체 생성 (번갈아가며 생성)
            if (spawnedObjectIDs.Count % 2 == 0 && PhotonNetwork.IsMasterClient || spawnedObjectIDs.Count % 2 == 1 && !PhotonNetwork.IsMasterClient)
            {
                GameObject cube = PhotonNetwork.Instantiate(cubePrefab.name, frozenPosition, lockedRotation);
                cube.transform.parent = spawnRootObject;
                // 물체의 ViewID를 파라미터로 설정
                photonView.RPC("SpawnObject", RpcTarget.AllBuffered, cube.GetComponent<PhotonView>().ViewID);
            }
            else return;

            //// 큐브 크기 조정
            //Vector3 originScale = Vector3.one * 0.1f;
            //cube.transform.localScale = originScale * Mathf.Max(1f, eventData.Pointer.Result.Details.RayDistance);
        }
    }

    // 물체 생성 및 저장
    [PunRPC]
    private void SpawnObject(int spawnedObjectID)
    {
        spawnedObjectIDs.Add(spawnedObjectID); // 물체 저장
        // 생성된 물체가 최대 물체 생성 개수와 동일한 경우
        if (UIManager.instance.maxSpawnObjectCount == spawnedObjectIDs.Count)
        {
            CoreServices.SpatialAwarenessSystem.Disable(); // 공간 비활성화
            photonView.RPC("ViewIDToTransform", RpcTarget.AllBuffered, spawnedObjectIDs.ToArray()); // ViewID를 Transform으로 변환
        }
    }

    // ViewID를 Transform으로 변환
    [PunRPC]
    private void ViewIDToTransform(int[] spawnedObjectIDs)
    {
        List<Transform> selectedObjects = new List<Transform>();
        foreach (int viewID in spawnedObjectIDs)
        {
            PhotonView pv = PhotonView.Find(viewID);
            if (pv != null)
            {
                selectedObjects.Add(pv.transform);
            }
        }

        CreateSpline(selectedObjects); // 스플라인 생성
    }

    // 스플라인 생성
    private void CreateSpline(List<Transform> selectedObjects)
    {
        // 스플라인 컨테이너가 없는 경우
        if (splineContainer == null)
        {
            Debug.LogError("SplineContainer가 연결되어 있지 않습니다.");
            return;
        }

        // 최소 물체 개수가 아닌 경우
        if (selectedObjects.Count <= 2)
        {
            Debug.LogWarning("Spline을 만들려면 최소 3개의 오브젝트가 필요합니다.");
            return;
        }

        var spline = splineContainer.Spline;
        spline.Clear();

        for (int i = 0; i < selectedObjects.Count; i++)
        {
            Transform obj = selectedObjects[i];

            Vector3 prev = selectedObjects[(i - 1 + selectedObjects.Count) % selectedObjects.Count].position;
            Vector3 next = selectedObjects[(i + 1) % selectedObjects.Count].position;

            // 물체의 높이 계산
            float height = 0f;
            var renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                height = renderer.bounds.size.y;
            }
            else
            {
                var collider = obj.GetComponent<Collider>();
                if (collider != null)
                    height = collider.bounds.size.y;
            }

            // 현재 위치 보정: 물체 위로 살짝 띄우기
            Vector3 current = obj.position + new Vector3(0, height * 0.5f + 0.2f, 0);

            Vector3 toPrev = (prev - current).normalized;
            Vector3 toNext = (next - current).normalized;
            Vector3 dir = (toNext - toPrev).normalized;

            float scale = Vector3.Distance(current, next) * 0.25f;
            Vector3 tangentOut = dir * scale;
            Vector3 tangentIn = -tangentOut;

            BezierKnot knot = new BezierKnot(current, tangentIn, tangentOut);
            spline.Add(knot);
        }

        spline.Closed = true;

        // SplineExtrude의 메쉬에 MeshCollider 자동 추가
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            MeshCollider meshCollider = GetComponent<MeshCollider>();
            if (meshCollider == null)
                meshCollider = gameObject.AddComponent<MeshCollider>();

            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = false;
        }

        Debug.Log("Spline 생성 완료");

        // 오브젝트 숨기기
        foreach (var obj in selectedObjects)
        {
            obj.gameObject.SetActive(false);
        }

        StartCoroutine(WaitAndSpawnCar());
    }

    private IEnumerator WaitAndSpawnCar()
    {
        yield return new WaitForEndOfFrame();
        
        if (!hasCar)
        {
            hasCar = true;
            SpawnCarOnSpline();
        }
    }

    void SpawnCarOnSpline()
    {
        if (carPrefab == null || splineContainer == null)
        {
            Debug.LogWarning("CarPrefab, SplineContainer, 또는 MiniCamera가 연결되지 않았습니다.");
            return;
        }

        // 스플라인 시작 위치 계산
        splineContainer.Spline.Evaluate(0f, out float3 pos, out float3 tangent, out float3 up);
        Vector3 splinePos = (Vector3)pos;

        // 자동차 생성
        GameObject spawnedCar = PhotonNetwork.Instantiate(carPrefab.name, splinePos + Vector3.up * 0.5f, Quaternion.LookRotation(tangent, up));
        spawnedCar.transform.parent = spawnRootObject;

        var mover = spawnedCar.GetComponent<CarMove>();
        if (mover != null)
        {
            mover.progress = 0f;
            mover.splineContainer = splineContainer;
        }

        this.enabled = false; // 스크립트 비활성화
    }
}
