import type { SmartPlaylistRuleGroup } from '../../api/Music/smartPlaylistService';
import RuleTreeEditor from '../Common/RuleTreeEditor';
import { pruneRules, SMART_MEDIA_OPTIONS, type SmartCollectionMedia } from './smartCollectionMedia';

interface SmartCollectionRulesProps {
    idPrefix: string;
    // A global collection can match movies, shows or both; a library
    // collection matches whatever its library holds, so it gets no choice.
    isGlobal: boolean;
    media: SmartCollectionMedia;
    onMediaChange: (media: SmartCollectionMedia) => void;
    root: SmartPlaylistRuleGroup;
    onRootChange: (root: SmartPlaylistRuleGroup) => void;
}

const NOUN: Record<SmartCollectionMedia, string> = {
    Movies: 'movie',
    Shows: 'show',
    Mixed: 'movie and show'
};

export default function SmartCollectionRules({ idPrefix, isGlobal, media, onMediaChange, root, onRootChange }: SmartCollectionRulesProps) {
    const selectId = `${idPrefix}-smart-media`;
    const where = isGlobal ? 'your libraries' : 'this library';

    const changeMedia = (next: SmartCollectionMedia) => {
        onMediaChange(next);
        onRootChange(pruneRules(root, next));
    };

    return (
        <div className="pt-3 mt-3 border-t border-[var(--vora-border-subtle)]/50 space-y-3">
            {isGlobal && (
                <div className="max-w-xs">
                    <label htmlFor={selectId} className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Match</label>
                    <select
                        id={selectId}
                        value={media}
                        onChange={e => changeMedia(e.target.value as SmartCollectionMedia)}
                        className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none"
                    >
                        {SMART_MEDIA_OPTIONS.map(option => (
                            <option key={option.value} value={option.value}>{option.label}</option>
                        ))}
                    </select>
                </div>
            )}
            <p className="text-xs text-[var(--vora-text-muted)] leading-relaxed">
                Adds every <span className="text-[var(--vora-text-secondary)]">{NOUN[media]}</span> in {where} that matches the rules below,
                for example Genre contains Horror and Year between 2010 and 2019.
                {media !== 'Movies' && ' Shows are added whole, not episode by episode.'}
                {' '}The collection updates itself as {isGlobal ? 'your libraries change' : 'the library changes'}.
            </p>
            <RuleTreeEditor mediaType={media} value={root} onChange={onRootChange} />
        </div>
    );
}
