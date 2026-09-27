namespace Vora.Application.Actors.Requests;

public class CreateActorRequest
{
    public required string Name { get; set; }
    public string? ProfileImageUrl { get; set; }
}