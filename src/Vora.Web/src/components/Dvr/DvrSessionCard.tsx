import type { IptvRecordingSessionVM } from '../../api/Iptv/dvrService';

interface DvrSessionCardProps {
    session: IptvRecordingSessionVM;
    activeTab: 'Completed' | 'Upcoming' | 'Failed';
    playingId: string | null;
    formatTime: (dateStr: string) => string;
    getDurationString: (start: string, end: string) => string;
    getStatusColor: (status: string) => string;
    onPlay: (session: IptvRecordingSessionVM) => Promise<void>;
    onDelete: (session: IptvRecordingSessionVM) => void;
}

export default function DvrSessionCard({
    session,
    activeTab,
    playingId,
    formatTime,
    getDurationString,
    getStatusColor,
    onPlay,
    onDelete
}: DvrSessionCardProps) {
    return (
        <div className="bg-gray-800/40 border border-gray-700 rounded-lg overflow-hidden flex flex-col group min-h-[220px] shrink-0 shadow-md">
            <div className="p-4 border-b border-gray-700 bg-gray-800/80 flex items-start gap-4">
                <div className="w-16 h-12 bg-gray-900 rounded flex items-center justify-center shrink-0 border border-gray-700 p-1">
                    {session.schedule?.channel?.logoUrl ? (
                        <img src={session.schedule.channel.logoUrl} className="max-w-full max-h-full object-contain" />
                    ) : (
                        <span className="text-[10px] text-gray-600 font-bold">No Logo</span>
                    )}
                </div>
                <div className="flex-1 overflow-hidden">
                    <h3 className="font-bold text-white text-lg truncate">{session.title}</h3>
                    {session.episodeTitle && (
                        <p className="text-sm text-gray-400 truncate">
                            {session.seasonNumber && session.episodeNumber
                                ? `S${session.seasonNumber} E${session.episodeNumber} - ${session.episodeTitle}`
                                : session.episodeTitle}
                        </p>
                    )}
                </div>
            </div>

            <div className="p-4 flex-1 flex flex-col justify-between gap-4">
                <div>
                    <p className="text-sm text-gray-400 mb-1">
                        <span className="font-bold text-gray-300">Channel:</span> {session.schedule?.channel?.name || "Unknown"}
                    </p>
                    <p className="text-sm text-gray-400 mb-3">
                        <span className="font-bold text-gray-300">Airing:</span> {formatTime(session.startTime)}
                    </p>
                    <div className="flex items-center gap-3">
                        <span className={`text-xs font-bold px-2 py-1 rounded border ${getStatusColor(session.status)}`}>
                            {session.status}
                        </span>
                        <span className="text-xs text-gray-500 font-bold bg-gray-900/50 px-2 py-1 rounded border border-gray-700">
                            {getDurationString(session.startTime, session.endTime)}
                        </span>
                        {session.fileSizeBytes ? (
                            <span className="text-xs text-gray-500 font-bold bg-gray-900/50 px-2 py-1 rounded border border-gray-700">
                                {(session.fileSizeBytes / (1024 * 1024 * 1024)).toFixed(2)} GB
                            </span>
                        ) : null}
                    </div>
                </div>

                {session.errorMessage && (
                    <div className="bg-red-900/20 border border-red-900/50 p-2 rounded text-xs text-red-400 line-clamp-2" title={session.errorMessage}>
                        {session.errorMessage}
                    </div>
                )}

                {activeTab === 'Completed' && (
                    <>
                        {session.status === 'Completed' || session.status === 'Completed (Partial)' ? (
                            <button
                                disabled={playingId === session.id}
                                onClick={() => onPlay(session)}
                                className="w-full py-2 bg-orange-600 hover:bg-orange-500 disabled:bg-orange-800 disabled:text-gray-400 text-white font-bold rounded flex items-center justify-center gap-2 transition-colors cursor-pointer"
                            >
                                {playingId === session.id ? (
                                    <><svg className="animate-spin w-4 h-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24"><circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle><path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path></svg> Starting...</>
                                ) : (
                                    <><svg className="w-4 h-4 fill-current" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg> Watch Now</>
                                )}
                            </button>
                        ) : session.status === 'Post-Processing' ? (
                            <button disabled className="w-full py-2 bg-gray-700 text-gray-400 font-bold rounded cursor-not-allowed flex items-center justify-center gap-2">
                                <svg className="animate-spin h-4 w-4 text-gray-400" fill="none" viewBox="0 0 24 24"><circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle><path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path></svg>
                                Processing Video...
                            </button>
                        ) : (
                            <button disabled className="w-full py-2 bg-gray-800 text-gray-500 font-bold rounded cursor-not-allowed">Unavailable</button>
                        )}
                    </>
                )}

                <button
                    disabled={session.status === 'Recording'}
                    onClick={() => onDelete(session)}
                    className="w-full py-2 bg-gray-800 hover:bg-red-600 disabled:opacity-50 disabled:hover:bg-gray-800 text-white font-bold rounded flex items-center justify-center gap-2 transition-colors mt-2 cursor-pointer"
                >
                    <svg className="w-4 h-4 fill-current" viewBox="0 0 24 24"><path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z" /></svg>
                    {activeTab === 'Upcoming' ? 'Cancel Recording' : 'Delete Recording'}
                </button>
            </div>
        </div>
    );
}
