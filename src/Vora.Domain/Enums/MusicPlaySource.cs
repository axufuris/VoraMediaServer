namespace Vora.Domain.Enums;

// Where a play of a song was started from, as the client says. Recently Played
// shows a playlist or a mix once, as itself, and every other play as its song,
// so only those three are ever grouped; Album is kept for the record.
public enum MusicPlaySource
{
    None = 0,
    Album = 1,
    Playlist = 2,
    SmartPlaylist = 3,
    Mix = 4
}
