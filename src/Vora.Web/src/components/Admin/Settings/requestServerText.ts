export const MINIMUM_AVAILABILITY_HINT =
    'When Radarr starts looking for a requested movie. Announced: as soon as it is listed. In cinemas: once it is showing. Released: once it is out digitally or on disc, which avoids poor-quality cinema recordings.';

export const SEARCH_ON_ADD_HINT = (name: string) =>
    `On: ${name} starts looking for it straight away. Off: the request is added to ${name} but nothing is downloaded until you search for it there, so you can review requests first.`;

export const RELEASE_CALENDAR_HINT = (name: string, what: string) =>
    `Shows the ${what} ${name} is tracking on the Release Calendar, with their upcoming release dates.`;
