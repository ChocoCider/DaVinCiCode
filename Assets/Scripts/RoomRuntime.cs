public static class RoomRuntime
{
    public static string CurrentRoomId { get; private set; }

    public static bool HasRoom => !string.IsNullOrWhiteSpace(CurrentRoomId);

    public static void SetRoom(string roomId)
    {
        CurrentRoomId = string.IsNullOrWhiteSpace(roomId) ? null : roomId.Trim();
    }

    public static void Clear()
    {
        CurrentRoomId = null;
    }
}