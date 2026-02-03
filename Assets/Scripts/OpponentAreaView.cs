using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class OpponentAreaView : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Text nameText;
    [SerializeField] private Transform cardsRow;
    [SerializeField] private CardView cardPrefab;

    public int RelativeSeat { get; private set; } // 1=Left, 2=Right
    public event Action<int, int> OnCardClicked;  // (relativeSeat, cardIndex)

    private readonly List<CardView> _cards = new();
    private bool _eliminated;

    public void SetHeader(string displayName, int relativeSeat)
    {
        RelativeSeat = relativeSeat;
        if (nameText) nameText.text = string.IsNullOrEmpty(displayName) ? "Player" : displayName;
    }

    public void SetEliminated(bool eliminated)
    {
        _eliminated = eliminated;
        // 탈락이면 카드 클릭 불가
        for (int i = 0; i < _cards.Count; i++)
            _cards[i].SetClickable(!eliminated && !_cards[i].IsFaceUp);

        // (선택) 텍스트 표시
        if (nameText)
        {
            if (eliminated && !nameText.text.Contains("(OUT)"))
                nameText.text += " (OUT)";
        }
    }

    public void EnsureCardCount(int count)
    {
        if (_cards.Count == count) return;
        BuildCards(count);
    }

    public void HighlightSelected(int selectedIndex)
    {
        for (int i = 0; i < _cards.Count; i++)
            _cards[i].SetSelected(i == selectedIndex);
    }

    public void ClearHighlight() => HighlightSelected(-1);

    private void BuildCards(int count)
    {
        ClearCards();

        for (int i = 0; i < count; i++)
        {
            var cv = Instantiate(cardPrefab, cardsRow);
            cv.SetIndex(i);
            cv.SetCardId("", false);
            cv.SetSelected(false);
            cv.SetClickable(true);
            cv.OnClicked += HandleCardClicked;
            _cards.Add(cv);
        }
    }

    public void ApplyPublicCards(List<PublicCardVM> publicCards)
    {
        if (publicCards == null) return;

        EnsureCardCount(publicCards.Count);

        for (int i = 0; i < _cards.Count; i++)
        {
            var info = publicCards.Find(c => c.idx == i);
            if (info == null)
            {
                _cards[i].SetColorHint("black");
                _cards[i].SetCardId("", false);
                _cards[i].SetClickable(!_eliminated);
                continue;
            }

            _cards[i].SetColorHint(info.color);

            if (info.revealed && !string.IsNullOrEmpty(info.cardId))
            {
                _cards[i].SetCardId(info.cardId, true);
                _cards[i].SetClickable(false); // ✅ 공개 카드는 클릭 불가
            }
            else
            {
                _cards[i].SetCardId("", false);
                _cards[i].SetClickable(!_eliminated); // ✅ 탈락자면 클릭 불가
            }
        }
    }

    private void HandleCardClicked(int cardIndex)
    {
        if (_eliminated) return;
        OnCardClicked?.Invoke(RelativeSeat, cardIndex);
    }

    private void ClearCards()
    {
        foreach (var c in _cards)
            if (c) Destroy(c.gameObject);
        _cards.Clear();
    }
}