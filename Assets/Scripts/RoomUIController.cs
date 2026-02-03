using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RoomUIController : MonoBehaviour
{
    [Header("TopBar")]
    [SerializeField] private Text roomIdText;
    [SerializeField] private Text statusText;
    [SerializeField] private Button copyButton; // 선택

    [Header("Seats (3 fixed)")]
    [SerializeField] private PlayerSlotView seatBottom;
    [SerializeField] private PlayerSlotView seatLeft;
    [SerializeField] private PlayerSlotView seatRight;

    [Header("Buttons")]
    [SerializeField] private Button readyButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private Button startButton;


    // 외부(Firebase/RoomManager)가 구독해서 처리
    public event Action OnReadyClicked;
    public event Action OnLeaveClicked;
    public event Action OnStartClicked;

    private void Awake()
    {
        if (readyButton) readyButton.onClick.AddListener(() => OnReadyClicked?.Invoke());
        if (leaveButton) leaveButton.onClick.AddListener(() => OnLeaveClicked?.Invoke());
        if (startButton) startButton.onClick.AddListener(() => OnStartClicked?.Invoke());

        if (copyButton)
            copyButton.onClick.AddListener(CopyRoomIdToClipboard);
    }

    public void SetRoomHeader(string roomId, string status, int playerCount, int maxPlayers)
    {
        if (roomIdText) roomIdText.text = roomId ?? "";
        if (statusText) statusText.text = $"{status}  {playerCount}/{maxPlayers}";
    }

    /// <summary>
    /// 내 uid와 내 seat를 받아서 "내 화면 기준"으로 좌석 배치한다.
    /// </summary>
    public void RenderSeats(
        List<PlayerVM> players,
        string myUid,
        int mySeat,
        string hostUid,
        int maxPlayers)
    {
        // 기본: 비우기
        seatBottom?.SetEmpty();
        seatLeft?.SetEmpty();
        seatRight?.SetEmpty();

        // player -> UI slot 배치
        foreach (var p in players)
        {
            int rel = SeatMapper.ToRelativeSeat(p.seat, mySeat, maxPlayers);
            SeatAnchor anchor = SeatMapper.ToAnchor(rel, maxPlayers);

            bool isHost = p.uid == hostUid;

            switch (anchor)
            {
                case SeatAnchor.Bottom:
                    seatBottom?.SetPlayer(p.displayName, p.ready, isHost);
                    break;
                case SeatAnchor.Left:
                    seatLeft?.SetPlayer(p.displayName, p.ready, isHost);
                    break;
                case SeatAnchor.Right:
                    seatRight?.SetPlayer(p.displayName, p.ready, isHost);
                    break;
                default:
                    // 3인 고정에선 Top 없음
                    break;
            }
        }
    }

    public void SetStartInteractable(bool canStart)
    {
        if (startButton) startButton.interactable = canStart;
    }

    public void SetReadyButtonLabel(bool isReady)
    {
        // 버튼 텍스트 바꾸고 싶으면 Button 아래 Text를 찾아 변경
        if (!readyButton) return;
        var t = readyButton.GetComponentInChildren<Text>();
        if (t) t.text = isReady ? "Ready 해제" : "Ready";
    }

    public void CopyRoomIdToClipboard()
    {
        if (roomIdText == null) return;

        string roomId = roomIdText.text;
        if (string.IsNullOrEmpty(roomId)) return;

        GUIUtility.systemCopyBuffer = roomId;
        Debug.Log($"[RoomUI] RoomId copied to clipboard: {roomId}");
    }
}