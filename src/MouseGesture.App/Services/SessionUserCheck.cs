using System.Runtime.InteropServices;

namespace MouseGesture.App.Services;

/// <summary>
/// Detects "over-the-shoulder" elevation: a standard user approved the UAC prompt with a
/// different administrator account, so the app runs as that admin — its settings, logs and
/// autostart task all land in the wrong profile.
/// </summary>
public static partial class SessionUserCheck
{
    private const int WTS_CURRENT_SESSION = -1;
    private const int WTSUserName = 5;
    private const int WTSDomainName = 7;

    /// <summary>Returns the interactive session's user when it differs from the process user; otherwise null.</summary>
    public static string? GetMismatchedSessionUser()
    {
        try
        {
            var sessionUser = Query(WTSUserName);
            if (string.IsNullOrEmpty(sessionUser))
                return null;
            if (string.Equals(sessionUser, Environment.UserName, StringComparison.OrdinalIgnoreCase))
                return null;
            var domain = Query(WTSDomainName);
            return string.IsNullOrEmpty(domain) ? sessionUser : $@"{domain}\{sessionUser}";
        }
        catch
        {
            return null;
        }
    }

    private static string? Query(int infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, WTS_CURRENT_SESSION, infoClass, out var buffer, out _))
            return null;
        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, int wtsInfoClass, out IntPtr ppBuffer, out uint pBytesReturned);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(IntPtr pMemory);
}
