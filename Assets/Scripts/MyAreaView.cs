using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MyAreaView : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Text myNameText;
    [SerializeField] private Transform myCardsRow;   // Horizontal Layout Group 붙은 컨테이너
    [SerializeField] private CardView cardPrefab;    // CardView 프리팹

    private readonly List<CardView> _cards = new();

    public void SetMyName(string name)
    {
        if (myNameText) myNameText.text = string.IsNullOrEmpty(name) ? "Me" : name;
    }

    // ✅ 기존 시그니처 유지 (다른 코드 깨지지 않게)
    public void RenderHand(IReadOnlyList<string> cardIds)
    {
        RenderHand(cardIds, null);
    }

    // ✅ InGame에서 사용: revealed까지 같이 받을 수 있게 오버로드
    public void RenderHand(IReadOnlyList<string> cardIds, IReadOnlyList<bool> revealed)
    {
        Clear();
        if (cardIds == null) return;

        // ✅ 정렬해서 보여주기 (내 UI만)
        var sorted = new List<string>(cardIds);
        sorted.Sort(CompareCardId);

        for (int i = 0; i < sorted.Count; i++)
        {
            var cv = Instantiate(cardPrefab, myCardsRow);
            cv.SetIndex(i);

            cv.SetCardId(sorted[i], faceUp: true);

            cv.SetSelected(false);
            cv.SetClickable(false);

            _cards.Add(cv);
        }
    }

    private static int CompareCardId(string a, string b)
    {
        ParseCard(a, out int na, out bool aBlack);
        ParseCard(b, out int nb, out bool bBlack);

        int c = na.CompareTo(nb);
        if (c != 0) return c;

        // black 먼저 => black은 -1, white는 +1
        if (aBlack == bBlack) return 0;
        return aBlack ? -1 : 1;
    }

    private static void ParseCard(string id, out int number, out bool isBlack)
    {
        number = -1;
        isBlack = true;

        if (string.IsNullOrEmpty(id)) return;

        isBlack = id.StartsWith("B", System.StringComparison.OrdinalIgnoreCase);

        if (id.Length >= 2 && int.TryParse(id.Substring(1), out int n))
            number = n;
    }

    public void Clear()
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            if (_cards[i]) Destroy(_cards[i].gameObject);
        }
        _cards.Clear();
    }
}