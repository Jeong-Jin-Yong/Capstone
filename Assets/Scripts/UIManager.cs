using Microsoft.MixedReality.Toolkit;
using UnityEngine;
using Photon.Pun;

public class UIManager : MonoBehaviourPun
{
    public static UIManager instance { get; private set; } // 싱글턴

    public GameObject playerWatingUI; // 플레이어 대기 UI
    public GameObject objectSelectUI; // 오브젝트 개수 선택 UI
    public GameObject clientWatingUI; // 마스터가 오브젝트 개수 선택 전까지 클라이언트 대기 UI

    public int maxSpawnObjectCount { get; private set; } // 최대 물체 선택 개수

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(this.gameObject);
            return;
        }

        instance = this;
    }

    private void Start()
    {
        CoreServices.SpatialAwarenessSystem.Disable();
    }

    // 마스터가 오브젝트 선택할 개수 정하기
    public void OnMasterSelectObject(int count)
    {
        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC("StartSelectedObject", RpcTarget.All, count);
        }
    }

    // 물체 선택 시작
    [PunRPC]
    public void StartSelectedObject(int count)
    {
        maxSpawnObjectCount = count;
        CoreServices.SpatialAwarenessSystem.Enable();
        ObjectSelectUI(false);
        ClientWatingUI(false);
    }

    // 플레이어 대기 UI 활성화 여부
    public void PlayerWatingUI(bool active)
    {
        playerWatingUI.SetActive(active);
    }
    // 오브젝트 선택 UI 활성화 여부
    public void ObjectSelectUI(bool active)
    {
        objectSelectUI.SetActive(active);
    }
    // 마스터가 오브젝트 개수 선택 전까지 클라이언트 대기 UI 활성화 여부
    public void ClientWatingUI(bool active)
    {
        clientWatingUI.SetActive(active);
    }
}
