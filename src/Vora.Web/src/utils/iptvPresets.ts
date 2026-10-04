import type { IptvChannelKind } from '../api/Iptv/iptvAdminService';

export interface FreePlaylist {
    name: string;
    m3u: string;
    supportsWeb: boolean;
    maxConnections: number;
    defaultKind: IptvChannelKind;
}

export interface FreeEpgSource {
    name: string;
    xml: string;
}

export const FREE_PLAYLISTS: FreePlaylist[] = [
    { name: "US — IPTV Org (full country)", m3u: "https://iptv-org.github.io/iptv/countries/us.m3u", supportsWeb: true, maxConnections: 0, defaultKind: "Tv" },
    { name: "Greece — IPTV Org", m3u: "https://iptv-org.github.io/iptv/countries/gr.m3u", supportsWeb: true, maxConnections: 0, defaultKind: "Tv" },
    { name: "Greece — Free-Greek-IPTV", m3u: "https://raw.githubusercontent.com/free-greek-iptv/greek-iptv/master/android.m3u", supportsWeb: true, maxConnections: 0, defaultKind: "Tv" },
    { name: "Radio — Top 100 Worldwide (Radio Browser)", m3u: "https://de1.api.radio-browser.info/m3u/stations/topclick/100", supportsWeb: true, maxConnections: 0, defaultKind: "Radio" },
    { name: "Radio — Top 200 Most Voted (Radio Browser)", m3u: "https://de1.api.radio-browser.info/m3u/stations/topvote/200", supportsWeb: true, maxConnections: 0, defaultKind: "Radio" },
    { name: "Radio — US Top 100 (Radio Browser)", m3u: "https://de1.api.radio-browser.info/m3u/stations/bycountrycodeexact/US?limit=100&order=clickcount&reverse=true&hidebroken=true", supportsWeb: true, maxConnections: 0, defaultKind: "Radio" },
    { name: "Radio — News (Radio Browser)", m3u: "https://de1.api.radio-browser.info/m3u/stations/bytag/news?limit=100&order=clickcount&reverse=true&hidebroken=true", supportsWeb: true, maxConnections: 0, defaultKind: "Radio" },
    { name: "Radio — Classical (Radio Browser)", m3u: "https://de1.api.radio-browser.info/m3u/stations/bytag/classical?limit=100&order=clickcount&reverse=true&hidebroken=true", supportsWeb: true, maxConnections: 0, defaultKind: "Radio" },
    { name: "Radio — Jazz (Radio Browser)", m3u: "https://de1.api.radio-browser.info/m3u/stations/bytag/jazz?limit=100&order=clickcount&reverse=true&hidebroken=true", supportsWeb: true, maxConnections: 0, defaultKind: "Radio" }
];

export const FREE_EPG_SOURCES: FreeEpgSource[] = [
    { name: "US — IPTV-EPG.org Guide", xml: "https://iptv-epg.org/files/epg-us.xml" },
    { name: "Greece — GreekTVApp EPG", xml: "https://ext.greektv.app/epg/epg.xml.gz" },
    { name: "Greece — EPG Share GR1", xml: "https://epgshare01.online/epgshare01/epg_ripper_GR1.xml.gz" },
    { name: "Greece — IPTV-EPG.org Guide", xml: "https://iptv-epg.org/files/epg-gr.xml" }
];
