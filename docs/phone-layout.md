# Phone layout

`Vora.Web` has one layout for phones and one for everything else. The breakpoint is Tailwind's `md` (48rem, 768px). Below it the browser gets the phone layout; at 48rem and wider it gets the desktop layout. A phone held sideways is usually wider than 48rem, so it gets the desktop layout.

## The rule: desktop never changes

Phone styles are additive. Every phone change is one of:

- a `max-md:` variant beside the existing classes (`px-8 max-md:px-4`, `grid-cols-7 max-md:hidden`);
- an element that only exists on phones (`md:hidden`), or a wrapper that disappears on desktop (`md:contents`, `contents max-md:hidden`);
- a token override inside the `@media (width < 48rem)` block in `styles/tokens.css`;
- a value picked in script with `useIsPhone()`, when a width is computed in JS rather than CSS.

Never edit a class or value that desktop uses: add the phone value next to it instead. Inline `style` beats a class, so to override an inline value on phones use the important modifier (`max-md:text-xs!`).

## The shells

Both shells wrap their sidebar in `components/Layout/PhoneNavDrawer.tsx`.

- **Desktop:** the wrapper is `display: contents`, so the sidebar sits in the layout exactly as before.
- **Phones:** the wrapper is a fixed panel that slides in from the left over a scrim (`z-[150]`, above the header).
  - It closes on the scrim, on Escape, or when a link inside it is followed.
  - Other controls inside it, such as Edit Navigation, leave it open.

The open buttons are `md:hidden`:

- client: in `MainLayoutUserMenu`;
- admin: `onToggleSidebar` on `TopAppBar`.

Both shells use `max-md:h-dvh`, so a phone's browser bar doesn't hide the bottom of the page.

## Tokens

On phones, the `@media (width < 48rem)` block in `styles/tokens.css` shrinks the card sizes and the gutter:

- `--vora-card-w-*`, `--vora-card-min-w*` and `--vora-card-gap`: a library grid fits three posters across, and a row shows the edge of the next card.
- `--vora-person-w*` and `--vora-video-w`: person and video tiles shrink to match.
- `--vora-row-gutter`: 1rem.

Page gutters that are classes follow the same 1rem: `px-8 max-md:px-4`.

## Patterns

- **Page headers:** both `PageHeader` primitives stack the title over the actions, and the actions wrap.
- **Tab bars:** the client `Tabs` and the admin `FeatureTabs` scroll sideways instead of wrapping.
- **Data tables:**
  - They keep their columns and scroll sideways inside their own `overflow-x-auto`.
  - A table that would squeeze its columns gets a phone minimum width (`max-md:min-w-[40rem]`).
- **Rows with a button group:** they wrap the buttons onto their own line (`max-md:basis-full` on the text, or `max-md:flex-wrap` on the row).
- **Calendar:** on phones, Week and Month render as a list of days (`RangeAgenda`). Month lists only the days that have releases.
- **Live TV guide:** the channel column is 88px on phones (`PHONE_CHANNEL_COLUMN_WIDTH`). Because the timeline width is computed from it, the guide reads it through `useIsPhone()`.
- **Players:**
  - The transport row and the tool row stack.
  - The volume slider is hidden on phones; the phone's own volume buttons do that job.
  - Mini players keep the title, play/pause, expand and close.
  - The queue and station lists open over the Now Playing screen instead of beside it.
- **Menus opened on a title:** holding a poster opens its menu as a sheet from the bottom edge, with taller rows (`ActionMenu`). See `docs/frontend-client-ui.md` → The title menu.

## Reordering by touch

The browser's drag and drop (`draggable`, `onDragStart`, `onDrop`) only works with a mouse. Every reorderable list therefore also uses `hooks/useTouchReorder.ts` for fingers and pens. Today that covers:

- Edit Navigation;
- manual and music playlists;
- Customize Discovery;
- the admin Discover rows;
- Smart Lists;
- Manual Sort Order on collections.

To add it to a list:

- Spread `reorderListProps` on the list container and `reorderRowProps(index)` on each row.
- Spread `handleProps(index)` on the row's drag handle, and give the handle the `touch-none` class so a drag on it doesn't scroll the page.
- Pass the list's move function to the hook. It is called once on release with `(from, to)`, the final index, the same move the mouse drop performs.
- `rowStyle(index)` fades the dragged row and draws the accent drop line. Lists that already draw their own line (Edit Navigation) can read `drag` instead.

The hook:

- ignores mouse pointers, so the mouse keeps the existing drag and drop;
- scrolls the list's scroll container when a finger nears its top or bottom edge;
- swallows the click that follows a real drag, so dropping a row doesn't also open it.

A list whose rows have no handle shows one only on touch screens: `hidden pointer-coarse:flex`.

## Checking a change

Look at the page at 390px wide, and at 360px for the narrowest common phone. Then confirm desktop is unchanged at 768px and wider: a screenshot before and after the change should match pixel for pixel.
