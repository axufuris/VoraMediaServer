using Vora.Domain.Enums;

namespace Vora.Application.Users.ViewModels;

public class LibrarySortVM
{
    public LibrarySortField? Sort { get; set; }
    public bool Descending { get; set; }
}
