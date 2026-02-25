namespace Cellular.Configuration;

public static class DotEnvLoader
{
    private static bool _loaded;

    public static void LoadFromSolutionRoot(string fileName = ".env")
    {
        if (_loaded)
        {
            return;
        }

        var path = FindFileInParentDirs(AppContext.BaseDirectory, fileName);
        if (path is null || !File.Exists(path))
        {
            _loaded = true;
            return;
        }

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
            {
                value = value[1..^1];
            }

            // Keep shell environment precedence if already set.
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        _loaded = true;
    }

    private static string? FindFileInParentDirs(string startDirectory, string fileName)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}
