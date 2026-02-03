public class FinishedState : IGameState
{
    private readonly GameContext _ctx;

    public FinishedState(GameContext ctx) { _ctx = ctx; }

    public void Enter()
    {
        _ctx.UI.SetActionButtons(false, false, false);
        _ctx.UI.HideGuessPopup();

        string winnerName = "Unknown";
        if (!string.IsNullOrEmpty(_ctx.WinnerUid) && _ctx.PlayersByUid.TryGetValue(_ctx.WinnerUid, out var p))
            winnerName = p.displayName;

        _ctx.UI.LogLocal($"게임 종료! 승자: {winnerName}");
    }

    public void Exit() { }

    public void OnDrawClicked() { }
    public void OnGuessClicked() { }
    public void OnOpponentCardClicked(int relativeSeat, int cardIndex) { }
    public void OnGuessSubmit(GuessRequest req) { }
    public void OnContinueGuess() { }
    public void OnEndTurn() { }
}