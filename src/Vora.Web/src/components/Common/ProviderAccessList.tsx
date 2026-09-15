import type { IptvPlaylistVM } from '../../api/Iptv/iptvAdminService';
import { groupProvidersByKind } from '../../utils/providerGroups';

// The checkbox list for choosing which Live TV and Radio providers someone can
// see, split into labelled Live TV and Radio sections. Shared by the admin's
// household access dialog and the profile editor so both read the same way.
interface ProviderAccessListProps {
    providers: IptvPlaylistVM[];
    selectedIds: string[];
    onToggle: (providerId: string) => void;
}

function ProviderGroup({ title, providers, selectedIds, onToggle }: { title: string } & ProviderAccessListProps) {
    if (providers.length === 0) return null;
    return (
        <fieldset className="m-0 border-0 p-0">
            <legend className="mb-2 p-0 text-[11px] font-bold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)' }}>{title}</legend>
            <div className="space-y-2.5">
                {providers.map(provider => (
                    <label key={provider.id} className="flex cursor-pointer select-none items-center gap-3">
                        <input
                            type="checkbox"
                            checked={selectedIds.includes(provider.id)}
                            onChange={() => onToggle(provider.id)}
                            className="h-4 w-4 accent-[var(--vora-accent-500)]"
                        />
                        <span className="text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }}>{provider.name}</span>
                    </label>
                ))}
            </div>
        </fieldset>
    );
}

export default function ProviderAccessList({ providers, selectedIds, onToggle }: ProviderAccessListProps) {
    if (providers.length === 0) {
        return <p className="py-2 text-center text-xs" style={{ color: 'var(--vora-text-disabled)' }}>No providers exist yet.</p>;
    }

    const groups = groupProvidersByKind(providers);
    return (
        <div className="space-y-4">
            <ProviderGroup title="Live TV" providers={groups.liveTv} selectedIds={selectedIds} onToggle={onToggle} />
            <ProviderGroup title="Radio" providers={groups.radio} selectedIds={selectedIds} onToggle={onToggle} />
        </div>
    );
}
