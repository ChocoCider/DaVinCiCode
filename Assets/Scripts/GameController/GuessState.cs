using UnityEngine;

public class GuessState : IGameState
{
    private readonly GameContext _ctx;
    private readonly GameRepository _repo;

    public GuessState(GameContext ctx, GameRepository repo) { _ctx = ctx; _repo = repo; }

    public void Enter()
    {
        _ctx.UI.SetActionButtons(
            canDraw: false,
            canGuess: _ctx.IsMyTurn,
            showGuessChoice: false
        );
        _ctx.UI.HideGuessPopup();
    }

    public void Exit() { }

    public void OnDrawClicked()
    {
        _ctx.UI.LogLocal("Draw는 이미 했습니다. 반드시 Guess 하세요.");
    }

    public void OnGuessClicked()
    {
        _ctx.UI.LogLocal("상대 카드(뒷면)를 클릭해서 추리하세요.");
    }

    public void OnOpponentCardClicked(int relativeSeat, int cardIndex)
    {
        if (!_ctx.IsMyTurn) return;

        int targetSeat = (relativeSeat == 1) ? ((_ctx.MySeat + 1) % 3) : ((_ctx.MySeat + 2) % 3);
        var target = _ctx.GetPlayerBySeat(targetSeat);
        if (target == null) return;

        if (target.eliminated)
        {
            _ctx.UI.LogLocal("이미 탈락한 상대입니다.");
            return;
        }

        var info = target.publicCards?.Find(x => x.idx == cardIndex);
        if (info != null && info.revealed)
        {
            _ctx.UI.LogLocal("이미 공개된 카드는 선택할 수 없습니다.");
            return;
        }

        _ctx.UI.HighlightOpponentSelection(relativeSeat, cardIndex);

        string targetName = target.displayName;
        _ctx.UI.ShowGuessPopup(targetName, relativeSeat, cardIndex);
    }

    public async void OnGuessSubmit(GuessRequest req)
    {
        if (!_ctx.IsMyTurn) return;

        int targetSeat = (req.targetRelativeSeat == 1)
            ? ((_ctx.MySeat + 1) % 3)
            : ((_ctx.MySeat + 2) % 3);

        await _repo.GuessAsync(
            targetSeat: targetSeat,
            cardIndex: req.targetCardIndex,
            guessNumber: req.guessNumber,
            guessIsBlack: req.guessIsBlack
        );
    }

    public void OnContinueGuess() { }
    public void OnEndTurn() { }
}