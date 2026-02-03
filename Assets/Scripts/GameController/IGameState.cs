public interface IGameState
{
    void Enter();
    void Exit();

    void OnDrawClicked();
    void OnGuessClicked();                 // 버튼용(안내 메시지 등)
    void OnOpponentCardClicked(int relativeSeat, int cardIndex);
    void OnGuessSubmit(GuessRequest req);

    void OnContinueGuess();                // GuessChoice에서 사용
    void OnEndTurn();                      // GuessChoice에서 사용
}