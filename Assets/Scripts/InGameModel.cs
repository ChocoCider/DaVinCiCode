using System.Collections.Generic;

public static class CardIdUtil
{
    // "B07" -> color="black", number=7
    public static void Parse(string cardId, out string color, out int number)
    {
        color = "black";
        number = 0;

        if (string.IsNullOrEmpty(cardId) || cardId.Length < 2) return;

        color = cardId.StartsWith("W") ? "white" : "black";
        int.TryParse(cardId.Substring(1), out number);
    }

    public static string ToCardId(string color, int number)
    {
        return (color == "white" ? "W" : "B") + number.ToString("00");
    }
}

public class HandDoc
{
    public int seat;
    public List<string> cardIds = new();
    public List<bool> revealed = new();
}