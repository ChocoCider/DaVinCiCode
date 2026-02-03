using Firebase.Firestore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class InGameController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameUIController ui;

    [Header("MVP Settings")]
    [SerializeField] private int cardsPerPlayer = 4;

    private ListenerRegistration _playersListener;
    private ListenerRegistration _gameListener;
    private ListenerRegistration _myHandListener;
    private ListenerRegistration _roomListener;

    private GameContext _ctx;
    private GameRepository _repo;
    private GameStateMachine _sm;

    // ---- main-thread flags ----
    private volatile bool _dirty;
    private volatile bool _requestReturnToRoom;

    // host jobs
    private bool _hostResetStarted = false;     // ✅ 리셋 1회 가드 (문제2)
    private bool _hostTurnFixInFlight = false;  // ✅ 턴 보정 중복 방지 (문제1)

    private bool _returnFlowStarted;

    // room cached
    private string _roomStatus = "playing";
    private string _hostUid = null;

    private async void Start()
    {
        while (FirebaseAuthService.Instance == null || !FirebaseAuthService.Instance.Ready)
            await Task.Yield();

        if (ui == null)
        {
            Debug.LogError("[InGameController] GameUIController not assigned.");
            return;
        }

        _ctx = new GameContext
        {
            Db = FirebaseAuthService.Instance.Db,
            MyUid = FirebaseAuthService.Instance.MyUid,
            RoomId = RoomRuntime.CurrentRoomId,
            UI = ui
        };

        _repo = new GameRepository(_ctx);
        _sm = new GameStateMachine(_ctx, _repo);

        HookUI();
        StartListeners();
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private void Update()
    {
        if (_dirty)
        {
            _dirty = false;

            Render();
            _sm.SyncToPhase(_ctx.Phase);

            // ------------------------------
            // ✅ 문제2: 게임 끝나면 host가 리셋+닫기 1회 수행
            // ------------------------------
            if (_ctx.Phase == GamePhases.Finished && IsHostCached() && !_hostResetStarted)
            {
                _hostResetStarted = true;
                _ = ResetAndCloseMatchAsHostAsync(); // fire-and-forget (내부 try/catch)
            }

            // ------------------------------
            // ✅ 문제1: "현재 턴 플레이어"가 OUT이면 host가 턴 자동 보정
            // - phase가 finished가 아닐 때만
            // - turnUid가 존재하고, 그 uid가 eliminated=true면
            // ------------------------------
            if (_ctx.Phase != GamePhases.Finished && IsHostCached())
            {
                if (!_hostTurnFixInFlight && IsTurnPlayerEliminatedLocally())
                {
                    _hostTurnFixInFlight = true;
                    _ = FixTurnIfCurrentTurnPlayerEliminatedAsync();
                }
            }
        }

        // ✅ 씬 이동은 room.status로만 (핑퐁 방지)
        if (_requestReturnToRoom && !_returnFlowStarted)
        {
            _returnFlowStarted = true;
            _ = ReturnToRoomFlowAsync();
        }
    }

    // ------------------------------------------------------------
    // UI hook
    // ------------------------------------------------------------
    private void HookUI()
    {
        ui.OnDrawClicked += _sm.OnDrawClicked;
        ui.OnGuessClicked += _sm.OnGuessClicked;
        ui.OnGuessSubmit += _sm.OnGuessSubmit;

        ui.OnOpponentCardClicked += _sm.OnOpponentCardClicked;
        ui.OnContinueGuessClicked += _sm.OnContinueGuess;
        ui.OnEndTurnClicked += _sm.OnEndTurn;
    }

    private void UnhookUI()
    {
        if (ui == null) return;

        ui.OnDrawClicked -= _sm.OnDrawClicked;
        ui.OnGuessClicked -= _sm.OnGuessClicked;
        ui.OnGuessSubmit -= _sm.OnGuessSubmit;

        ui.OnOpponentCardClicked -= _sm.OnOpponentCardClicked;
        ui.OnContinueGuessClicked -= _sm.OnContinueGuess;
        ui.OnEndTurnClicked -= _sm.OnEndTurn;
    }

    private void Cleanup()
    {
        UnhookUI();

        _playersListener?.Stop(); _playersListener = null;
        _gameListener?.Stop(); _gameListener = null;
        _myHandListener?.Stop(); _myHandListener = null;
        _roomListener?.Stop(); _roomListener = null;
    }

    // ------------------------------------------------------------
    // Listeners
    // ------------------------------------------------------------
    private void StartListeners()
    {
        // room: status/hostUid
        _roomListener = _ctx.RoomRef.Listen(snap =>
        {
            try
            {
                if (!snap.Exists) return;

                var d = snap.ToDictionary();
                _roomStatus = d.TryGetValue("status", out var s) ? s?.ToString() : "lobby";
                _hostUid = d.TryGetValue("hostUid", out var h) ? h?.ToString() : null;

                // ✅ playing이 아니면 전원 복귀 (플래그만)
                if (_roomStatus != "playing")
                    _requestReturnToRoom = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RoomListener@InGame] {e}");
            }
        });

        // players
        _playersListener = _ctx.RoomRef.Collection("players").Listen(snap =>
        {
            try
            {
                _ctx.PlayersByUid.Clear();
                _ctx.UidBySeat.Clear();

                foreach (var doc in snap.Documents)
                {
                    var d = doc.ToDictionary();
                    var p = new PlayerVM
                    {
                        uid = doc.Id,
                        displayName = d.TryGetValue("displayName", out var n) ? n?.ToString() : "Player",
                        seat = d.TryGetValue("seat", out var s) ? Convert.ToInt32(s) : 0,
                        ready = d.TryGetValue("ready", out var r) && Convert.ToBoolean(r),
                        eliminated = d.TryGetValue("eliminated", out var e) && Convert.ToBoolean(e),
                        publicCards = ReadPublicCards(d)
                    };

                    _ctx.PlayersByUid[p.uid] = p;
                    _ctx.UidBySeat[p.seat] = p.uid;

                    if (p.uid == _ctx.MyUid) _ctx.MySeat = p.seat;
                }

                _dirty = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayersListener] {e}");
            }
        });

        // game
        _gameListener = _ctx.GameStateRef.Listen(snap =>
        {
            try
            {
                if (!snap.Exists) return;

                var d = snap.ToDictionary();
                _ctx.Phase = d.TryGetValue("phase", out var ph) ? ph?.ToString() : GamePhases.Draw;
                _ctx.TurnSeat = d.TryGetValue("turnSeat", out var ts) ? Convert.ToInt32(ts) : 0;
                _ctx.DeckCount = d.TryGetValue("deckCount", out var dc) ? Convert.ToInt32(dc) : 0;
                _ctx.LastLog = d.TryGetValue("lastLog", out var ll) ? ll?.ToString() : "";
                _ctx.TurnUid = d.TryGetValue("turnUid", out var tu) ? tu?.ToString() : null;
                _ctx.WinnerUid = d.TryGetValue("winnerUid", out var wu) ? wu?.ToString() : null;

                _ctx.SeatToUid = new List<string>();
                if (d.TryGetValue("seatToUid", out var raw) && raw is System.Collections.IEnumerable arr)
                    foreach (var x in arr) _ctx.SeatToUid.Add(x?.ToString() ?? "");

                _ctx.Deck = ReadStringList(d, "deck");

                _dirty = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameListener] {e}");
            }
        });

        // my hand
        _myHandListener = _ctx.HandRef(_ctx.MyUid).Listen(snap =>
        {
            try
            {
                if (!snap.Exists) return;
                _ctx.MyHand = ReadHand(snap.ToDictionary());
                _dirty = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MyHandListener] {e}");
            }
        });
    }

    // ------------------------------------------------------------
    // ✅ 문제1: host가 turnUid가 OUT이면 턴을 다음 생존자에게 넘김
    // ------------------------------------------------------------
    private bool IsTurnPlayerEliminatedLocally()
    {
        if (string.IsNullOrEmpty(_ctx.TurnUid)) return false;
        if (_ctx.PlayersByUid.TryGetValue(_ctx.TurnUid, out var p))
            return p != null && p.eliminated;
        return false;
    }

    private async Task FixTurnIfCurrentTurnPlayerEliminatedAsync()
    {
        try
        {
            await _repo.HostFixTurnIfCurrentTurnEliminatedAsync();
        }
        catch (Exception e)
        {
            Debug.LogError($"[FixTurnIfCurrentTurnPlayerEliminatedAsync] {e}");
        }
        finally
        {
            _hostTurnFixInFlight = false;
        }
    }

    // ------------------------------------------------------------
    // ✅ 문제2: host가 게임 끝나면 전체 리셋 후 lobby로 전환
    // ------------------------------------------------------------
    private async Task ResetAndCloseMatchAsHostAsync()
    {
        try
        {
            await _repo.HostResetRoomForNextMatchAsync(cardsPerPlayer);
            // HostResetRoomForNextMatchAsync 내부에서 room.status="lobby"까지 처리
        }
        catch (Exception e)
        {
            Debug.LogError($"[ResetAndCloseMatchAsHostAsync] {e}");
        }
    }

    // ------------------------------------------------------------
    // Scene return
    // ------------------------------------------------------------
    private async Task ReturnToRoomFlowAsync()
    {
        Cleanup();
        await Task.Yield();
        SceneManager.LoadScene("RoomScene");
    }

    // ------------------------------------------------------------
    // Cached host
    // ------------------------------------------------------------
    private bool IsHostCached() => !string.IsNullOrEmpty(_hostUid) && _hostUid == _ctx.MyUid;

    // ------------------------------------------------------------
    // Render
    // ------------------------------------------------------------
    private void Render()
    {
        string myName = _ctx.Me != null ? _ctx.Me.displayName : "Me";
        string turnName =
            _ctx.PlayersByUid.TryGetValue(_ctx.TurnUid, out var tp)
                ? tp.displayName
                : $"UID:{_ctx.TurnUid}";

        ui.SetHeader(_ctx.Phase, turnName, _ctx.DeckCount, _ctx.LastLog);
        ui.SetMyName(myName);
        ui.RenderMyHand(_ctx.MyHand.cardIds, _ctx.MyHand.revealed);

        int leftSeat = (_ctx.MySeat + 1) % 3;
        int rightSeat = (_ctx.MySeat + 2) % 3;

        var left = _ctx.GetPlayerBySeat(leftSeat);
        var right = _ctx.GetPlayerBySeat(rightSeat);

        int leftCount = left?.publicCards?.Count ?? cardsPerPlayer;
        int rightCount = right?.publicCards?.Count ?? cardsPerPlayer;

        ui.SetupOpponents(left?.displayName ?? "Left", leftCount, right?.displayName ?? "Right", rightCount);
        ui.ApplyOpponentPublicCards(1, left?.publicCards);
        ui.ApplyOpponentPublicCards(2, right?.publicCards);

        ui.SetOpponentEliminated(1, left?.eliminated ?? false);
        ui.SetOpponentEliminated(2, right?.eliminated ?? false);
    }

    // ------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------
    private static List<string> ReadStringList(Dictionary<string, object> dict, string key)
    {
        var list = new List<string>();
        if (!dict.TryGetValue(key, out var raw) || raw == null) return list;
        if (raw is System.Collections.IEnumerable arr)
            foreach (var x in arr) list.Add(x?.ToString() ?? "");
        return list;
    }

    private static HandDoc ReadHand(Dictionary<string, object> dict)
    {
        var h = new HandDoc();
        h.seat = dict.TryGetValue("seat", out var s) ? Convert.ToInt32(s) : 0;
        h.cardIds = ReadStringList(dict, "cardIds");
        h.revealed = ReadBoolList(dict, "revealed");
        return h;
    }

    private static List<bool> ReadBoolList(Dictionary<string, object> dict, string key)
    {
        var list = new List<bool>();
        if (!dict.TryGetValue(key, out var raw) || raw == null) return list;
        if (raw is System.Collections.IEnumerable arr)
            foreach (var x in arr) list.Add(x != null && Convert.ToBoolean(x));
        return list;
    }

    private static List<PublicCardVM> ReadPublicCards(Dictionary<string, object> dict)
    {
        var list = new List<PublicCardVM>();
        if (!dict.TryGetValue("publicCards", out var raw) || raw == null) return list;

        if (raw is IEnumerable<object> arr)
        {
            foreach (var item in arr)
            {
                if (item is Dictionary<string, object> m)
                {
                    list.Add(new PublicCardVM
                    {
                        idx = m.TryGetValue("idx", out var idx) ? Convert.ToInt32(idx) : 0,
                        color = m.TryGetValue("color", out var col) ? col?.ToString() : "black",
                        revealed = m.TryGetValue("revealed", out var rv) && Convert.ToBoolean(rv),
                        cardId = m.TryGetValue("cardId", out var cid) ? cid?.ToString() : null
                    });
                }
            }
        }

        list.Sort((a, b) => a.idx.CompareTo(b.idx));
        return list;
    }
}