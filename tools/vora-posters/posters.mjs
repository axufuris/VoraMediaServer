import * as decades from './art/decades.mjs';
import * as genres from './art/genres.mjs';
import * as holidays from './art/holidays.mjs';
import * as popular from './art/popular.mjs';
import * as ratings from './art/ratings.mjs';

export const CATEGORIES = [
    { id: 'holidays', name: 'Holidays', eyebrow: 'Holiday' },
    { id: 'popular', name: 'Popular', eyebrow: 'Collection' },
    { id: 'genres', name: 'Genres', eyebrow: 'Genre' },
    { id: 'decades', name: 'Decades', eyebrow: 'Decade' },
    { id: 'ratings', name: 'Ratings', eyebrow: 'Rated' },
];

const holiday = (id, title, subtitle, aliases, look) => ({ id, category: 'holidays', title, subtitle, aliases, look });
const pick = (id, title, aliases, look) => ({ id, category: 'popular', title, aliases, look });
const genre = (id, title, aliases, look) => ({ id, category: 'genres', title, aliases, look });
const decade = (start, words, look, shortForm = true) => ({
    id: `${start}s`,
    category: 'decades',
    title: `${start}s`,
    subtitle: `${start} – ${start + 9}`,
    aliases: [`${start}s`, ...(shortForm ? [`${String(start).slice(2)}s`, `${String(start).slice(2)}'s`] : []), ...words],
    look,
});
const rating = (id, glyph, title, tv, look) => ({
    id,
    category: 'ratings',
    title,
    eyebrow: tv ? 'TV rating' : 'Rated',
    aliases: tv ? [glyph.toLowerCase(), `rated ${glyph.toLowerCase()}`] : [`rated ${glyph.toLowerCase()}`, `${glyph.toLowerCase()} rated`, ...(glyph.length > 2 ? [glyph.toLowerCase()] : [])],
    look,
});

