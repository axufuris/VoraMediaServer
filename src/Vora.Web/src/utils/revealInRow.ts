// Scrolls a horizontal row so `item` is in view, centring it when it starts
// off-screen. Only the row moves: scrollIntoView would also scroll the page to
// the row, which on a season page means jumping past the hero on arrival.
export function revealInRow(scroller: HTMLElement, item: HTMLElement): void {
    const row = scroller.getBoundingClientRect();
    const box = item.getBoundingClientRect();
    if (box.left >= row.left && box.right <= row.right) return;
    scroller.scrollLeft += box.left - row.left - (row.width - box.width) / 2;
}
