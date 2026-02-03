public class ChoiceState : IGameState
{
    private readonly GameContext _ctx;
    private readonly GameRepository _repo;

    public ChoiceState(GameContext ctx, GameRepository repo) { _ctx = ctx; _repo = repo; }

    public void Enter()
    {
        bool myElim = _ctx.Me != null && _ctx.Me.eliminated;

        _ctx.UI.SetActionButtons(
            canDraw: false,
            canGuess: false,
            showGuessChoice: _ctx.IsMyTurn && !myElim
        );
        _ctx.UI.HideGuessPopup();
    }

    public void Exit() { }

    public void OnDrawClicked() { }
    public void OnGuessClicked() { }
    public void OnOpponentCardClicked(int relativeSeat, int cardIndex) { }
    public void OnGuessSubmit(GuessRequest req) { }

    public async void OnContinueGuess()
    {
        if (!_ctx.IsMyTurn) return;
        if (_ctx.Me != null && _ctx.Me.eliminated) return;
        await _repo.ContinueGuessAsync();
    }

    public async void OnEndTurn()
    {
        if (!_ctx.IsMyTurn) return;
        if (_ctx.Me != null && _ctx.Me.eliminated) return;
        await _repo.EndTurnAsync();
    }
}