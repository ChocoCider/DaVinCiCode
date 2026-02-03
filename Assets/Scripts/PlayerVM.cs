using System.Collections.Generic;

public class PlayerVM
{
    public string uid;
    public string displayName;
    public int seat;
    public bool ready;

    public bool eliminated;

    public List<PublicCardVM> publicCards = new();
}

public class PublicCardVM
{
    public int idx;
    public string color;
    public bool revealed;
    public string cardId;
}