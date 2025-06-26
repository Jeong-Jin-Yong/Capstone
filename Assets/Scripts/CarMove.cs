using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Utilities;
using Photon.Pun;

[RequireComponent(typeof(Rigidbody))]
public class CarMove : MonoBehaviourPunCallbacks
{
    public SplineContainer splineContainer;
    public float speed = 0.2f;
    public float reverseSpeed = 1f;
    public float dashMultiplier = 2f;
    public float turnSpeed = 100f;
    public float respawnHeightOffset = 5f;  // 리스폰 기준 위치 (스플라인 위치 위로 일정 오프셋 추가)
    public float respawnLift = 0.1f;        // 도로 위로 띄우는 높이
    private bool isFalling = false;
    private float ignoreExitTime = 0f;
    private float exitIgnoreDuration = 0.5f; // 리스폰 직후 0.5초간 Exit 무시

    [HideInInspector] public float progress = 0f;

    private Rigidbody rb;
    private float lastGroundedProgress = 0f;
    private Vector3 lastGroundedPosition = Vector3.zero;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (splineContainer == null || rb == null) return;

        if (photonView.IsMine)
        {
            HandMove();
        }
        else return;
        //KeyMove();    
    }

    void OnCollisionStay(Collision collision)
    {
        if (collision.gameObject.CompareTag("spline"))
        {
            lastGroundedProgress = progress;
            lastGroundedPosition = transform.position;
            isFalling = false;
        }
    }

    void OnCollisionExit(Collision collision)
    {
        // 리스폰 직후엔 무시
        if (Time.time < ignoreExitTime) return;

        if (collision.gameObject.CompareTag("spline") && !isFalling)
        {
            Debug.Log(lastGroundedPosition);
            Debug.Log(lastGroundedProgress);
            isFalling = true;
            StartCoroutine(RespawnAfterDelay(1f));
        }
    }

    //void OnCollisionEnter(Collision collision)
    //{
    //    if (collision.gameObject.CompareTag("spline"))
    //    {
    //        // OnCollisionEnter에서 코루틴 호출
    //        StartCoroutine(HandleCollision());
    //    }
    //}

    //private IEnumerator HandleCollision()
    //{
    //    // Rigidbody를 잠시 비활성화
    //    rb.useGravity = false;

    //    // 1초 대기
    //    yield return new WaitForSeconds(1f);

    //    // Rigidbody를 다시 활성화
    //    rb.useGravity = true;
    //}

    private IEnumerator RespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        RespawnAtCurrentProgress();
    }

    void RespawnAtCurrentProgress()
    {
        // 마지막 도로 위 위치에서 스플라인 평가
        splineContainer.Spline.Evaluate(lastGroundedProgress, out float3 pos, out float3 tangent, out float3 up);

        // 리스폰 위치 계산: 스플라인 중심에서 위 방향으로 살짝 띄움
        Vector3 finalPos = (Vector3)pos + ((Vector3)up).normalized * respawnLift;

        // 위치 및 회전 설정
        rb.position = finalPos;
        rb.rotation = Quaternion.LookRotation((Vector3)tangent, (Vector3)up);

        // 물리 상태 초기화
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // 진행도도 되돌림
        progress = lastGroundedProgress;

        // 약간의 도로 방향 누르기 힘
        StartCoroutine(PushDownAlongNormal((Vector3)up));

   
        ignoreExitTime = Time.time + exitIgnoreDuration;


        Debug.Log($"[리스폰 - 고정] t={lastGroundedProgress:F3}, 위치: {finalPos}");
    }



    IEnumerator PushDownAlongNormal(Vector3 groundNormal)
    {
        float duration = 1f; // 지속 시간
        float timer = 0f;

        while (timer < duration)
        {
            rb.AddForce(-groundNormal * 100f, ForceMode.Acceleration);
            timer += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
    }

    float CalculateProgressFromPosition(Vector3 position)
    {
        int sampleCount = 300;
        float closestT = 0f;
        float closestDist = Mathf.Infinity;

        for (int i = 0; i <= sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            splineContainer.Spline.Evaluate(t, out float3 samplePos, out _, out _);

            float dist = Vector3.Distance(position, (Vector3)samplePos);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestT = t;
            }
        }

        return closestT;
    }

    void HandMove()
    {
        // 오른손이 가리키는 방향으로 이동하는 오브젝트
        if (HandJointUtils.TryGetJointPose(TrackedHandJoint.IndexTip, Handedness.Right, out MixedRealityPose rightHandPos))
        {
            MixedRealityPose handPos = rightHandPos; // 오른손 위치

            Vector3 moveDir = handPos.Forward; // 이동 방향
            //moveDir.y = 0; // y축으로 이동 금지
            moveDir.Normalize();
            rb.linearVelocity = moveDir * speed;
        }
        else
        {
            rb.linearVelocity = Vector3.zero;
        }
    }

    void KeyMove()
    {
        // 현재 스플라인 위치 계산 (한 번만 호출)
        splineContainer.Spline.Evaluate(progress, out float3 pos, out float3 tangent, out float3 up);


        float vInput = Input.GetAxis("Vertical");   // W/S
        float hInput = Input.GetAxis("Horizontal"); // A/D

        // 대쉬 포함한 현재 속도 계산
        float currentSpeed = vInput > 0 ? speed : reverseSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            currentSpeed *= dashMultiplier;
        }

        progress = CalculateProgressFromPosition(transform.position);

        if (progress >= 1f) progress -= 1f;

        // 좌우 회전
        float turn = hInput * turnSpeed * Time.fixedDeltaTime;
        transform.Rotate(0, turn, 0);

        Vector3 moveDirection;
        Vector3 rayOrigin = transform.position + Vector3.up * 0.1f; // 바닥 체크를 위한 Ray 시작 위치

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit slopeHit, 2f)) // 도로가 있는지 확인
        {
            Vector3 groundNormal = slopeHit.normal; // 도로의 기울기 벡터
            moveDirection = Vector3.ProjectOnPlane(transform.forward, groundNormal).normalized;

            // 회전도 도로 경사에 맞춰 보간
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, groundNormal);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.fixedDeltaTime * 5f);

            float angle = Vector3.Angle(transform.up, groundNormal);
            if (angle > 60f)
            {
                if (!isFalling)
                {
                    isFalling = true;
                    StartCoroutine(RespawnAfterDelay(1f));
                }
            }
            //Debug.Log("기울기 각도: " + angle);
        }
        else
        {
            moveDirection = transform.forward; // 공중일 경우엔 원래 방향
        }


        // velocity를 사용하여 속도 설정 (AddForce 없이 직접 설정)
        Vector3 forward = transform.forward;
        if (vInput == 0)
        {
            rb.linearVelocity = Vector3.zero;  // 이동하지 않으면 속도를 0으로 설정

        }
        else if (vInput > 0)
        {
            rb.linearVelocity = forward * currentSpeed;  // 일정 속도로 이동
        }
        else
        {
            rb.linearVelocity = -forward * currentSpeed;  // 일정 속도로 이동
        }
        //rb.AddForce(forward * (vInput * currentSpeed * 10f));

        // progress가 1을 넘으면 0으로 돌아가도록 설정
        if (progress >= 1f)
        {
            progress -= 1f;  // progress가 1를 넘으면 0부터 다시 시작
        }

        // 스플라인 방향 디버그 표시
        Debug.DrawLine(transform.position, transform.position + (Vector3)tangent, Color.yellow);
    }
}