public class DrawState : IGameState
{
    private readonly GameContext _ctx;
    private readonly GameRepository _repo;

    public DrawState(GameContext ctx, GameRepository repo) { _ctx = ctx; _repo = repo; }

    public void Enter()
    {
        bool myElim = _ctx.Me != null && _ctx.Me.eliminated;

        bool canDraw = _ctx.IsMyTurn && !myElim && _ctx.DeckCount > 0;
        bool canGuess = _ctx.IsMyTurn && !myElim; // 덱 0이어도 guess는 가능

        _ctx.UI.SetActionButtons(
            canDraw: canDraw,
            canGuess: canGuess,
            showGuessChoice: false
        );

        _ctx.UI.HideGuessPopup();

        if (_ctx.IsMyTurn && !myElim && _ctx.DeckCount == 0)
        {
            _ = _repo.ForceMustGuessWhenDeckEmptyAsync();
        }
    }

    public void Exit() { }

    public async void OnDrawClicked()
    {
        if (!_ctx.IsMyTurn) return;
        if (_ctx.Me != null && _ctx.Me.eliminated) return;

        await _repo.DrawAsync();
    }

    public async void OnGuessClicked()
    {
        if (!_ctx.IsMyTurn) return;
        if (_ctx.Me != null && _ctx.Me.eliminated) return;

        if (_ctx.DeckCount == 0)
        {
            await _repo.ForceMustGuessWhenDeckEmptyAsync();
            _ctx.UI.LogLocal("Deck is empty. Draw skipped. You must guess.");
            return;
        }

        _ctx.UI.LogLocal("Draw 먼저 해야 합니다.");
    }

    public void OnOpponentCardClicked(int relativeSeat, int cardIndex)
        => _ctx.UI.LogLocal("Draw 먼저 해야 합니다.");

    public void OnGuessSubmit(GuessRequest req) { }
    public void OnContinueGuess() { }
    public void OnEndTurn() { }
}