using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Firestore;

public class GameRepository
{
    private readonly GameContext _ctx;

    public GameRepository(GameContext ctx) { _ctx = ctx; }

    // ============================================================
    // Public API
    // ============================================================

    public async Task DrawAsync()
    {
        await _ctx.Db.RunTransactionAsync(async tx =>
        {
            // ===================== READS =====================
            var stateSnap = await tx.GetSnapshotAsync(_ctx.GameStateRef);
            var myHandSnap = await tx.GetSnapshotAsync(_ctx.HandRef(_ctx.MyUid));
            var myPlayerSnap = await tx.GetSnapshotAsync(_ctx.PlayerRef(_ctx.MyUid));
            if (!stateSnap.Exists || !myHandSnap.Exists || !myPlayerSnap.Exists) return 0;

            string phase = stateSnap.GetValue<string>("phase");
            if (phase == GamePhases.Finished) return 0;

            string turnUid = stateSnap.GetValue<string>("turnUid");
            if (turnUid != _ctx.MyUid) return 0;
            if (phase != GamePhases.Draw) return 0;

            var myPlayerDict = myPlayerSnap.ToDictionary();
            bool myElim = myPlayerDict.TryGetValue("eliminated", out var el) && Convert.ToBoolean(el);
            if (myElim) return 0;

            var handDict = myHandSnap.ToDictionary();
            var myCardIds = ReadStringList(handDict, "cardIds");
            var myRevealed = ReadBoolList(handDict, "revealed");
            if (myRevealed.Count != myCardIds.Count)
                myRevealed = Enumerable.Repeat(false, myCardIds.Count).ToList();

            var stateDict = stateSnap.ToDictionary();
            var seatToUid = ReadSeatToUid(stateDict);
            if (seatToUid.Count != 3) return 0;

            // (안전망) 내 패가 이미 전부 공개라면 draw 금지 + 즉시 탈락 처리 + 승리 판정까지 (READ는 이미 끝까지 수행)
            if (IsAllRevealed(myRevealed))
            {
                // 다른 플레이어 eliminated 상태도 "지금 여기서 미리 읽어둔다" (READ 단계)
                var p0 = await tx.GetSnapshotAsync(_ctx.PlayerRef(seatToUid[0]));
                var p1 = await tx.GetSnapshotAsync(_ctx.PlayerRef(seatToUid[1]));
                var p2 = await tx.GetSnapshotAsync(_ctx.PlayerRef(seatToUid[2]));
                if (!p0.Exists || !p1.Exists || !p2.Exists) return 0;

                var elimByUid = new Dictionary<string, bool>
                {
                    { seatToUid[0], ReadEliminated(p0) },
                    { seatToUid[1], ReadEliminated(p1) },
                    { seatToUid[2], ReadEliminated(p2) },
                };

                // 내 탈락을 반영한 상태로 winner 계산
                elimByUid[_ctx.MyUid] = true;
                string winnerUid = GetWinnerIfOnlyOneAlive(seatToUid, elimByUid);

                // ===================== WRITES =====================
                tx.Update(_ctx.PlayerRef(_ctx.MyUid), new Dictionary<string, object>
                {
                    { "eliminated", true },
                    { "updatedAt", FieldValue.ServerTimestamp }
                });

                if (!string.IsNullOrEmpty(winnerUid))
                {
                    tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
                    {
                        { "phase", GamePhases.Finished },
                        { "winnerUid", winnerUid },
                        { "lastLog", $"Game finished. Winner={winnerUid}" },
                        { "updatedAt", FieldValue.ServerTimestamp }
                    });
                }
                else
                {
                    tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
                    {
                        { "lastLog", "You are eliminated. Draw is not allowed." },
                        { "updatedAt", FieldValue.ServerTimestamp }
                    });
                }

                return 0;
            }

            // deck 읽기 (READ 단계)
            var deck = ReadStringList(stateDict, "deck");
            if (deck.Count == 0)
            {
                tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
    {
        { "phase", GamePhases.MustGuess }, // ✅ draw 불가 -> guess로 진행
        { "lastLog", "Deck is empty. Must guess without drawing." },
        { "updatedAt", FieldValue.ServerTimestamp }
    });
                return 0;
            }

            // ===================== CALC =====================
            string drawn = deck[0];
            deck.RemoveAt(0);

            myCardIds.Add(drawn);
            myRevealed.Add(false);

