using System.Diagnostics;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace MouseGesture.App.Services;

public sealed record AutoStartResult(bool Success, string? Message);

/// <summary>
/// Manages "run at logon". Because the app runs elevated (requireAdministrator), a plain
/// HKCU\...\Run entry is silently skipped by Windows at logon, so we register a Scheduled
/// Task with "run with highest privileges" instead, which starts the app elevated and
/// silently. Any legacy Run-key entry is removed.
///
/// The task is registered from XML rather than schtasks flags because the flag-based
/// defaults are wrong for a resident app: "stop if running longer than 3 days", "don't
/// start / stop on battery", and below-normal priority.
///
/// All methods block (they may spawn schtasks.exe) — call them off the UI thread.
/// </summary>
public static class AutoStartManager
{
    private const string TaskName = "MouseGesture";
    private const string LegacyRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyValueName = "MouseGesture";
    private static readonly XNamespace TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static string SchTasksPath => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    // The task definition as stored by Task Scheduler (UTF-16 XML). Reading it directly is
    // fast and, unlike parsing schtasks output, safe for non-ASCII paths.
    private static string TaskFilePath => Path.Combine(Environment.SystemDirectory, "Tasks", TaskName);

    /// <summary>True when the task exists and launches this executable.</summary>
    public static bool IsEnabled()
    {
        var task = ReadTask();
        var path = Environment.ProcessPath;
        return task is not null && path is not null &&
               string.Equals(task.Value.Command, path, StringComparison.OrdinalIgnoreCase);
    }

