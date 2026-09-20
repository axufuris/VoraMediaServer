// Pull the directing credits out of a cast list. Library items and discovery
// items both carry crew inside the same cast array (the role contains
// "Director"), so both detail pages read the credit the same way.
export function directorsFrom(cast?: { name: string; role: string }[]): string[] {
    return (cast ?? []).filter(member => /director/i.test(member.role)).map(member => member.name);
}
