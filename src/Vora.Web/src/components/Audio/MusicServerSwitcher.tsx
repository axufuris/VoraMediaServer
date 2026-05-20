import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { serverVault, type VoraServer } from '../../utils/serverVault';
import { musicService } from '../../api/Music/musicService';

export default function MusicServerSwitcher() {
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const [serversWithMusic, setServersWithMusic] = useState<VoraServer[]>([]);

    useEffect(() => {
        let cancelled = false;
        const probe = async () => {
            const servers = serverVault.getServers();
            if (servers.length < 2) {
                if (!cancelled) setServersWithMusic([]);
                return;
            }

            const probes = await Promise.allSettled(
                servers.map(s => musicService.getArtists(undefined, s.id, { limit: 1 }).then(rows => ({ server: s, hasMusic: rows.length > 0 })))
            );

            const withMusic: VoraServer[] = [];
            for (const p of probes) {
                if (p.status === 'fulfilled' && p.value.hasMusic) {
                    withMusic.push(p.value.server);
                }
            }

            if (!cancelled) setServersWithMusic(withMusic);
        };

        probe();
        return () => { cancelled = true; };
    }, []);

    if (serversWithMusic.length < 2) return null;

    const activeId = serverId ?? serverVault.getActiveServerId() ?? undefined;

    return (
        <div className="flex flex-wrap items-center gap-2 mb-4">
            <span className="text-xs font-bold uppercase tracking-widest text-gray-500 mr-1">Server</span>
            {serversWithMusic.map(s => {
                const isActive = s.id === activeId;
                return (
                    <button
                        key={s.id}
                        onClick={() => navigate(`/server/${s.id}/audio`)}
                        className={`px-3 py-1.5 text-xs font-bold rounded transition-colors cursor-pointer ${isActive ? 'bg-orange-600 text-white' : 'bg-gray-900/60 text-gray-400 hover:text-white hover:bg-gray-800'}`}
                    >
                        {s.name}
                    </button>
                );
            })}
        </div>
    );
}
