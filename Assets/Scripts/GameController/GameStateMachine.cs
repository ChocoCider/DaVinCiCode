using UnityEngine;

public class GameStateMachine
{
    private readonly GameContext _ctx;
    private readonly GameRepository _repo;

    private IGameState _current;

    public GameStateMachine(GameContext ctx, GameRepository repo)
    {
        _ctx = ctx;
        _repo = repo;
    }

    public void SyncToPhase(string phase)
    {
        if (phase == GamePhases.Draw) ChangeState(new DrawState(_ctx, _repo));
        else if (phase == GamePhases.MustGuess) ChangeState(new GuessState(_ctx, _repo));
        else if (phase == GamePhases.GuessChoice) ChangeState(new ChoiceState(_ctx, _repo));
        else if (phase == GamePhases.Finished) ChangeState(new FinishedState(_ctx));
        else ChangeState(new DrawState(_ctx, _repo));
    }

    private void ChangeState(IGameState next)
    {
        _current?.Exit();
        _current = next;
        _current.Enter();

        Debug.Log($"[StateMachine] -> {(_current?.GetType().Name ?? "null")}, phase={_ctx.Phase}, myTurn={_ctx.IsMyTurn}");
    }

    public void OnDrawClicked() => _current?.OnDrawClicked();
    public void OnGuessClicked() => _current?.OnGuessClicked();

    public void OnOpponentCardClicked(int relativeSeat, int cardIndex)
    {
        Debug.Log($"[UI->SM] OpponentCardClicked rel={relativeSeat} idx={cardIndex} state={_current?.GetType().Name} phase={_ctx.Phase} myTurn={_ctx.IsMyTurn}");
        _current?.OnOpponentCardClicked(relativeSeat, cardIndex);
    }

    public void OnGuessSubmit(GuessRequest req) => _current?.OnGuessSubmit(req);
    public void OnContinueGuess() => _current?.OnContinueGuess();
    public void OnEndTurn() => _current?.OnEndTurn();
}