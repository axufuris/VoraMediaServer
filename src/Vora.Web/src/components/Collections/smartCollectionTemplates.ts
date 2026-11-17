import type { SmartPlaylistField, SmartPlaylistOperator, SmartPlaylistRule, SmartPlaylistRuleGroup } from '../../api/Music/smartPlaylistService';

export type SmartTemplateGroup = 'Holidays' | 'Popular' | 'Genres' | 'Decades' | 'Ratings';

export interface SmartCollectionTemplate {
    id: string;
    group: SmartTemplateGroup;
    label: string;
    note?: string;
    build: (today: Date) => SmartPlaylistRuleGroup;
}

export const SMART_TEMPLATE_GROUPS: SmartTemplateGroup[] = ['Holidays', 'Popular', 'Genres', 'Decades', 'Ratings'];

const rule = (field: SmartPlaylistField, operator: SmartPlaylistOperator, value: string, secondValue?: string): SmartPlaylistRule =>
    secondValue === undefined ? { field, operator, value } : { field, operator, value, secondValue };

const group = (match: 'All' | 'Any', rules: SmartPlaylistRule[], groups: SmartPlaylistRuleGroup[] = []): SmartPlaylistRuleGroup => ({ match, rules, groups });

const titleHas = (...words: string[]) => words.map(word => rule('Title', 'Contains', word));
const genreHas = (...names: string[]) => names.map(name => rule('Genre', 'Contains', name));
const ratingIs = (...ratings: string[]) => ratings.map(value => rule('ContentRating', 'Equals', value));

const HOLIDAY_NOTE = 'A holiday has no date or tag to match on, so this looks for words in the title and finds few titles. For a fuller list, set the List Provider to AI Theme instead.';
const RATING_NOTE = 'The rating cut-offs assume scores out of 10, as TMDB and IMDb give.';

const holiday = (id: string, label: string, rules: SmartPlaylistRule[]): SmartCollectionTemplate => ({
    id, group: 'Holidays', label, note: HOLIDAY_NOTE, build: () => group('Any', rules.map(r => ({ ...r }))),
});

const genre = (id: string, label: string, rules: SmartPlaylistRule[], note?: string): SmartCollectionTemplate => ({
    id, group: 'Genres', label, note, build: () => group('Any', rules.map(r => ({ ...r }))),
});

const decade = (start: number): SmartCollectionTemplate => ({
    id: `${start}s`,
    group: 'Decades',
    label: `${start}s`,
    build: () => group('All', [rule('ReleaseYear', 'Between', String(start), String(start + 9))]),
});

const rated = (id: string, label: string, value: string): SmartCollectionTemplate => ({
    id, group: 'Ratings', label, build: () => group('All', [rule('ContentRating', 'Equals', value)]),
});

const popular = (id: string, label: string, build: (today: Date) => SmartPlaylistRuleGroup, note?: string): SmartCollectionTemplate => ({
    id, group: 'Popular', label, note, build,
});

