import type { SetupGuideVM } from '../../../api/System/setupGuideService';

export type SetupStepId =
    | 'welcome' | 'server' | 'playback' | 'content'
    | 'remote' | 'signup' | 'email'
    | 'metadata' | 'artwork' | 'ratings' | 'detection' | 'thumbnails' | 'requests' | 'subtitles' | 'discover'
    | 'livetv' | 'radio' | 'podcasts'
    | 'lastfm' | 'lyrics'
    | 'ai' | 'backups' | 'done';

export interface SetupStep {
    id: SetupStepId;
    group: string;
    title: string;
}

export type SetupContent = Pick<SetupGuideVM, 'moviesAndShows' | 'music' | 'liveTv' | 'internetRadio' | 'podcasts'>;

export const EMAIL_INVITATION_MODE = 3;

export interface SetupStepOptions {
    discover: boolean;
    email: boolean;
}

const ALL_IDS: readonly SetupStepId[] = [
    'welcome', 'server', 'playback', 'content', 'remote', 'signup', 'email', 'metadata', 'artwork', 'ratings', 'detection', 'thumbnails', 'requests',
    'subtitles', 'discover', 'livetv', 'radio', 'podcasts', 'lastfm', 'lyrics', 'ai', 'backups', 'done',
];

export const isSetupStepId = (value: string | null | undefined): value is SetupStepId =>
    !!value && (ALL_IDS as readonly string[]).includes(value);

export function buildSetupSteps(content: SetupContent, options: SetupStepOptions): SetupStep[] {
    const steps: SetupStep[] = [
        { id: 'welcome', group: '', title: 'Welcome' },
        { id: 'server', group: 'Basics', title: 'Your server' },
        { id: 'playback', group: 'Basics', title: 'Playback' },
        { id: 'content', group: 'Basics', title: "What you'll add" },
        { id: 'remote', group: 'Access', title: 'Remote access' },
        { id: 'signup', group: 'Access', title: 'Sign-ups' },
    ];
    if (options.email) steps.push({ id: 'email', group: 'Access', title: 'Email' });
    if (content.moviesAndShows) {
        steps.push(
            { id: 'metadata', group: 'Movies & TV', title: 'Metadata' },
            { id: 'artwork', group: 'Movies & TV', title: 'Artwork' },
            { id: 'ratings', group: 'Movies & TV', title: 'Ratings' },
            { id: 'detection', group: 'Movies & TV', title: 'Skip intro & credits' },
            { id: 'thumbnails', group: 'Movies & TV', title: 'Preview thumbnails' },
            { id: 'requests', group: 'Movies & TV', title: 'Requests' },
            { id: 'subtitles', group: 'Movies & TV', title: 'Subtitles' },
        );
        if (options.discover) steps.push({ id: 'discover', group: 'Movies & TV', title: 'Discover' });
    }
    if (content.liveTv) steps.push({ id: 'livetv', group: 'Live TV & radio', title: 'Live TV' });
    if (content.internetRadio) steps.push({ id: 'radio', group: 'Live TV & radio', title: 'Internet radio' });
    if (content.podcasts) steps.push({ id: 'podcasts', group: 'Podcasts', title: 'Podcasts' });
    if (content.music) {
        steps.push(
            { id: 'lastfm', group: 'Music', title: 'Last.fm' },
            { id: 'lyrics', group: 'Music', title: 'Lyrics' },
        );
    }
    steps.push(
        { id: 'ai', group: 'AI', title: 'AI features' },
        { id: 'backups', group: 'Backups', title: 'Backups' },
        { id: 'done', group: '', title: 'Done' },
    );
    return steps;
}

export function nearestStep(steps: SetupStep[], wanted: SetupStepId): SetupStepId {
    if (steps.some(s => s.id === wanted)) return wanted;
    const order = ALL_IDS.indexOf(wanted);
    const after = steps.find(s => ALL_IDS.indexOf(s.id) > order);
    return after?.id ?? 'done';
}

export const SKIPPABLE_STEPS: readonly SetupStepId[] = [
    'remote', 'signup', 'email',
    'metadata', 'artwork', 'ratings', 'detection', 'thumbnails', 'requests', 'subtitles', 'discover',
    'livetv', 'radio', 'podcasts', 'lastfm', 'lyrics', 'ai', 'backups',
];
