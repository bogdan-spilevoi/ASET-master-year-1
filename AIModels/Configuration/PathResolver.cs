namespace SmartLost.AI.Configuration;

internal static class PathResolver
{
    public static string Resolve(string? value, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A required path is missing. Configure the Paths values in appsettings.json.");
        }

        if (value == "~")
        {
            value = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else if (value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("~\\", StringComparison.Ordinal))
        {
            value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value[2..]);
        }

        return Path.GetFullPath(value, baseDirectory ?? AppContext.BaseDirectory);
    }
}
