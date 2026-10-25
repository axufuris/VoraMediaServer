// A tile's corner box is drawn as a share of the tile's width: `px` is a
// length on a tile 128 wide, and this is that length on whatever tile the box
// sits in, read from the PosterCorner container around it.
export const POSTER_CORNER_REFERENCE_WIDTH = 128;

// A landscape tile (a 16:9 still) is barely half as tall as it is wide, so the
// box measured on its whole width stood taller than the overlay badges baked
// into it. It is measured on this share of the width instead.
export const LANDSCAPE_CORNER_SHARE = 0.75;

export function cornerLength(px: number): string {
    return `calc(100cqw * ${px} / ${POSTER_CORNER_REFERENCE_WIDTH})`;
}