export const POSTERS = [
    holiday('new-years', "New Year's", 'January 1', ["new year's", 'new years', 'new year', "new year's eve", 'nye'], holidays.newYears),
    holiday('lunar-new-year', 'Lunar New Year', 'Spring Festival', ['lunar new year', 'chinese new year', 'spring festival'], holidays.lunarNewYear),
    holiday('valentines-day', "Valentine's Day", 'February 14', ["valentine's", 'valentines', 'valentine'], holidays.valentinesDay),
    holiday('presidents-day', "Presidents' Day", 'Third Monday of February', ["presidents' day", 'presidents day', 'president', 'presidents'], holidays.presidentsDay),
    holiday('st-patricks-day', "St. Patrick's Day", 'March 17', ["st. patrick's", 'st patricks', 'saint patrick', 'st paddy', 'irish'], holidays.stPatricksDay),
    holiday('easter', 'Easter', 'Spring', ['easter'], holidays.easter),
    holiday('earth-day', 'Earth Day', 'April 22', ['earth day', 'planet earth', 'nature', 'environment'], holidays.earthDay),
    holiday('arbor-day', 'Arbor Day', 'Last Friday of April', ['arbor day', 'trees', 'forest'], holidays.arborDay),
    holiday('mothers-day', "Mother's Day", 'Second Sunday of May', ["mother's day", 'mothers day', 'mother', 'mom', 'moms'], holidays.mothersDay),
    holiday('memorial-day', 'Memorial Day', 'Last Monday of May', ['memorial day', 'memorial'], holidays.memorialDay),
    holiday('fathers-day', "Father's Day", 'Third Sunday of June', ["father's day", 'fathers day', 'father', 'dad', 'dads'], holidays.fathersDay),
    holiday('independence-day', 'Independence Day', 'July 4', ['independence day', 'fourth of july', '4th of july', 'july 4th', 'july 4', 'patriotic'], holidays.independenceDay),
    holiday('labor-day', 'Labor Day', 'First Monday of September', ['labor day', 'labour day'], holidays.laborDay),
    holiday('halloween', 'Halloween', 'October 31', ['halloween', 'spooky season', 'spooky', 'trick or treat'], holidays.halloween),
    holiday('veterans-day', 'Veterans Day', 'November 11', ['veterans day', "veterans' day", 'veterans', 'veteran'], holidays.veteransDay),
    holiday('thanksgiving', 'Thanksgiving', 'Fourth Thursday of November', ['thanksgiving', 'friendsgiving'], holidays.thanksgiving),
    holiday('hanukkah', 'Hanukkah', 'Festival of Lights', ['hanukkah', 'chanukah', 'festival of lights'], holidays.hanukkah),
    holiday('christmas', 'Christmas', 'December 25', ['christmas', 'xmas', 'holiday season', 'santa'], holidays.christmas),
    holiday('pride-month', 'Pride Month', 'June', ['pride month', 'pride', 'lgbtq', 'lgbt'], holidays.prideMonth),
    holiday('black-history-month', 'Black History Month', 'February', ['black history month', 'black history'], holidays.blackHistoryMonth),

    pick('trending', 'Trending', ['trending', 'trending now', 'hot right now'], popular.trending),
    pick('popular', 'Popular', ['popular', 'most popular', 'fan favorites', 'crowd pleasers'], popular.popular),
    pick('top-rated', 'Top Rated', ['top rated', 'top-rated', 'highest rated', 'best rated', 'imdb top'], popular.topRated),
    pick('new-releases', 'New Releases', ['new releases', 'new release', 'just released', 'new movies', 'new shows'], popular.newReleases),
    pick('recently-added', 'Recently Added', ['recently added', 'just added', 'new arrivals', 'latest additions'], popular.recentlyAdded),
    pick('award-winners', 'Award Winners', ['award winners', 'award winning', 'award-winning', 'awards', 'oscar', 'oscars', 'academy award', 'emmy', 'golden globe'], popular.awardWinners),
    pick('hidden-gems', 'Hidden Gems', ['hidden gems', 'hidden gem', 'underrated', 'overlooked'], popular.hiddenGems),
    pick('blockbusters', 'Blockbusters', ['blockbusters', 'blockbuster', 'box office', 'summer blockbusters'], popular.blockbusters),

    genre('action', 'Action', ['action', 'action & adventure', 'action and adventure'], genres.action),
    genre('adventure', 'Adventure', ['adventure', 'adventures'], genres.adventure),
    genre('animation', 'Animation', ['animation', 'animated', 'cartoon', 'cartoons'], genres.animation),
    genre('anime', 'Anime', ['anime'], genres.anime),
    genre('biography', 'Biography', ['biography', 'biographies', 'biopic', 'biopics', 'true story', 'true stories'], genres.biography),
    genre('comedy', 'Comedy', ['comedy', 'comedies', 'funny', 'sitcom', 'sitcoms', 'stand-up'], genres.comedy),
    genre('crime', 'Crime', ['crime', 'true crime', 'heist', 'gangster'], genres.crime),
    genre('documentary', 'Documentary', ['documentary', 'documentaries', 'docuseries', 'docs'], genres.documentary),
    genre('drama', 'Drama', ['drama', 'dramas'], genres.drama),
    genre('family', 'Family', ['family', 'family movie night', 'family night'], genres.family),
    genre('fantasy', 'Fantasy', ['fantasy'], genres.fantasy),
    genre('history', 'History', ['history', 'historical', 'period'], genres.history),
    genre('horror', 'Horror', ['horror', 'scary', 'slasher'], genres.horror),
    genre('kids', 'Kids', ['kids', 'children', "children's", 'kid'], genres.kids),
    genre('martial-arts', 'Martial Arts', ['martial arts', 'kung fu', 'karate', 'wuxia'], genres.martialArts),
    genre('music', 'Music', ['music', 'musical', 'musicals', 'concert', 'concerts'], genres.music),
    genre('mystery', 'Mystery', ['mystery', 'mysteries', 'whodunit', 'detective'], genres.mystery),
    genre('reality', 'Reality', ['reality', 'reality tv', 'reality shows'], genres.reality),
    genre('romance', 'Romance', ['romance', 'romantic', 'rom-com', 'rom-coms', 'romcom', 'love stories'], genres.romance),
    genre('science-fiction', 'Science Fiction', ['science fiction', 'sci-fi', 'scifi', 'sci-fi & fantasy'], genres.scienceFiction),
    genre('sports', 'Sports', ['sports', 'sport'], genres.sports),
    genre('superhero', 'Superhero', ['superhero', 'superheroes', 'super hero', 'comic book', 'comic books'], genres.superhero),
    genre('thriller', 'Thriller', ['thriller', 'thrillers', 'suspense'], genres.thriller),
    genre('war', 'War', ['war', 'war & politics', 'military'], genres.war),
    genre('western', 'Western', ['western', 'westerns', 'cowboy', 'cowboys'], genres.western),

    decade(1920, ['twenties', 'roaring twenties', 'roaring 20s'], decades.twenties, false),
    decade(1930, ['thirties'], decades.thirties),
    decade(1940, ['forties'], decades.forties),
    decade(1950, ['fifties'], decades.fifties),
    decade(1960, ['sixties'], decades.sixties),
    decade(1970, ['seventies'], decades.seventies),
    decade(1980, ['eighties'], decades.eighties),
    decade(1990, ['nineties'], decades.nineties),
    decade(2000, ['two thousands', 'noughties', 'aughts'], decades.twoThousands),
    decade(2010, ['twenty tens', 'twenty-tens'], decades.twentyTens),
    decade(2020, ['twenty twenties', 'twenty-twenties'], decades.twentyTwenties, false),

    rating('g', 'G', 'General Audiences', false, ratings.g),
    rating('pg', 'PG', 'Parental Guidance Suggested', false, ratings.pg),
    rating('pg-13', 'PG-13', 'Parents Strongly Cautioned', false, ratings.pg13),
    rating('r', 'R', 'Restricted', false, ratings.r),
    rating('nc-17', 'NC-17', 'Adults Only', false, ratings.nc17),
    rating('tv-y', 'TV-Y', 'All Children', true, ratings.tvY),
    rating('tv-y7', 'TV-Y7', 'Older Children', true, ratings.tvY7),
    rating('tv-g', 'TV-G', 'General Audience', true, ratings.tvG),
    rating('tv-pg', 'TV-PG', 'Parental Guidance Suggested', true, ratings.tvPg),
    rating('tv-14', 'TV-14', 'Parents Strongly Cautioned', true, ratings.tv14),
    rating('tv-ma', 'TV-MA', 'Mature Audience Only', true, ratings.tvMa),
];
