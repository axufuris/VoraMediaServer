namespace Vora.Application.Users.ViewModels;

public class PlayHistoryPageVM
{
    public List<UserProfileHistoryDto> Data { get; set; } = new();
    public int Total { get; set; }
}
