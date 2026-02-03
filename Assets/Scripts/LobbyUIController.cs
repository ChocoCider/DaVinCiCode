using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUIController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Text myNameText;
    [SerializeField] private Text statusText;

    [Header("Create Room")]
    [SerializeField] private Button createRoomButton;

    [Header("Join Room")]
    [SerializeField] private InputField roomIdInput;
    [SerializeField] private Button joinRoomButton;

    [Header("Scenes")]
    [SerializeField] private string loginSceneName = SceneNames.Login;
    [SerializeField] private string roomSceneName = SceneNames.Room;

    [Header("Room Settings")]
    [SerializeField] private int maxPlayers = 3;

    private void Awake()
    {
        if (createRoomButton) createRoomButton.onClick.AddListener(() => _ = CreateRoom());
        if (joinRoomButton) joinRoomButton.onClick.AddListener(() => _ = JoinRoom(roomIdInput.text.Trim()));
    }

    private async void Start()
    {
        while (FirebaseAuthService.Instance == null || !FirebaseAuthService.Instance.Ready)
            await Task.Yield();

        if (FirebaseAuthService.Instance.Auth.CurrentUser == null)
        {
            SceneLoader.LoadIfNotCurrent(loginSceneName);
            return;
        }

        RefreshMyName();
        SetStatus("로비 진입 완료");
    }

    private void RefreshMyName()
    {
        if (!myNameText) return;
        var n = FirebaseAuthService.Instance.MyDisplayName;
        myNameText.text = string.IsNullOrEmpty(n) ? "Player" : n;
    }

    private async Task CreateRoom()
    {
        try
        {
            SetStatus("방 생성 중...");

            var db = FirebaseAuthService.Instance.Db;
            var uid = FirebaseAuthService.Instance.MyUid;
            var name = FirebaseAuthService.Instance.MyDisplayName;

            var roomRef = db.Collection("rooms").Document();
            string roomId = roomRef.Id;

            var roomData = new Dictionary<string, object>
            {
                { "status", "lobby" },
                { "hostUid", uid },
                { "maxPlayers", maxPlayers },
                { "playerCount", 1 },
                { "createdAt", FieldValue.ServerTimestamp },
                { "updatedAt", FieldValue.ServerTimestamp }
            };

            var playerData = new Dictionary<string, object>
            {
                { "uid", uid },
                { "displayName", name },
                { "seat", 0 },
                { "ready", false },
                { "joinedAt", FieldValue.ServerTimestamp }
            };

            await db.RunTransactionAsync(async tx =>
            {
                tx.Set(roomRef, roomData);
                tx.Set(roomRef.Collection("players").Document(uid), playerData);
                return 0;
            });

            RoomRuntime.SetRoom(roomId);
            SetStatus($"방 생성 완료: {roomId}");

            // RoomScene으로 가야 정상
            SceneLoader.LoadIfNotCurrent(roomSceneName);
        }
        catch (Exception e)
        {
            SetStatus($"방 생성 실패: {e.Message}");
        }
    }

    private async Task JoinRoom(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            SetStatus("roomId를 입력하세요.");
            return;
        }

        roomId = roomId.Trim();

        try
        {
            SetStatus("방 입장 중...");

            var db = FirebaseAuthService.Instance.Db;
            var uid = FirebaseAuthService.Instance.MyUid;
            var name = FirebaseAuthService.Instance.MyDisplayName;

            var roomRef = db.Collection("rooms").Document(roomId);
            var myPlayerRef = roomRef.Collection("players").Document(uid);

            await db.RunTransactionAsync(async tx =>
            {
                var roomSnap = await tx.GetSnapshotAsync(roomRef);
                if (!roomSnap.Exists) throw new Exception("방이 존재하지 않습니다.");

                string status = roomSnap.ContainsField("status") ? roomSnap.GetValue<string>("status") : "";
                if (status != "lobby") throw new Exception("이미 게임이 시작된 방입니다.");

                long playerCount = roomSnap.GetValue<long>("playerCount");
                long maxP = roomSnap.GetValue<long>("maxPlayers");
                if (playerCount >= maxP) throw new Exception("방이 가득 찼습니다.");

                var mySnap = await tx.GetSnapshotAsync(myPlayerRef);
                if (mySnap.Exists) return 0;

                int seat = (int)playerCount; // MVP

                tx.Set(myPlayerRef, new Dictionary<string, object>
                {
                    { "uid", uid },
                    { "displayName", name },
                    { "seat", seat },
                    { "ready", false },
                    { "joinedAt", FieldValue.ServerTimestamp }
                });

                tx.Update(roomRef, new Dictionary<string, object>
                {
                    { "playerCount", playerCount + 1 },
                    { "updatedAt", FieldValue.ServerTimestamp }
                });

                return 0;
            });

            RoomRuntime.SetRoom(roomId);
            SetStatus($"입장 완료: {roomId}");
            SceneLoader.LoadIfNotCurrent(roomSceneName);
        }
        catch (Exception e)
        {
            SetStatus($"방 입장 실패: {e.Message}");
        }
    }

    private void SetStatus(string msg)
    {
        if (statusText) statusText.text = msg;
        Debug.Log(msg);
    }
}