            var pub = ReadPublicCards(myPlayerDict);
            CardIdUtil.Parse(drawn, out var drawnColor, out _);
            pub.Add(new PublicCardVM { idx = myCardIds.Count - 1, color = drawnColor, revealed = false, cardId = null });

            SortHandAndPublic(myCardIds, myRevealed, pub);

            // ===================== WRITES =====================
            tx.Update(_ctx.HandRef(_ctx.MyUid), new Dictionary<string, object>
            {
                { "cardIds", myCardIds },
                { "revealed", myRevealed },
                { "updatedAt", FieldValue.ServerTimestamp }
            });

            tx.Update(_ctx.PlayerRef(_ctx.MyUid), new Dictionary<string, object>
            {
                { "publicCards", pub.Select(ToPublicCardMap).ToList() },
                { "updatedAt", FieldValue.ServerTimestamp }
            });

            tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
            {
                { "deck", deck },
                { "deckCount", deck.Count },
                { "phase", GamePhases.MustGuess },
                { "lastLog", $"Seat {_ctx.MySeat} drew 1 card. Must guess." },
                { "updatedAt", FieldValue.ServerTimestamp }
            });

            return 0;
        });
    }

    public async Task GuessAsync(int targetSeat, int cardIndex, int guessNumber, bool guessIsBlack)
    {
        var target = _ctx.GetPlayerBySeat(targetSeat);
        if (target == null) return;

        await _ctx.Db.RunTransactionAsync(async tx =>
        {
            // ===================== READS =====================
            var stateSnap = await tx.GetSnapshotAsync(_ctx.GameStateRef);
            if (!stateSnap.Exists) return 0;

            string phase = stateSnap.GetValue<string>("phase");
            if (phase == GamePhases.Finished) return 0;

            string turnUid = stateSnap.GetValue<string>("turnUid");
            int turnSeat = Convert.ToInt32(stateSnap.GetValue<long>("turnSeat"));

            if (turnUid != _ctx.MyUid) return 0;
            if (phase != GamePhases.MustGuess) return 0;

            var stateDict = stateSnap.ToDictionary();
            var seatToUid = ReadSeatToUid(stateDict);
            if (seatToUid.Count != 3) return 0;

            string myUid = _ctx.MyUid;
            string targetUid = target.uid;

            // third uid
            string thirdUid = seatToUid.FirstOrDefault(u => u != myUid && u != targetUid);

            var myPlayerRef = _ctx.PlayerRef(myUid);
            var myHandRef = _ctx.HandRef(myUid);
            var targetPlayerRef = _ctx.PlayerRef(targetUid);
            var targetHandRef = _ctx.HandRef(targetUid);

            var myPlayerSnap = await tx.GetSnapshotAsync(myPlayerRef);
            var myHandSnap = await tx.GetSnapshotAsync(myHandRef);
            var targetPlayerSnap = await tx.GetSnapshotAsync(targetPlayerRef);
            var targetHandSnap = await tx.GetSnapshotAsync(targetHandRef);

            if (!myPlayerSnap.Exists || !myHandSnap.Exists || !targetPlayerSnap.Exists || !targetHandSnap.Exists) return 0;

            DocumentSnapshot thirdPlayerSnap = null;
            if (!string.IsNullOrEmpty(thirdUid))
            {
                thirdPlayerSnap = await tx.GetSnapshotAsync(_ctx.PlayerRef(thirdUid));
                if (!thirdPlayerSnap.Exists) thirdPlayerSnap = null;
            }

            bool myElim = ReadEliminated(myPlayerSnap);
            bool targetElim = ReadEliminated(targetPlayerSnap);
            bool thirdElim = (thirdPlayerSnap != null) ? ReadEliminated(thirdPlayerSnap) : true;

            if (myElim || targetElim) return 0;

            // target hand
            var targetHandDict = targetHandSnap.ToDictionary();
            var targetCardIds = ReadStringList(targetHandDict, "cardIds");
            var targetRevealed = ReadBoolList(targetHandDict, "revealed");
            if (targetRevealed.Count != targetCardIds.Count)
                targetRevealed = Enumerable.Repeat(false, targetCardIds.Count).ToList();

            if (cardIndex < 0 || cardIndex >= targetCardIds.Count) return 0;
            if (targetRevealed[cardIndex]) return 0;

            string targetCardId = targetCardIds[cardIndex];
            CardIdUtil.Parse(targetCardId, out var realColor, out int realNumber);
            bool correct = (realColor == (guessIsBlack ? "black" : "white")) && (realNumber == guessNumber);

            // my hand (penalty)
            var myHandDict = myHandSnap.ToDictionary();
            var myCardIds = ReadStringList(myHandDict, "cardIds");
            var myRevealed = ReadBoolList(myHandDict, "revealed");
            if (myRevealed.Count != myCardIds.Count)
                myRevealed = Enumerable.Repeat(false, myCardIds.Count).ToList();

            // public cards dicts
            var myPlayerDict = myPlayerSnap.ToDictionary();
            var targetPlayerDict = targetPlayerSnap.ToDictionary();

            // ===================== CALC (로컬 상태) =====================
            bool newMyElim = myElim;
            bool newTargetElim = targetElim;
            bool newThirdElim = thirdElim;

            // ===================== WRITES (여기부터는 Get 금지) =====================
            if (correct)
            {
                // target reveal
                targetRevealed[cardIndex] = true;

                tx.Update(targetHandRef, new Dictionary<string, object>
                {
                    { "revealed", targetRevealed },
                    { "updatedAt", FieldValue.ServerTimestamp }
                });

                var targetPub = ReadPublicCards(targetPlayerDict);
                var entry = targetPub.FirstOrDefault(x => x.idx == cardIndex);
                if (entry != null)
                {
                    entry.revealed = true;
                    entry.cardId = targetCardId;
                }

                // eliminated 체크(로컬)
                if (IsAllRevealed(targetRevealed))
                {
                    newTargetElim = true;

                    // publicCards + eliminated를 한 번에
                    tx.Update(targetPlayerRef, new Dictionary<string, object>
                    {
                        { "publicCards", targetPub.Select(ToPublicCardMap).ToList() },
                        { "eliminated", true },
                        { "updatedAt", FieldValue.ServerTimestamp }
                    });
                }
                else
                {
                    tx.Update(targetPlayerRef, new Dictionary<string, object>
                    {
                        { "publicCards", targetPub.Select(ToPublicCardMap).ToList() },
                        { "updatedAt", FieldValue.ServerTimestamp }
                    });
                }
            }
            else
            {
                // penalty: reveal my last card
                int penaltyIdx = myCardIds.Count - 1;
                if (penaltyIdx < 0 || penaltyIdx >= myRevealed.Count) return 0;

                if (!myRevealed[penaltyIdx])
                {
                    myRevealed[penaltyIdx] = true;

                    tx.Update(myHandRef, new Dictionary<string, object>
                    {
                        { "revealed", myRevealed },
                        { "updatedAt", FieldValue.ServerTimestamp }
                    });

                    var myPub = ReadPublicCards(myPlayerDict);
                    var meEntry = myPub.FirstOrDefault(x => x.idx == penaltyIdx);
                    if (meEntry != null)
                    {
                        meEntry.revealed = true;
                        meEntry.cardId = myCardIds[penaltyIdx];
                    }

                    if (IsAllRevealed(myRevealed))
                    {
                        newMyElim = true;

                        tx.Update(myPlayerRef, new Dictionary<string, object>
                        {
                            { "publicCards", myPub.Select(ToPublicCardMap).ToList() },
                            { "eliminated", true },
                            { "updatedAt", FieldValue.ServerTimestamp }
                        });
                    }
                    else
                    {
                        tx.Update(myPlayerRef, new Dictionary<string, object>
                        {
                            { "publicCards", myPub.Select(ToPublicCardMap).ToList() },
                            { "updatedAt", FieldValue.ServerTimestamp }
                        });
                    }
                }
            }

            // ---- finish 판정(로컬 eliminated로만) ----
            var elimByUid = new Dictionary<string, bool>();
            elimByUid[myUid] = newMyElim;
            elimByUid[targetUid] = newTargetElim;
            if (!string.IsNullOrEmpty(thirdUid)) elimByUid[thirdUid] = newThirdElim;

            string winnerUid = GetWinnerIfOnlyOneAlive(seatToUid, elimByUid);
            if (!string.IsNullOrEmpty(winnerUid))
            {
                tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
                {
                    { "phase", GamePhases.Finished },
                    { "winnerUid", winnerUid },
                    { "lastLog", $"Game finished. Winner={winnerUid}" },
                    { "updatedAt", FieldValue.ServerTimestamp }
                });
                return 0;
            }

            // ---- phase/turn 진행 ----
            if (correct)
            {
                // 맞추면 턴 유지(GuessChoice)
                tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
                {
                    { "phase", GamePhases.GuessChoice },
                    { "lastLog", $"Correct! Revealed seat {targetSeat} idx={cardIndex}" },
                    { "updatedAt", FieldValue.ServerTimestamp }
                });
            }
            else
            {
                // 틀리면 다음 alive로 넘김 (탈락자 스킵)
                int nextSeat = NextAliveSeat(turnSeat, seatToUid, elimByUid);
                if (nextSeat < 0) return 0;

                tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
                {
                    { "phase", GamePhases.Draw },
                    { "turnSeat", nextSeat },
                    { "turnUid", seatToUid[nextSeat] },
                    { "lastLog", $"Wrong! Penalty reveal. Next turn -> seat {nextSeat}" },
                    { "updatedAt", FieldValue.ServerTimestamp }
                });
            }

            return 0;
        });
    }

    public async Task ContinueGuessAsync()
    {
        await _ctx.Db.RunTransactionAsync(async tx =>
        {
            var st = await tx.GetSnapshotAsync(_ctx.GameStateRef);
            if (!st.Exists) return 0;

            string phase = st.GetValue<string>("phase");
            if (phase == GamePhases.Finished) return 0;

            string turnUid = st.GetValue<string>("turnUid");
            if (turnUid != _ctx.MyUid) return 0;
            if (phase != GamePhases.GuessChoice) return 0;

            var stateDict = st.ToDictionary();
            var seatToUid = ReadSeatToUid(stateDict);
            if (seatToUid.Count != 3) return 0;

            // ✅ eliminated 맵 구성 (문서 없으면 eliminated=true로 간주)
            var elimByUid = new Dictionary<string, bool>();
            foreach (var uid in seatToUid)
            {
                if (string.IsNullOrEmpty(uid)) continue;
                var ps = await tx.GetSnapshotAsync(_ctx.PlayerRef(uid));
                elimByUid[uid] = (ps == null || !ps.Exists) ? true : ReadEliminated(ps);
            }

            // ✅ 승자 체크
            string winnerUid = GetWinnerIfOnlyOneAlive(seatToUid, elimByUid);
            if (!string.IsNullOrEmpty(winnerUid))
            {
                tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
            {
                { "phase", GamePhases.Finished },
                { "winnerUid", winnerUid },
                { "lastLog", $"Game finished. Winner={winnerUid}" },
                { "updatedAt", FieldValue.ServerTimestamp }
            });
                return 0;
            }

            // ✅ 내가 턴인데 "살아있는 상대(나 제외)"가 1명도 없으면 -> Finished 안전망
            bool hasAliveTarget = seatToUid.Any(uid =>
                !string.IsNullOrEmpty(uid) &&
                uid != _ctx.MyUid &&
                elimByUid.TryGetValue(uid, out var e) && !e
            );

            if (!hasAliveTarget)
            {
                tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
            {
                { "phase", GamePhases.Finished },
                { "winnerUid", _ctx.MyUid }, // 나만 살아있다고 간주
                { "lastLog", "No alive targets. Auto-finish." },
                { "updatedAt", FieldValue.ServerTimestamp }
            });
                return 0;
            }

            // 정상 진행
            tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
        {
            { "phase", GamePhases.MustGuess },
            { "lastLog", "Continue guessing." },
            { "updatedAt", FieldValue.ServerTimestamp }
        });

            return 0;
        });
    }

    public async Task EndTurnAsync()
    {
        await _ctx.Db.RunTransactionAsync(async tx =>
        {
            // ===================== READS =====================
            var st = await tx.GetSnapshotAsync(_ctx.GameStateRef);
            if (!st.Exists) return 0;

            string phase = st.GetValue<string>("phase");
            if (phase == GamePhases.Finished) return 0;

            int turnSeat = Convert.ToInt32(st.GetValue<long>("turnSeat"));
            string turnUid = st.GetValue<string>("turnUid");

            if (turnUid != _ctx.MyUid) return 0;
            if (phase != GamePhases.GuessChoice) return 0;

            var stateDict = st.ToDictionary();
            var seatToUid = ReadSeatToUid(stateDict);
            if (seatToUid.Count != 3) return 0;

            // 3명 playerSnap을 미리 읽어서 eliminated 맵 만든다 (READ 단계)
            var p0 = await tx.GetSnapshotAsync(_ctx.PlayerRef(seatToUid[0]));
            var p1 = await tx.GetSnapshotAsync(_ctx.PlayerRef(seatToUid[1]));
            var p2 = await tx.GetSnapshotAsync(_ctx.PlayerRef(seatToUid[2]));
            if (!p0.Exists || !p1.Exists || !p2.Exists) return 0;

            var elimByUid = new Dictionary<string, bool>
            {
                { seatToUid[0], ReadEliminated(p0) },
                { seatToUid[1], ReadEliminated(p1) },
                { seatToUid[2], ReadEliminated(p2) },
            };

            // 혹시 여기 시점에 이미 1명만 남았으면 finished로 마무리(안전망)
            string winnerUid = GetWinnerIfOnlyOneAlive(seatToUid, elimByUid);
            if (!string.IsNullOrEmpty(winnerUid))
            {
                // ===================== WRITES =====================
                tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
                {
                    { "phase", GamePhases.Finished },
                    { "winnerUid", winnerUid },
                    { "lastLog", $"Game finished. Winner={winnerUid}" },
                    { "updatedAt", FieldValue.ServerTimestamp }
                });
                return 0;
            }

            // ===================== CALC =====================
            int nextSeat = NextAliveSeat(turnSeat, seatToUid, elimByUid);
            if (nextSeat < 0) return 0;

            // ===================== WRITES =====================
            tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
            {
                { "phase", GamePhases.Draw },
                { "turnSeat", nextSeat },
                { "turnUid", seatToUid[nextSeat] },
                { "lastLog", $"Turn ended. Next -> seat {nextSeat}" },
                { "updatedAt", FieldValue.ServerTimestamp }
            });

            return 0;
        });
    }

    // ============================================================
    // Pure helpers (tx 절대 안 받음)  ✅ 안전
    // ============================================================

    private static int NextAliveSeat(int currentSeat, List<string> seatToUid, Dictionary<string, bool> eliminatedByUid)
    {
        // 3인 고정
        for (int step = 1; step <= 3; step++)
        {
            int s = (currentSeat + step) % 3;
            var uid = seatToUid[s];
            if (string.IsNullOrEmpty(uid)) continue;

            bool elim = eliminatedByUid.TryGetValue(uid, out var e) && e;
            if (!elim) return s;
        }
        return -1;
    }

    private static string GetWinnerIfOnlyOneAlive(List<string> seatToUid, Dictionary<string, bool> eliminatedByUid)
    {
        int alive = 0;
        string winner = null;

        foreach (var uid in seatToUid)
        {
            if (string.IsNullOrEmpty(uid)) continue;
            bool elim = eliminatedByUid.TryGetValue(uid, out var e) && e;
            if (!elim)
            {
                alive++;
                winner = uid;
            }
        }

        return alive == 1 ? winner : null;
    }

    private static bool ReadEliminated(DocumentSnapshot playerSnap)
    {
        var d = playerSnap.ToDictionary();
        return d.TryGetValue("eliminated", out var ev) && Convert.ToBoolean(ev);
    }

    // ============================================================
    // Dict helpers
    // ============================================================

    private static List<string> ReadStringList(Dictionary<string, object> dict, string key)
    {
        var list = new List<string>();
        if (!dict.TryGetValue(key, out var raw) || raw == null) return list;
        if (raw is System.Collections.IEnumerable arr)
            foreach (var x in arr) list.Add(x?.ToString() ?? "");
        return list;
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

    private static List<string> ReadSeatToUid(Dictionary<string, object> dict)
    {
        var list = new List<string>();
        if (!dict.TryGetValue("seatToUid", out var raw) || raw == null) return list;
        if (raw is System.Collections.IEnumerable arr)
            foreach (var x in arr) list.Add(x?.ToString() ?? "");
        return list;
    }

    private static Dictionary<string, object> ToPublicCardMap(PublicCardVM x)
    {
        return new Dictionary<string, object>
        {
            { "idx", x.idx },
            { "color", x.color },
            { "revealed", x.revealed },
            { "cardId", x.revealed ? (object)x.cardId : null }
        };
    }

    // ============================================================
    // Sorting helpers
    // ============================================================

    private static int SortKey(string cardId)
    {
        if (string.IsNullOrEmpty(cardId) || cardId.Length < 2) return int.MaxValue;

        bool isBlack = cardId.StartsWith("B", StringComparison.OrdinalIgnoreCase);
        int colorRank = isBlack ? 0 : 1;

        int num = 0;
        int.TryParse(cardId.Substring(1), out num);

        return num * 10 + colorRank;
    }

    private static void SortHandAndPublic(List<string> cardIds, List<bool> revealed, List<PublicCardVM> pub)
    {
        var idxs = Enumerable.Range(0, cardIds.Count).ToList();
        idxs.Sort((a, b) => SortKey(cardIds[a]).CompareTo(SortKey(cardIds[b])));

        var newIds = idxs.Select(i => cardIds[i]).ToList();
        var newRev = idxs.Select(i => (i < revealed.Count ? revealed[i] : false)).ToList();

        var oldToNew = new Dictionary<int, int>();
        for (int newIdx = 0; newIdx < idxs.Count; newIdx++)
            oldToNew[idxs[newIdx]] = newIdx;

        foreach (var p in pub)
        {
            if (oldToNew.TryGetValue(p.idx, out var newIdx))
                p.idx = newIdx;
        }
        pub.Sort((x, y) => x.idx.CompareTo(y.idx));

        cardIds.Clear(); cardIds.AddRange(newIds);
        revealed.Clear(); revealed.AddRange(newRev);
    }

    private static bool IsAllRevealed(List<bool> revealed)
    {
        if (revealed == null || revealed.Count == 0) return false;
        for (int i = 0; i < revealed.Count; i++)
            if (!revealed[i]) return false;
        return true;
    }

    public async Task ForceMustGuessWhenDeckEmptyAsync()
    {
        await _ctx.Db.RunTransactionAsync(async tx =>
        {
            var stateSnap = await tx.GetSnapshotAsync(_ctx.GameStateRef);
            if (!stateSnap.Exists) return 0;

            string phase = stateSnap.GetValue<string>("phase");
            if (phase == GamePhases.Finished) return 0;

            string turnUid = stateSnap.GetValue<string>("turnUid");
            if (turnUid != _ctx.MyUid) return 0;

            int deckCount = 0;
            if (stateSnap.ToDictionary().TryGetValue("deckCount", out var dc) && dc != null)
                deckCount = Convert.ToInt32(dc);

            // 덱이 남아있으면 굳이 바꾸지 않음
            if (deckCount > 0) return 0;

            // draw 상태에서만 must_guess로 전환
            if (phase == GamePhases.Draw)
            {
                tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
            {
                { "phase", GamePhases.MustGuess },
                { "lastLog", "Deck is empty. Must guess without drawing." },
                { "updatedAt", FieldValue.ServerTimestamp }
            });
            }

            return 0;
        });
    }

    public async Task HostFixTurnIfCurrentTurnEliminatedAsync()
    {
        await _ctx.Db.RunTransactionAsync(async tx =>
        {
            var st = await tx.GetSnapshotAsync(_ctx.GameStateRef);
            if (!st.Exists) return 0;

            string phase = st.GetValue<string>("phase");
            if (phase == GamePhases.Finished) return 0;

            var stateDict = st.ToDictionary();
            var seatToUid = ReadSeatToUid(stateDict);
            if (seatToUid.Count != 3) return 0;

            int turnSeat = Convert.ToInt32(st.GetValue<long>("turnSeat"));
            string turnUid = st.GetValue<string>("turnUid");

            // 현재 턴 uid의 eliminated를 읽는다
            var turnPlayerSnap = await tx.GetSnapshotAsync(_ctx.PlayerRef(turnUid));
            if (!turnPlayerSnap.Exists) return 0;

            bool turnElim = ReadEliminated(turnPlayerSnap);
            if (!turnElim) return 0; // ✅ 정상

            // 3명 eliminated 맵 구성
            var p0 = await tx.GetSnapshotAsync(_ctx.PlayerRef(seatToUid[0]));
            var p1 = await tx.GetSnapshotAsync(_ctx.PlayerRef(seatToUid[1]));
            var p2 = await tx.GetSnapshotAsync(_ctx.PlayerRef(seatToUid[2]));
            if (!p0.Exists || !p1.Exists || !p2.Exists) return 0;

            var elimByUid = new Dictionary<string, bool>
        {
            { seatToUid[0], ReadEliminated(p0) },
            { seatToUid[1], ReadEliminated(p1) },
            { seatToUid[2], ReadEliminated(p2) },
        };

            // 혹시 1명만 살아있으면 finish로
            string winnerUid = GetWinnerIfOnlyOneAlive(seatToUid, elimByUid);
            if (!string.IsNullOrEmpty(winnerUid))
            {
                tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
            {
                { "phase", GamePhases.Finished },
                { "winnerUid", winnerUid },
                { "lastLog", $"Game finished. Winner={winnerUid}" },
                { "updatedAt", FieldValue.ServerTimestamp }
            });
                return 0;
            }

            // 다음 생존자 턴으로 넘김 (phase는 Draw로 복귀)
            int nextSeat = NextAliveSeat(turnSeat, seatToUid, elimByUid);
            if (nextSeat < 0) return 0;

            tx.Update(_ctx.GameStateRef, new Dictionary<string, object>
        {
            { "phase", GamePhases.Draw },
            { "turnSeat", nextSeat },
            { "turnUid", seatToUid[nextSeat] },
            { "lastLog", $"Turn player was eliminated. Auto advance -> seat {nextSeat}" },
            { "updatedAt", FieldValue.ServerTimestamp }
        });

            return 0;
        });
    }

    public async Task HostResetRoomForNextMatchAsync(int cardsPerPlayer)
    {
        // ✅ host만 호출해야 함 (InGameController에서 host만 호출)
        // rules에서 isHost(roomId)면 rooms/game/state 업데이트가 허용됨

        // 1) players 읽기
        var playersSnap = await _ctx.RoomRef.Collection("players").GetSnapshotAsync();
        var players = new List<PlayerVM>();
        foreach (var doc in playersSnap.Documents)
        {
            var d = doc.ToDictionary();
            players.Add(new PlayerVM
            {
                uid = doc.Id,
                seat = d.TryGetValue("seat", out var s) ? Convert.ToInt32(s) : 0
            });
        }
        players = players.OrderBy(p => p.seat).ToList();
        if (players.Count != 3) return;

        var seatToUid = new List<string> { players[0].uid, players[1].uid, players[2].uid };

        // 2) 새 덱 생성/셔플
        var deck = new List<string>();
        for (int n = 0; n <= 11; n++)
        {
            deck.Add(CardIdUtil.ToCardId("black", n));
            deck.Add(CardIdUtil.ToCardId("white", n));
        }
        var rng = new System.Random();
        deck = deck.OrderBy(_ => rng.Next()).ToList();

        var batch = _ctx.Db.StartBatch();

        // 3) players/hands 초기화 (ready=false, eliminated=false 포함)
        foreach (var p in players)
        {
            var playerRef = _ctx.RoomRef.Collection("players").Document(p.uid);
            var handRef = _ctx.RoomRef.Collection("hands").Document(p.uid);

            var myCards = deck.Take(cardsPerPlayer).ToList();
            deck.RemoveRange(0, cardsPerPlayer);

            var revealed = Enumerable.Repeat(false, cardsPerPlayer).ToList();

            batch.Set(handRef, new Dictionary<string, object>
        {
            { "seat", p.seat },
            { "cardIds", myCards },
            { "revealed", revealed },
            { "updatedAt", FieldValue.ServerTimestamp }
        });

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

            batch.Update(playerRef, new Dictionary<string, object>
        {
            { "ready", false },
            { "eliminated", false },
            { "publicCards", publicCards },
            { "updatedAt", FieldValue.ServerTimestamp }
        });
        }

        // 4) game/state reset
        var stateRef = _ctx.RoomRef.Collection("game").Document("state");
        batch.Set(stateRef, new Dictionary<string, object>
    {
        { "phase", GamePhases.Draw },
        { "turnSeat", 0 },
        { "turnUid", seatToUid[0] },
        { "seatToUid", seatToUid },
        { "deck", deck },
        { "deckCount", deck.Count },
        { "winnerUid", null },
        { "lastLog", "Reset complete. Back to lobby." },
        { "updatedAt", FieldValue.ServerTimestamp }
    });

        // 5) ✅ 마지막에 status=lobby (전원 복귀 신호)
        batch.Update(_ctx.RoomRef, new Dictionary<string, object>
    {
        { "status", "lobby" },
        { "updatedAt", FieldValue.ServerTimestamp }
    });

        await batch.CommitAsync();
    }
}