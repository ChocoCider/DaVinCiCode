using System.Collections.Generic;
using Firebase.Firestore;

public class GameContext
{
    public FirebaseFirestore Db;
    public string RoomId;
    public string MyUid;

    public string TurnUid;
    public List<string> SeatToUid = new();
    public bool IsMyTurn => TurnUid == MyUid;

    public int MySeat;
    public int TurnSeat;
    public string Phase = GamePhases.Draw;
    public int DeckCount;
    public string LastLog = "";

    public string WinnerUid;

    public List<string> Deck = new();
    public Dictionary<string, PlayerVM> PlayersByUid = new();
    public Dictionary<int, string> UidBySeat = new();

    public HandDoc MyHand = new HandDoc();

    public GameUIController UI;

    public DocumentReference RoomRef => Db.Collection("rooms").Document(RoomId);
    public DocumentReference GameStateRef => RoomRef.Collection("game").Document("state");
    public DocumentReference PlayerRef(string uid) => RoomRef.Collection("players").Document(uid);
    public DocumentReference HandRef(string uid) => RoomRef.Collection("hands").Document(uid);

    public PlayerVM Me => PlayersByUid.TryGetValue(MyUid, out var p) ? p : null;

    public PlayerVM GetPlayerBySeat(int seat)
    {
        if (UidBySeat.TryGetValue(seat, out var uid) && PlayersByUid.TryGetValue(uid, out var p))
            return p;
        return null;
    }
}