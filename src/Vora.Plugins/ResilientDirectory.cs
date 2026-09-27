namespace Vora.Plugins;

// Directory.GetFiles/EnumerateFiles with SearchOption.AllDirectories throws on
// the first unreadable subdirectory and yields NOTHING for the whole tree — it
// does not return a partial list. Media libraries live on network shares, where
// a single bad folder (a permission, a broken symlink, a transient SMB error) is
// routine, so that overload turns a populated library into an apparently empty
// one. Every walk of a media tree goes through here instead.
//
// Callers must report what was skipped rather than discarding it. A walk that
// quietly returns nothing is indistinguishable from a tree that really is empty,
// which is the failure this exists to prevent.
public static class ResilientDirectory
{
    public static IEnumerable<string> EnumerateFiles(string root, Action<string, Exception> onSkipped) =>
        EnumerateFiles(root, Directory.GetDirectories, Directory.GetFiles, onSkipped);

    internal static IEnumerable<string> EnumerateFiles(
        string root,
        Func<string, string[]> getDirectories,
        Func<string, string[]> getFiles,
        Action<string, Exception> onSkipped)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            string[] subdirectories;
            try
            {
                subdirectories = getDirectories(current);
            }
            catch (Exception ex)
            {
                onSkipped(current, ex);
                subdirectories = Array.Empty<string>();
            }

            foreach (var subdirectory in subdirectories)
            {
                pending.Push(subdirectory);
            }

            string[] files;
            try
            {
                files = getFiles(current);
            }
            catch (Exception ex)
            {
                onSkipped(current, ex);
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }
}
