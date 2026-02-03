using System;
using UnityEngine;
using UnityEngine.UI;

public class CardView : MonoBehaviour
{
    [Header("Roots")]
    [SerializeField] private GameObject frontRoot;
    [SerializeField] private GameObject backRoot;

    [Header("Front UI")]
    [SerializeField] private Text numberText;
    [SerializeField] private Image frontBg;

    [Header("Back UI")]
    [SerializeField] private Image backBg;

    [Header("Selection")]
    [SerializeField] private Image selectedOutline;

    [Header("Click")]
    [SerializeField] private Button button;

    public int CardIndex { get; private set; } = -1;
    public bool IsFaceUp { get; private set; }
    public event Action<int> OnClicked;

    private void Awake()
    {
        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                Debug.Log($"[CardView] Click idx={CardIndex}");
                OnClicked?.Invoke(CardIndex);
            });
        }
    }

    public void SetIndex(int idx) => CardIndex = idx;

    public void SetSelected(bool selected)
    {
        if (selectedOutline) selectedOutline.enabled = selected;
    }

    public void SetClickable(bool clickable)
    {
        if (button) button.interactable = clickable;
    }

    public void SetColorHint(string color)
    {
        bool isBlack = string.Equals(color, "black", StringComparison.OrdinalIgnoreCase);
        if (backBg) backBg.color = isBlack ? Color.black : Color.white;
        if (frontBg) frontBg.color = isBlack ? Color.black : Color.white;
    }

    public void SetCardId(string cardId, bool faceUp)
    {
        IsFaceUp = faceUp;

        if (frontRoot) frontRoot.SetActive(faceUp);
        if (backRoot) backRoot.SetActive(!faceUp);

        if (!string.IsNullOrEmpty(cardId))
        {
            bool isBlack = cardId.StartsWith("B", StringComparison.OrdinalIgnoreCase);
            SetColorHint(isBlack ? "black" : "white");
        }

        if (!faceUp)
        {
            if (numberText) numberText.text = "";
            return;
        }

        if (numberText) numberText.text = ParseNumber(cardId).ToString();
    }

    private int ParseNumber(string cardId)
    {
        if (string.IsNullOrEmpty(cardId) || cardId.Length < 2) return -1;
        return int.TryParse(cardId.Substring(1), out int n) ? n : -1;
    }
}