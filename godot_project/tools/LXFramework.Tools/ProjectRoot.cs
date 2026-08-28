namespace LXFramework.Tools;

internal static class ProjectRoot
{
    public static string Find(string startDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "project.godot")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find project.godot from '{startDirectory}' or any parent directory.");
    }
}
