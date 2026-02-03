using System;
using UnityEngine;

public enum SeatAnchor
{
    Bottom,
    Left,
    Right,
    Top
}

public static class SeatMapper
{
    /// <summary>
    /// mySeat 기준으로 seat를 회전시켜 "내 화면 기준 상대 위치"를 만든다.
    /// 반환 rel: 0=Bottom, 1=Left, 2=Right, 3=Top(4인일 때)
    /// </summary>
    public static int ToRelativeSeat(int seat, int mySeat, int maxPlayers)
    {
        if (maxPlayers <= 0) return seat;
        int rel = (seat - mySeat) % maxPlayers;
        if (rel < 0) rel += maxPlayers;
        return rel;
    }

    /// <summary>
    /// 상대 위치를 실제 UI 앵커로 매핑.
    /// 3인: 0 Bottom, 1 Left, 2 Right
    /// 4인: 0 Bottom, 1 Left, 2 Top, 3 Right (원하면 순서 바꿔도 됨)
    /// </summary>
    public static SeatAnchor ToAnchor(int relativeSeat, int maxPlayers)
    {
        if (maxPlayers == 3)
        {
            return relativeSeat switch
            {
                0 => SeatAnchor.Bottom,
                1 => SeatAnchor.Left,
                2 => SeatAnchor.Right,
                _ => SeatAnchor.Bottom
            };
        }

        if (maxPlayers == 4)
        {
            return relativeSeat switch
            {
                0 => SeatAnchor.Bottom,
                1 => SeatAnchor.Left,
                2 => SeatAnchor.Top,
                3 => SeatAnchor.Right,
                _ => SeatAnchor.Bottom
            };
        }

        // 2인/기타(임시)
        return relativeSeat == 0 ? SeatAnchor.Bottom : SeatAnchor.Top;
    }
}