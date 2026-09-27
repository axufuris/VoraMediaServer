namespace Vora.Plugins.Dtos;

// What a piece of music artwork actually IS. Providers return several kinds from
// one lookup — Fanart.tv alone gives artistthumb, artistbackground, musiclogo and
// musicbanner — and without this they arrived as one undifferentiated list. The
// consumer took the first and assigned it to the artist image, so backgrounds,
// banners and logos were fetched and thrown away, and wordmark logos were offered
// as candidate artist photos.
//
// Unknown is the default so a provider that does not classify its results keeps
// working: the consumer treats Unknown as the primary image for whatever it is
// looking up (the artist photo, or the album cover).
public enum MusicArtworkKind
{
    Unknown = 0,
    Thumb = 1,
    Cover = 2,
    Background = 3,
    Banner = 4,
    Logo = 5,
}