export const SMART_COLLECTION_TEMPLATES: SmartCollectionTemplate[] = [
    holiday('new-years', "New Year's", titleHas('New Year')),
    holiday('lunar-new-year', 'Lunar New Year', titleHas('Lunar New Year', 'Chinese New Year', 'Spring Festival')),
    holiday('valentines-day', "Valentine's Day", titleHas('Valentine')),
    holiday('presidents-day', "Presidents' Day", titleHas('President')),
    holiday('st-patricks-day', "St. Patrick's Day", titleHas('Patrick', 'Irish', 'Leprechaun')),
    holiday('easter', 'Easter', titleHas('Easter')),
    holiday('earth-day', 'Earth Day', titleHas('Earth Day', 'Planet Earth', 'Nature', 'Environment')),
    holiday('arbor-day', 'Arbor Day', titleHas('Arbor Day', 'Trees', 'Forest')),
    holiday('mothers-day', "Mother's Day", titleHas('Mother', 'Mama', 'Mommy')),
    holiday('memorial-day', 'Memorial Day', [...genreHas('War'), ...titleHas('Memorial')]),
    holiday('fathers-day', "Father's Day", titleHas('Father', 'Dad')),
    holiday('independence-day', 'Independence Day', titleHas('Independence Day', 'Fourth of July', '4th of July', 'July 4', 'Patriot')),
    holiday('labor-day', 'Labor Day', titleHas('Labor Day', 'Labour Day')),
    holiday('halloween', 'Halloween', [...genreHas('Horror'), ...titleHas('Halloween', 'Trick or Treat', 'Spooky')]),
    holiday('veterans-day', 'Veterans Day', [...genreHas('War'), ...titleHas('Veteran')]),
    holiday('thanksgiving', 'Thanksgiving', titleHas('Thanksgiving', 'Friendsgiving')),
    holiday('hanukkah', 'Hanukkah', titleHas('Hanukkah', 'Chanukah', 'Festival of Lights')),
    holiday('christmas', 'Christmas', titleHas('Christmas', 'Xmas', 'Santa', 'Grinch', 'Scrooge')),
    holiday('pride-month', 'Pride Month', titleHas('Pride Month', 'LGBT', 'Queer')),
    holiday('black-history-month', 'Black History Month', titleHas('Black History', 'Malcolm X', 'Selma', 'Hidden Figures', 'Harriet', 'Martin Luther King')),

    popular(
        'trending',
        'Trending',
        () => group('All', [rule('AudienceRating', 'GreaterThan', '7'), rule('DateAdded', 'InLastDays', '90')]),
        `Vora has no view counts, so this is well-rated titles added in the last 90 days. ${RATING_NOTE}`,
    ),
    popular('popular', 'Popular', () => group('All', [rule('AudienceRating', 'GreaterThan', '7')]), `Titles the audience rates above 7. ${RATING_NOTE}`),
    popular('top-rated', 'Top Rated', () => group('All', [rule('AudienceRating', 'GreaterThan', '8')]), `Titles the audience rates above 8. ${RATING_NOTE}`),
    popular(
        'new-releases',
        'New Releases',
        today => group('All', [rule('ReleaseYear', 'Between', String(today.getFullYear() - 1), String(today.getFullYear()))]),
        "Last year and this year, as of today. The years don't move on their own, so update them when the year changes.",
    ),
    popular('recently-added', 'Recently Added', () => group('All', [rule('DateAdded', 'InLastDays', '30')]), 'Added to a library in the last 30 days.'),
    popular(
        'award-winners',
        'Award Winners',
        () => group('All', [
            rule('AudienceRating', 'GreaterThan', '8'),
        ], [group('Any', genreHas('Drama', 'Biography', 'History', 'War'))]),
        `Vora has no award data, so this is the best-rated dramas, biographies, history and war titles. ${RATING_NOTE}`,
    ),
    popular(
        'hidden-gems',
        'Hidden Gems',
        () => group('All', [rule('AudienceRating', 'Between', '7', '8')]),
        `Vora can't tell how little-known a title is, so this is good but not top-rated titles, from 7 to 8. ${RATING_NOTE}`,
    ),
    popular(
        'blockbusters',
        'Blockbusters',
        () => group('All', [
            rule('AudienceRating', 'GreaterThan', '6'),
        ], [group('Any', genreHas('Action', 'Adventure', 'Science Fiction', 'Fantasy', 'Animation'))]),
        `Vora has no box-office numbers, so this is well-rated action, adventure, science fiction, fantasy and animation titles. ${RATING_NOTE}`,
    ),

    genre('action', 'Action', genreHas('Action')),
    genre('adventure', 'Adventure', genreHas('Adventure')),
    genre('animation', 'Animation', genreHas('Animation')),
    genre('anime', 'Anime', genreHas('Anime'), 'Matches the genre Anime. If your metadata tags anime as Animation, match that and narrow it by hand.'),
    genre('biography', 'Biography', genreHas('Biography', 'Biopic')),
    genre('comedy', 'Comedy', genreHas('Comedy')),
    genre('crime', 'Crime', genreHas('Crime')),
    genre('documentary', 'Documentary', genreHas('Documentary')),
    genre('drama', 'Drama', genreHas('Drama')),
    genre('family', 'Family', genreHas('Family')),
    genre('fantasy', 'Fantasy', genreHas('Fantasy')),
    genre('history', 'History', genreHas('History')),
    genre('horror', 'Horror', genreHas('Horror')),
    genre('kids', 'Kids', [...genreHas('Kids', 'Children'), ...ratingIs('TV-Y', 'TV-Y7', 'G')]),
    genre('martial-arts', 'Martial Arts', genreHas('Martial Arts', 'Kung Fu')),
    genre('music', 'Music', genreHas('Music')),
    genre('mystery', 'Mystery', genreHas('Mystery')),
    genre('reality', 'Reality', genreHas('Reality')),
    genre('romance', 'Romance', genreHas('Romance')),
    genre('science-fiction', 'Science Fiction', genreHas('Science Fiction', 'Sci-Fi')),
    genre('sports', 'Sports', genreHas('Sport')),
    genre(
        'superhero',
        'Superhero',
        [...genreHas('Superhero'), ...titleHas('Batman', 'Superman', 'Spider-Man', 'Avengers', 'Justice League', 'X-Men')],
        'Superhero is rarely a genre, so this also looks for a few well-known heroes in the title. Add the ones you have.',
    ),
    genre('thriller', 'Thriller', genreHas('Thriller')),
    genre('war', 'War', genreHas('War')),
    genre('western', 'Western', genreHas('Western')),

    ...[1920, 1930, 1940, 1950, 1960, 1970, 1980, 1990, 2000, 2010, 2020].map(decade),

    rated('g', 'Rated G', 'G'),
    rated('pg', 'Rated PG', 'PG'),
    rated('pg-13', 'Rated PG-13', 'PG-13'),
    rated('r', 'Rated R', 'R'),
    rated('nc-17', 'Rated NC-17', 'NC-17'),
    rated('tv-y', 'TV-Y', 'TV-Y'),
    rated('tv-y7', 'TV-Y7', 'TV-Y7'),
    rated('tv-g', 'TV-G', 'TV-G'),
    rated('tv-pg', 'TV-PG', 'TV-PG'),
    rated('tv-14', 'TV-14', 'TV-14'),
    rated('tv-ma', 'TV-MA', 'TV-MA'),
];

export const templatesInGroup = (name: SmartTemplateGroup): SmartCollectionTemplate[] =>
    SMART_COLLECTION_TEMPLATES.filter(template => template.group === name);

export const findSmartTemplate = (id: string): SmartCollectionTemplate | undefined =>
    SMART_COLLECTION_TEMPLATES.find(template => template.id === id);
