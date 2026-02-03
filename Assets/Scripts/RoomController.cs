using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

public class RoomController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RoomUIController roomUI;

    [Header("Scenes")]
    [SerializeField] private string loginSceneName = "LoginScene";
    [SerializeField] private string lobbySceneName = "LobbyScene";
    [SerializeField] private string inGameSceneName = "InGameScene";

    [Header("Room Settings")]
    [SerializeField] private int maxPlayers = 3; // MVP: 3인 고정

    [SerializeField] private int cardsPerPlayer = 4;

    private string _roomId;
    private string _myUid;
    private string _hostUid;
    private int _mySeat = 0;

    private bool _myReady = false;
    private int _playerCount = 0;
    private string _roomStatus = "lobby";

    private ListenerRegistration _roomListener;
    private ListenerRegistration _playersListener;

    // 최신 플레이어 스냅샷 보관
    private readonly List<PlayerVM> _players = new();

    // ✅ Firestore 콜백에서 Unity UI 건드리지 않기 위한 플래그
    private volatile bool _dirtyUi = false;
    private volatile bool _shouldGoInGame = false;
    private volatile bool _shouldGoLobby = false;

    private void Awake()
    {
        if (!roomUI) Debug.LogError("RoomUIController is not assigned.");

        if (roomUI != null)
        {
            roomUI.OnReadyClicked += HandleReadyClicked;
            roomUI.OnLeaveClicked += HandleLeaveClicked;
            roomUI.OnStartClicked += HandleStartClicked;
        }
    }

    private async void Start()
    {
        // Firebase 준비 대기
        while (FirebaseAuthService.Instance == null || !FirebaseAuthService.Instance.Ready)
            await Task.Yield();

        if (FirebaseAuthService.Instance.Auth.CurrentUser == null)
        {
            SceneLoader.LoadIfNotCurrent(loginSceneName);
            return;
        }

        _myUid = FirebaseAuthService.Instance.MyUid;

        _roomId = RoomRuntime.CurrentRoomId;
        if (string.IsNullOrWhiteSpace(_roomId))
        {
            Debug.LogError("RoomRuntime.CurrentRoomId is empty. Returning to Lobby.");
            SceneLoader.LoadIfNotCurrent(lobbySceneName);
            return;
        }

        StartListeners(_roomId);
    }

    private void Update()
    {
        // ✅ UI 업데이트는 반드시 메인스레드(Update)에서만
        if (_dirtyUi)
        {
            _dirtyUi = false;
            RenderAll();
        }

        if (_shouldGoLobby)
        {
            _shouldGoLobby = false;
            // roomId는 떠날 때 비워주기
            RoomRuntime.Clear();
            SceneLoader.LoadIfNotCurrent(lobbySceneName);
        }

        if (_shouldGoInGame)
        {
            _shouldGoInGame = false;
            SceneLoader.LoadIfNotCurrent(inGameSceneName);
        }
    }

    private void OnDestroy()
    {
        StopListeners();

        if (roomUI != null)
        {
            roomUI.OnReadyClicked -= HandleReadyClicked;
            roomUI.OnLeaveClicked -= HandleLeaveClicked;
            roomUI.OnStartClicked -= HandleStartClicked;
        }
    }

    private void StartListeners(string roomId)
    {
        StopListeners();

        var db = FirebaseAuthService.Instance.Db;
        var roomRef = db.Collection("rooms").Document(roomId);

        // rooms/{roomId} 리스너: 상태/호스트/인원
        _roomListener = roomRef.Listen(snap =>
        {
            try
            {
                if (!snap.Exists)
                {
                    Debug.LogWarning("Room doc missing -> back to lobby");
                    _shouldGoLobby = true;
                    return;
                }

                _hostUid = snap.ContainsField("hostUid") ? snap.GetValue<string>("hostUid") : "";
                _roomStatus = snap.ContainsField("status") ? snap.GetValue<string>("status") : "lobby";
                _playerCount = snap.ContainsField("playerCount") ? (int)snap.GetValue<long>("playerCount") : 0;
                maxPlayers = snap.ContainsField("maxPlayers") ? (int)snap.GetValue<long>("maxPlayers") : maxPlayers;

                // playing이면 다음 프레임에 이동
                if (_roomStatus == "playing") _shouldGoInGame = true;

                // ✅ UI는 Update에서만 갱신
                _dirtyUi = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RoomListener] Exception: {e}");
                // 심각하면 로비로 보내서 회복
                _shouldGoLobby = true;
            }
        });

        // rooms/{roomId}/players 리스너: 좌석/닉/ready
        _playersListener = roomRef.Collection("players").Listen(snap =>
        {
            try
            {
                _players.Clear();

                foreach (var doc in snap.Documents)
                {
                    var d = doc.ToDictionary();

                    var p = new PlayerVM
                    {
                        uid = doc.Id,
                        displayName = d.TryGetValue("displayName", out var n) ? n?.ToString() : "Player",
                        seat = d.TryGetValue("seat", out var s) ? Convert.ToInt32(s) : 0,
                        ready = d.TryGetValue("ready", out var r) && Convert.ToBoolean(r)
                    };

                    _players.Add(p);

                    if (p.uid == _myUid)
                    {
                        _mySeat = p.seat;
                        _myReady = p.ready;
                    }
                }

                // seat 정렬(디버깅 편함)
                _players.Sort((a, b) => a.seat.CompareTo(b.seat));

                // playing이면 다음 프레임에 이동(중복 방지)
                if (_roomStatus == "playing") _shouldGoInGame = true;

                // ✅ UI는 Update에서만 갱신
                _dirtyUi = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayersListener] Exception: {e}");
                _shouldGoLobby = true;
            }
        });

        Debug.Log($"[RoomController] Listening roomId={roomId}");
    }

    private void StopListeners()
    {
        _roomListener?.Stop();
        _roomListener = null;

        _playersListener?.Stop();
        _playersListener = null;
    }

    private void RenderAll()
    {
        if (roomUI == null) return;

        roomUI.SetRoomHeader(_roomId, _roomStatus, _playerCount, maxPlayers);
        roomUI.RenderSeats(_players, _myUid, _mySeat, _hostUid, maxPlayers);

        // Start 조건
        bool isHost = _myUid == _hostUid;
        bool full = _players.Count >= maxPlayers; // MVP: 3명 꽉 차야 시작
        bool allReady = _players.Count > 0 && _players.All(p => p.ready);

        bool canStart = isHost && full && allReady && _roomStatus == "lobby";
        roomUI.SetStartInteractable(canStart);
        roomUI.SetReadyButtonLabel(_myReady);
    }

    // ---------------- Buttons ----------------

    private async void HandleReadyClicked()
    {
        try
        {
            var db = FirebaseAuthService.Instance.Db;
            var myRef = db.Collection("rooms").Document(_roomId).Collection("players").Document(_myUid);

            // 토글
            await myRef.UpdateAsync(new Dictionary<string, object>
            {
                { "ready", !_myReady }
            });
        }
        catch (Exception e)
        {
            Debug.LogError($"Ready toggle failed: {e}");
        }
    }

    private async void HandleLeaveClicked()
    {
        await LeaveRoom();
    }

    private async Task LeaveRoom()
    {
        try
        {
            var db = FirebaseAuthService.Instance.Db;
            var roomRef = db.Collection("rooms").Document(_roomId);
            var myRef = roomRef.Collection("players").Document(_myUid);

            await db.RunTransactionAsync(async tx =>
            {
                var roomSnap = await tx.GetSnapshotAsync(roomRef);
                if (!roomSnap.Exists) return 0;

                long playerCount = roomSnap.ContainsField("playerCount") ? roomSnap.GetValue<long>("playerCount") : 0;

                var mySnap = await tx.GetSnapshotAsync(myRef);
                if (mySnap.Exists)
                {
                    tx.Delete(myRef);
                    tx.Update(roomRef, new Dictionary<string, object>
                    {
                        { "playerCount", Math.Max(0, playerCount - 1) },
                        { "updatedAt", FieldValue.ServerTimestamp }
                    });
                }

                return 0;
            });
        }
        catch (Exception e)
        {
            Debug.LogError($"LeaveRoom failed: {e}");
        }
        finally
        {
            _shouldGoLobby = true;
        }
    }

    private async void HandleStartClicked()
    {
        if (_myUid != _hostUid) return;
        if (_roomStatus != "lobby") return;

        bool full = _players.Count >= maxPlayers;
        bool allReady = _players.Count > 0 && _players.All(p => p.ready);
        if (!full || !allReady) return;

        try
        {
            var db = FirebaseAuthService.Instance.Db;
            var roomRef = db.Collection("rooms").Document(_roomId);

            await InitGameAsHostAsync(roomRef);

            // 이제 status=playing은 InitGameAsHostAsync에서 같이 처리됨.
            // listeners가 감지해서 전원 인게임으로 이동
        }
        catch (Exception e)
        {
            Debug.LogError($"StartGame(init) failed: {e}");
        }
    }

    private async Task InitGameAsHostAsync(DocumentReference roomRef)
    {
        var db = FirebaseAuthService.Instance.Db;

        // seat 순 정렬 + 3명 검증
        var ordered = _players.OrderBy(p => p.seat).ToList();
        if (ordered.Count != 3) throw new Exception("MVP는 3인 고정입니다. (players != 3)");

        // seatToUid 배열 (index=seat)
        var seatToUid = new List<string> { ordered[0].uid, ordered[1].uid, ordered[2].uid };

        // 덱 생성: B00~B11, W00~W11 (총 24장)
        var deck = new List<string>();
        for (int n = 0; n <= 11; n++)
        {
            deck.Add(CardIdUtil.ToCardId("black", n));
            deck.Add(CardIdUtil.ToCardId("white", n));
        }

        // 셔플
        var rng = new System.Random();
        deck = deck.OrderBy(_ => rng.Next()).ToList();

        var batch = db.StartBatch();

        // 각 플레이어 hands + publicCards 세팅
        foreach (var p in ordered)
        {
            var handRef = roomRef.Collection("hands").Document(p.uid);
            var playerRef = roomRef.Collection("players").Document(p.uid);

            // 카드 분배
            var myCards = deck.Take(cardsPerPlayer).ToList();
            deck.RemoveRange(0, cardsPerPlayer);

            var revealed = Enumerable.Repeat(false, cardsPerPlayer).ToList();

            // hands/{uid}
            batch.Set(handRef, new Dictionary<string, object>
        {
            { "seat", p.seat },
            { "cardIds", myCards },
            { "revealed", revealed },
            { "updatedAt", FieldValue.ServerTimestamp }
        });

            // players/{uid}.publicCards (색만 공개)
            var publicCards = new List<Dictionary<string, object>>();
            for (int i = 0; i < myCards.Count; i++)
            {
                CardIdUtil.Parse(myCards[i], out var color, out _);
                publicCards.Add(new Dictionary<string, object>
            {
                { "idx", i },
                { "color", color },
                { "revealed", false },
                { "cardId", null }
            });
            }

            // ⚠️ 너의 rules가 "호스트는 publicCards만 변경 가능"이라서 Update로 publicCards만 바꿔야 안전
            batch.Update(playerRef, new Dictionary<string, object>
        {
            { "publicCards", publicCards }
        });
        }

        // game/state 생성
        var stateRef = roomRef.Collection("game").Document("state");
        batch.Set(stateRef, new Dictionary<string, object>
    {
        { "phase", GamePhases.Draw },
        { "turnSeat", 0 },
        { "turnUid", seatToUid[0] },
        { "seatToUid", seatToUid },     // ★ 2번의 핵심
        { "deck", deck },
        { "deckCount", deck.Count },
        { "lastLog", "Game started." },
        { "updatedAt", FieldValue.ServerTimestamp }
    });

        // room status를 playing으로 변경 (init과 같은 커밋에 묶음)
        batch.Update(roomRef, new Dictionary<string, object>
    {
        { "status", "playing" },
        { "updatedAt", FieldValue.ServerTimestamp }
    });

        await batch.CommitAsync();
    }
}