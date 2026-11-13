namespace Degausser;

/// <summary>
/// Lets only one file or folder picker be requested at a time. The system picker can be slow to appear
/// (on Linux it goes through the desktop portal) and doesn't block the window while it does, so repeated
/// clicks would otherwise queue several dialogs that all show up together. UI thread only.
/// </summary>
internal static class PickerGate
{
    private static bool pending;

    /// <summary>Runs <paramref name="picker"/>, or returns null at once if another picker is already being shown.</summary>
    public static async Task<T?> RunAsync<T>(Func<Task<T>> picker) where T : class?
    {
        if (pending) return null;
        pending = true;
        try
        {
            return await picker();
        }
        finally
        {
            pending = false;
        }
    }
}
