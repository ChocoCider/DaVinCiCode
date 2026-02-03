using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameUIController : MonoBehaviour
{
    [Header("TopHUD")]
    [SerializeField] private Text turnText;
    [SerializeField] private Text phaseText;
    [SerializeField] private Text deckCountText;
    [SerializeField] private Text logText;

    [Header("MyArea")]
    [SerializeField] private MyAreaView myArea;

    [Header("Opponents")]
    [SerializeField] private OpponentAreaView leftOpponent;
    [SerializeField] private OpponentAreaView rightOpponent;

    [Header("ActionBar")]
    [SerializeField] private Button drawButton;
    [SerializeField] private Button guessButton;

    [Header("GuessChoiceBar")]
    [SerializeField] private GameObject guessChoiceRoot;
    [SerializeField] private Button continueGuessButton;
    [SerializeField] private Button endTurnButton;

    [Header("Popups")]
    [SerializeField] private GuessPopupView guessPopup;

    public event Action OnDrawClicked;
    public event Action OnGuessClicked;
    public event Action<GuessRequest> OnGuessSubmit;

    public event Action<int, int> OnOpponentCardClicked;
    public event Action OnContinueGuessClicked;
    public event Action OnEndTurnClicked;

    private void Awake()
    {
        if (drawButton) drawButton.onClick.AddListener(() => OnDrawClicked?.Invoke());
        if (guessButton) guessButton.onClick.AddListener(() => OnGuessClicked?.Invoke());

        if (continueGuessButton) continueGuessButton.onClick.AddListener(() => OnContinueGuessClicked?.Invoke());
        if (endTurnButton) endTurnButton.onClick.AddListener(() => OnEndTurnClicked?.Invoke());

        if (leftOpponent) leftOpponent.OnCardClicked += (rs, idx) => OnOpponentCardClicked?.Invoke(rs, idx);
        if (rightOpponent) rightOpponent.OnCardClicked += (rs, idx) => OnOpponentCardClicked?.Invoke(rs, idx);

        if (guessPopup)
        {
            guessPopup.OnSubmit += req => OnGuessSubmit?.Invoke(req);
            guessPopup.OnCancel += () => HideGuessPopup();
        }

        if (guessChoiceRoot) guessChoiceRoot.SetActive(false);
        HideGuessPopup();
    }

    public void SetHeader(string phase, string turnPlayerName, int deckCount, string lastLog)
    {
        if (phaseText) phaseText.text = $"Phase: {phase}";
        if (turnText) turnText.text = $"Turn: {turnPlayerName}";
        if (deckCountText) deckCountText.text = $"Deck: {deckCount}";
        if (logText) logText.text = lastLog ?? "";
    }

    public void SetMyName(string name) => myArea?.SetMyName(name);

    public void RenderMyHand(List<string> myCardIds, List<bool> revealed = null)
    {
        if (revealed == null) myArea?.RenderHand(myCardIds);
        else myArea?.RenderHand(myCardIds, revealed);
    }

    public void SetupOpponents(string leftName, int leftCount, string rightName, int rightCount)
    {
        if (leftOpponent)
        {
            leftOpponent.SetHeader(leftName, relativeSeat: 1);
            leftOpponent.EnsureCardCount(leftCount);
        }
        if (rightOpponent)
        {
            rightOpponent.SetHeader(rightName, relativeSeat: 2);
            rightOpponent.EnsureCardCount(rightCount);
        }
    }

    public void ApplyOpponentPublicCards(int relativeSeat, List<PublicCardVM> publicCards)
    {
        if (relativeSeat == 1) leftOpponent?.ApplyPublicCards(publicCards);
        if (relativeSeat == 2) rightOpponent?.ApplyPublicCards(publicCards);
    }

    public void SetOpponentEliminated(int relativeSeat, bool eliminated)
    {
        if (relativeSeat == 1) leftOpponent?.SetEliminated(eliminated);
        if (relativeSeat == 2) rightOpponent?.SetEliminated(eliminated);
    }

    public void SetActionButtons(bool canDraw, bool canGuess, bool showGuessChoice)
    {
        if (drawButton) drawButton.interactable = canDraw;
        if (guessButton) guessButton.interactable = canGuess;

        if (guessChoiceRoot) guessChoiceRoot.SetActive(showGuessChoice);
        if (continueGuessButton) continueGuessButton.interactable = showGuessChoice;
        if (endTurnButton) endTurnButton.interactable = showGuessChoice;
    }

    public void ShowGuessPopup(string targetName, int relativeSeat, int cardIndex)
    {
        if (guessPopup == null)
        {
            Debug.LogError("[UI] guessPopup is NULL (Inspector 연결 필요)");
            return;
        }
        guessPopup.Show(targetName, relativeSeat, cardIndex);
        guessPopup.transform.SetAsLastSibling();
    }

    public void HideGuessPopup()
    {
        if (guessPopup == null) return;
        guessPopup.Hide();
    }

    public void LogLocal(string msg)
    {
        if (logText) logText.text = msg ?? "";
        Debug.Log(msg);
    }

    public void HighlightOpponentSelection(int relativeSeat, int cardIndex)
    {
        if (relativeSeat == 1)
        {
            leftOpponent?.HighlightSelected(cardIndex);
            rightOpponent?.ClearHighlight();
        }
        else if (relativeSeat == 2)
        {
            rightOpponent?.HighlightSelected(cardIndex);
            leftOpponent?.ClearHighlight();
        }
        else
        {
            leftOpponent?.ClearHighlight();
            rightOpponent?.ClearHighlight();
        }
    }

    public void ClearOpponentSelection()
    {
        leftOpponent?.ClearHighlight();
        rightOpponent?.ClearHighlight();
    }
}