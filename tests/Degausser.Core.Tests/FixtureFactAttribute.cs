namespace Degausser.Core.Tests;

/// <summary>
/// A fact that needs private, non-redistributable fixture data. It is reported
/// as skipped, rather than silently passing, unless <paramref name="environmentVariable"/>
/// names an existing file or directory.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FixtureFactAttribute : FactAttribute
{
    public FixtureFactAttribute(string environmentVariable)
    {
        EnvironmentVariable = environmentVariable;
        string? path = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(path))
            Skip = $"Set {environmentVariable} to run this fixture test.";
        else if (!File.Exists(path) && !Directory.Exists(path))
            Skip = $"{environmentVariable} points to a missing path: {path}";
    }

    public string EnvironmentVariable { get; }

    public static string GetPath(string environmentVariable)
        => Environment.GetEnvironmentVariable(environmentVariable)
           ?? throw new InvalidOperationException($"{environmentVariable} is not set.");
}
