using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class LoginUIController : MonoBehaviour
{
    [Header("Inputs")]
    [SerializeField] private InputField emailInput;
    [SerializeField] private InputField passwordInput;
    [SerializeField] private InputField inGameNameInput; // SignUp 시 사용

    [Header("Buttons")]
    [SerializeField] private Button loginButton;
    [SerializeField] private Button signUpButton;

    [Header("UI")]
    [SerializeField] private Text statusText;

    [Header("Scene")]
    [SerializeField] private string lobbySceneName = SceneNames.Lobby;

    private void Awake()
    {
        if (loginButton) loginButton.onClick.AddListener(() => _ = Login());
        if (signUpButton) signUpButton.onClick.AddListener(() => _ = SignUp());
    }

    private async void Start()
    {
        // Firebase 준비 대기
        while (FirebaseAuthService.Instance == null || !FirebaseAuthService.Instance.Ready)
            await Task.Yield();

        // 자동 로그인 기능 제거: LoginScene 들어오면 무조건 로그아웃
        // (에디터/빌드 모두 적용. 원하면 UNITY_EDITOR로 감싸도 됨)
        try
        {
            FirebaseAuthService.Instance.SignOut();
            SetStatus("로그인 해주세요.");
        }
        catch { /* ignore */ }
    }

    private async Task Login()
    {
        try
        {
            SetStatus("로그인 중...");

            await FirebaseAuthService.Instance.SignIn(
                emailInput.text.Trim(),
                passwordInput.text
            );

            SetStatus("로그인 성공!");
            SceneLoader.LoadIfNotCurrent(lobbySceneName);
        }
        catch (Exception e)
        {
            SetStatus($"로그인 실패: {e.Message}");
        }
    }

    private async Task SignUp()
    {
        try
        {
            SetStatus("회원가입 중...");

            await FirebaseAuthService.Instance.SignUp(
                emailInput.text.Trim(),
                passwordInput.text,
                inGameNameInput.text.Trim()
            );

            SetStatus("회원가입 성공!");
            SceneLoader.LoadIfNotCurrent(lobbySceneName);
        }
        catch (Exception e)
        {
            SetStatus($"회원가입 실패: {e.Message}");
        }
    }

    private void SetStatus(string msg)
    {
        if (statusText) statusText.text = msg;
        Debug.Log(msg);
    }
}