    public static AutoStartResult Set(bool enabled)
    {
        try
        {
            return enabled ? Enable() : Disable();
        }
        catch (Exception ex)
        {
            Logger.Error("AutoStart write failed", ex);
            return new AutoStartResult(false, $"자동 실행 설정 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-registers a task that points at this executable but was created by an older
    /// version with the problematic schtasks defaults.
    /// </summary>
    public static void UpgradeIfNeeded()
    {
        try
        {
            var task = ReadTask();
            if (task is null || !task.Value.Outdated || !IsEnabled())
                return;
            Logger.Info("AutoStart task has outdated settings; re-registering.");
            Enable();
        }
        catch (Exception ex)
        {
            Logger.Warn($"AutoStart upgrade failed: {ex.Message}");
        }
    }

    private static AutoStartResult Enable()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
            return new AutoStartResult(false, "실행 파일 경로를 알 수 없어 자동 실행을 켤 수 없습니다.");

        // An elevated logon task pointing at a user-writable exe would let any unelevated
        // program replace it and gain admin rights at the next logon without a UAC prompt.
        if (!IsProtectedLocation(path, out var reason))
        {
            Logger.Warn($"AutoStart refused: {reason}");
            return new AutoStartResult(false,
                "현재 위치는 일반 권한으로도 파일을 바꿀 수 있어서 관리자 권한 자동 실행을 등록하지 않았습니다. " +
                "인스톨러로 설치(Program Files)한 뒤 켜 주세요.");
        }

        var user = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        var xmlPath = Path.Combine(Path.GetTempPath(), $"MouseGesture-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(path, user), Encoding.Unicode);
            var (code, output) = RunSchTasks("/Create", "/TN", TaskName, "/XML", xmlPath, "/F");
            if (code != 0)
            {
                Logger.Warn($"AutoStart enable failed: schtasks exit {code}: {output}");
                return new AutoStartResult(false, $"작업 스케줄러 등록 실패 (코드 {code})");
            }
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }

        Logger.Info($"AutoStart enabled (scheduled task) → {path}");
        RemoveLegacyRunEntry();
        return new AutoStartResult(true, null);
    }

    private static AutoStartResult Disable()
    {
        RemoveLegacyRunEntry();
        if (ReadTask() is null)
            return new AutoStartResult(true, null);

        var (code, output) = RunSchTasks("/Delete", "/TN", TaskName, "/F");
        if (code != 0)
        {
            Logger.Warn($"AutoStart disable: schtasks exit {code}: {output}");
            return new AutoStartResult(false, $"작업 스케줄러 삭제 실패 (코드 {code})");
        }
        Logger.Info("AutoStart disabled (scheduled task removed)");
        return new AutoStartResult(true, null);
    }

    private static string BuildTaskXml(string exePath, string userSid)
    {
        var cmd = SecurityElement.Escape(exePath);
        var sid = SecurityElement.Escape(userSid);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>MouseGesture 로그인 시 자동 실행</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{sid}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{sid}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{cmd}</Command>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static (string Command, bool Outdated)? ReadTask()
    {
        try
        {
            if (!File.Exists(TaskFilePath))
                return null;
            var doc = XDocument.Load(TaskFilePath);
            var command = doc.Descendants(TaskNs + "Command").FirstOrDefault()?.Value.Trim().Trim('"') ?? "";
            var settings = doc.Descendants(TaskNs + "Settings").FirstOrDefault();
            string? Get(string name) => settings?.Element(TaskNs + name)?.Value.Trim();
            var outdated =
                Get("ExecutionTimeLimit") != "PT0S" ||
                Get("DisallowStartIfOnBatteries") != "false" ||
                Get("StopIfGoingOnBatteries") != "false";
            return (command, outdated);
        }
        catch (Exception ex)
        {
            Logger.Warn($"AutoStart task read failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// True when neither the exe nor its folder (nor the folder's parent) can be modified
    /// by the unelevated user — e.g. under Program Files, but not Downloads or Desktop.
    /// </summary>
    internal static bool IsProtectedLocation(string exePath, out string reason)
    {
        var user = WindowsIdentity.GetCurrent().User;
        var risky = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.WorldSid, null),
            new(WellKnownSidType.BuiltinUsersSid, null),
            new(WellKnownSidType.AuthenticatedUserSid, null),
            new(WellKnownSidType.InteractiveSid, null),
        };
        if (user is not null)
            risky.Add(user);

        const FileSystemRights fileWrite =
            FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete |
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        const FileSystemRights dirWrite =
            FileSystemRights.CreateFiles | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Delete |
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        const int genericWriteOrAll = 0x40000000 | 0x10000000;

        bool Writable(FileSystemSecurity security, FileSystemRights mask)
        {
            if (security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && risky.Contains(owner))
                return true;
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow ||
                    rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly) ||
                    rule.IdentityReference is not SecurityIdentifier sid || !risky.Contains(sid))
                    continue;
                if ((rule.FileSystemRights & mask) != 0 || ((int)rule.FileSystemRights & genericWriteOrAll) != 0)
                    return true;
            }
            return false;
        }

        try
        {
            if (Writable(new FileInfo(exePath).GetAccessControl(), fileWrite))
            {
                reason = $"exe is user-writable: {exePath}";
                return false;
            }
            var dir = new FileInfo(exePath).Directory;
            for (var depth = 0; dir is not null && depth < 2; depth++, dir = dir.Parent)
            {
                if (Writable(dir.GetAccessControl(), dirWrite))
                {
                    reason = $"folder is user-writable: {dir.FullName}";
                    return false;
                }
            }
            reason = "";
            return true;
        }
        catch (Exception ex)
        {
            reason = $"ACL check failed: {ex.Message}";
            return false;
        }
    }

    private static void RemoveLegacyRunEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKey, writable: true);
            if (key?.GetValue(LegacyValueName) is not null)
            {
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
                Logger.Info("Removed legacy Run-key autostart entry.");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Legacy Run-key cleanup failed: {ex.Message}");
        }
    }

    private static (int ExitCode, string Output) RunSchTasks(params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = SchTasksPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process is null)
            return (-1, "");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, (stdout.Result + stderr.Result).Trim());
    }
}
