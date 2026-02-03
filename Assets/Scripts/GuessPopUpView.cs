using System;
using UnityEngine;
using UnityEngine.UI;

public class GuessPopupView : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject root;
    [SerializeField] private Text targetNameText;
    [SerializeField] private Text selectedCardInfoText;

    [Header("Inputs")]
    [SerializeField] private Dropdown numberDropdown;
    [SerializeField] private Toggle blackToggle;
    [SerializeField] private Toggle whiteToggle;
    [SerializeField] private ToggleGroup colorToggleGroup;

    [Header("Buttons")]
    [SerializeField] private Button submitButton;
    [SerializeField] private Button cancelButton;

    public event Action<GuessRequest> OnSubmit;
    public event Action OnCancel;

    private GuessRequest _current;
    private bool _wiring;

    private void Awake()
    {
        if (root == null) root = gameObject;

        if (colorToggleGroup == null)
            colorToggleGroup = GetComponentInChildren<ToggleGroup>(true);

        if (colorToggleGroup != null)
        {
            colorToggleGroup.allowSwitchOff = false;
        }

        if (blackToggle && colorToggleGroup) blackToggle.group = colorToggleGroup;
        if (whiteToggle && colorToggleGroup) whiteToggle.group = colorToggleGroup;

        WireOnce();

        if (submitButton) submitButton.onClick.AddListener(Submit);
        if (cancelButton) cancelButton.onClick.AddListener(Cancel);

        Hide();
    }

    private void WireOnce()
    {
        if (_wiring) return;
        _wiring = true;

        if (blackToggle) blackToggle.onValueChanged.AddListener(isOn =>
        {
            if (isOn && whiteToggle) whiteToggle.isOn = false;
        });

        if (whiteToggle) whiteToggle.onValueChanged.AddListener(isOn =>
        {
            if (isOn && blackToggle) blackToggle.isOn = false;
        });
    }

    public void Show(string targetName, int relativeSeat, int cardIndex)
    {
        _current = new GuessRequest
        {
            targetRelativeSeat = relativeSeat,
            targetCardIndex = cardIndex
        };

        if (targetNameText) targetNameText.text = string.IsNullOrEmpty(targetName) ? "Target" : targetName;
        if (selectedCardInfoText) selectedCardInfoText.text = $"선택 카드: #{cardIndex}";

        if (numberDropdown) numberDropdown.value = 0;
        if (blackToggle) blackToggle.isOn = true;
        if (whiteToggle) whiteToggle.isOn = false;

        root.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void Hide()
    {
        if (root) root.SetActive(false);
    }

    private void Submit()
    {
        _current.guessNumber = numberDropdown ? numberDropdown.value : 0;

        bool isBlack = blackToggle != null && blackToggle.isOn;
        bool isWhite = whiteToggle != null && whiteToggle.isOn;

        // 안전장치
        if (isBlack == isWhite)
        {
            isBlack = true;
            if (blackToggle) blackToggle.isOn = true;
            if (whiteToggle) whiteToggle.isOn = false;
        }

        _current.guessIsBlack = isBlack;

        OnSubmit?.Invoke(_current);
        Hide();
    }

    private void Cancel()
    {
        OnCancel?.Invoke();
        Hide();
    }
}

[Serializable]
public struct GuessRequest
{
    public int targetRelativeSeat;
    public int targetCardIndex;
    public int guessNumber;
    public bool guessIsBlack;
}