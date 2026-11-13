using System.Reflection;

namespace Degausser;

public static class AppVersion
{
    public static string Full
    {
        get
        {
            Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;
            string? informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
                return informational.Split('+')[0];

            return assembly.GetName().Version?.ToString(3) ?? "3.0.0";
        }
    }

    public static string MajorMinor
    {
        get
        {
            if (!Version.TryParse(Full, out Version? version) || version is null)
                return Full;
            return $"{version.Major}.{version.Minor}";
        }
    }
}
