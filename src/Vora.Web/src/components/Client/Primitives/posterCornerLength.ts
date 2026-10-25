// A tile's corner box is drawn as a share of the tile's width: `px` is a
// length on a tile 192 wide, and this is that length on whatever tile the box
// sits in, read from the PosterCorner container around it.
export const POSTER_CORNER_REFERENCE_WIDTH = 192;

export function cornerLength(px: number): string {
    return `calc(100cqw * ${px} / ${POSTER_CORNER_REFERENCE_WIDTH})`;
}
