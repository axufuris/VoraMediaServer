// 0 hides every automatic (TMDB) collection in the library; 1-25 hides the
// automatic ones with fewer movies than that. Collections an admin created
// always show.
export default function MinimumCollectionSizeField({ value, onChange }: { value: number; onChange: (next: number) => void }) {
    return (
        <div>
            <label htmlFor="minimum-collection-size" className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">Minimum Collection Size</label>
            <select
                id="minimum-collection-size"
                value={value}
                onChange={e => onChange(Number(e.target.value))}
                className="vora-input cursor-pointer"
            >
                <option value={0}>Hide automatic collections</option>
                {Array.from({ length: 25 }, (_, i) => i + 1).map(num => (
                    <option key={num} value={num}>{num}</option>
                ))}
            </select>
            <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">
                Automatic collections (from TMDB) with fewer movies than this are hidden from viewers. Collections you create always show. Admins can still see hidden ones on the Collections tab.
            </p>
        </div>
    );
}
