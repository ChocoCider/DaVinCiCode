using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// Firebase 진입점(Auth + Firestore).
/// - DontDestroyOnLoad 싱글턴
/// - 멀티 인스턴스(에디터+빌드, 빌드 2개) 테스트 안정화를 위해
///   Firestore 로컬 퍼시스턴스(캐시)를 기본 OFF 로 설정(중요)
/// </summary>
public class FirebaseAuthService : MonoBehaviour
{
    public static FirebaseAuthService Instance { get; private set; }

    public FirebaseAuth Auth { get; private set; }
    public FirebaseFirestore Db { get; private set; }

    public bool Ready { get; private set; }

    public string MyUid => Auth?.CurrentUser?.UserId;
    public string MyDisplayName { get; private set; } = "";

    public event Action<bool> OnAuthStateChanged; // signedIn?

    [Header("Debug/Test")]
    [Tooltip("같은 PC에서 에디터/빌드 여러 개를 동시에 실행할 때 크래시를 막기 위해 Firestore 로컬 캐시를 끕니다.")]
    [SerializeField] private bool disableFirestorePersistence = true;

    [Tooltip("Init 완료 로그 출력")]
    [SerializeField] private bool verboseLog = true;

    private bool _initStarted = false;
    private bool _handlingAuthState = false;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private async void Start()
    {
        await Init();
    }

    private void OnDestroy()
    {
        try
        {
            if (Auth != null)
                Auth.StateChanged -= HandleAuthStateChanged;
        }
        catch { /* ignore */ }
    }

    public async Task Init()
    {
        if (Ready) return;
        if (_initStarted) return;
        _initStarted = true;

        try
        {
            var dep = await FirebaseApp.CheckAndFixDependenciesAsync();
            if (dep != DependencyStatus.Available)
            {
                Debug.LogError($"[Firebase] Dependencies error: {dep}");
                return;
            }

            Auth = FirebaseAuth.DefaultInstance;
            Db = FirebaseFirestore.DefaultInstance;

            // ✅ 멀티 프로세스 동시 실행(에디터+빌드, 빌드 2개) 시
            // Firestore 로컬 퍼시스턴스(캐시) 파일 충돌로 한쪽이 종료되는 케이스가 매우 흔함.
            // 테스트 단계에서는 OFF가 안전.
            if (disableFirestorePersistence && Db != null)
            {
                var settings = Db.Settings;
                settings.PersistenceEnabled = false;

                if (verboseLog) Debug.Log("[Firebase] Firestore persistence disabled (multi-instance safe).");
            }

            Auth.StateChanged += HandleAuthStateChanged;

            Ready = true;

            // 현재 상태 1회 반영(이미 로그인 상태일 수 있음)
            _ = SafeRaiseAuthStateChanged();

            if (verboseLog) Debug.Log("[Firebase] Ready (Auth + Firestore).");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Firebase] Init exception: {e}");
        }
    }

    /// <summary>
    /// Auth.StateChanged 이벤트 핸들러
    /// </summary>
    private void HandleAuthStateChanged(object sender, EventArgs e)
    {
        _ = SafeRaiseAuthStateChanged();
    }

    private async Task SafeRaiseAuthStateChanged()
    {
        if (!Ready || Auth == null) return;

        // ✅ StateChanged가 연속 호출되는 경우 레이스 방지
        if (_handlingAuthState) return;
        _handlingAuthState = true;

        try
        {
            bool signedIn = Auth.CurrentUser != null;

            if (signedIn)
            {
                await LoadMyProfile(); // displayName 동기화
            }
            else
            {
                MyDisplayName = "";
            }

            OnAuthStateChanged?.Invoke(signedIn);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Firebase] AuthStateChanged handler exception: {e}");
        }
        finally
        {
            _handlingAuthState = false;
        }
    }

    // ---------------- Auth API ----------------

    public async Task SignUp(string email, string password, string inGameName)
    {
        if (!Ready) throw new Exception("Firebase not ready.");
        if (string.IsNullOrWhiteSpace(inGameName)) throw new Exception("InGameName is empty.");

        email = email?.Trim();
        inGameName = inGameName.Trim();

        var cred = await Auth.CreateUserWithEmailAndPasswordAsync(email, password);
        string uid = cred.User.UserId;

        await UpsertUserProfile(uid, inGameName);
        await LoadMyProfile();

        // 상태 이벤트(로그인 UI가 구독 중이면 반응)
        OnAuthStateChanged?.Invoke(true);
    }

    public async Task SignIn(string email, string password)
    {
        if (!Ready) throw new Exception("Firebase not ready.");

        email = email?.Trim();

        await Auth.SignInWithEmailAndPasswordAsync(email, password);
        await LoadMyProfile();

        OnAuthStateChanged?.Invoke(true);
    }

    public void SignOut()
    {
        try
        {
            Auth?.SignOut();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Firebase] SignOut exception: {e}");
        }

        MyDisplayName = "";
        OnAuthStateChanged?.Invoke(false);
    }

    // ---------------- Firestore User Profile ----------------

    private DocumentReference UserDoc(string uid) => Db.Collection("users").Document(uid);

    private async Task UpsertUserProfile(string uid, string inGameName)
    {
        var data = new Dictionary<string, object>
        {
            { "displayName", inGameName },
            { "createdAt", FieldValue.ServerTimestamp },
            { "lastOnlineAt", FieldValue.ServerTimestamp }
        };

        await UserDoc(uid).SetAsync(data, SetOptions.MergeAll);
    }

    public async Task LoadMyProfile()
    {
        if (Auth?.CurrentUser == null) return;
        if (Db == null) return;

        string uid = MyUid;
        if (string.IsNullOrEmpty(uid)) return;

        var snap = await UserDoc(uid).GetSnapshotAsync();

        if (snap.Exists && snap.ContainsField("displayName"))
            MyDisplayName = snap.GetValue<string>("displayName");
        else
            MyDisplayName = "Player";

        // lastOnline 갱신(선택)
        await UserDoc(uid).SetAsync(new Dictionary<string, object>
        {
            { "lastOnlineAt", FieldValue.ServerTimestamp }
        }, SetOptions.MergeAll);
    }
}