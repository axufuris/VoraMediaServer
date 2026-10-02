namespace Vora.Application.Tasks;

[Flags]
public enum LibraryThumbnailReason
{
    None = 0,
    Addition = 1,
    Schedule = 2,
    Manual = 4,
    Force = 8
}
