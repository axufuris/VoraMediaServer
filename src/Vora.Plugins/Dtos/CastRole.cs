namespace Vora.Plugins.Dtos;

[Flags]
public enum CastRole
{
    None = 0,
    Actor = 1 << 0,
    Director = 1 << 1,
    Writer = 1 << 2,
    Producer = 1 << 3,
    Creator = 1 << 4
}
