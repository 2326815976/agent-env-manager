using System.Runtime.InteropServices;
using System.Text;

namespace AgentEnvManager.Core.Migrations;

public sealed class WindowsMigrationOccupancyProbe
    : IMigrationOccupancyProbe
{
    public Task<IReadOnlyList<MigrationBlocker>> FindBlockersAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = Path.GetFullPath(sourcePath);
        var blockers = new List<MigrationBlocker>();
        var files = Directory.EnumerateFiles(
                source,
                "*",
                SearchOption.AllDirectories)
            .ToArray();
        FindFileLockBlockers(
            files,
            blockers,
            cancellationToken);
        FindRestartManagerBlockers(
            files,
            blockers,
            cancellationToken);
        return Task.FromResult<IReadOnlyList<MigrationBlocker>>(blockers);
    }

    private static void FindFileLockBlockers(
        IReadOnlyList<string> files,
        ICollection<MigrationBlocker> blockers,
        CancellationToken cancellationToken)
    {
        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = File.Open(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None);
            }
            catch (IOException)
            {
                blockers.Add(new MigrationBlocker(
                    "file-lock",
                    "迁移前检测到文件锁。",
                    filePath));
            }
            catch (UnauthorizedAccessException)
            {
                blockers.Add(new MigrationBlocker(
                    "access-denied",
                    "迁移前无法检查文件访问状态。",
                    filePath));
            }
        }
    }

    private static void FindRestartManagerBlockers(
        IReadOnlyList<string> files,
        ICollection<MigrationBlocker> blockers,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return;
        }

        var sessionKey = new StringBuilder(32);
        if (RmStartSession(
                out var sessionHandle,
                flags: 0,
                sessionKey) != 0)
        {
            blockers.Add(new MigrationBlocker(
                "occupancy-probe",
                "无法启动 Windows 占用检测，迁移已阻止。"));
            return;
        }

        try
        {
            if (RmRegisterResources(
                    sessionHandle,
                    checked((uint)files.Count),
                    files.ToArray(),
                    applications: 0,
                    null,
                    services: 0,
                    null) != 0)
            {
                blockers.Add(new MigrationBlocker(
                    "occupancy-probe",
                    "无法注册待迁移文件，迁移已阻止。"));
                return;
            }

            uint needed = 0;
            uint count = 16;
            var processes = new RmProcessInfo[checked((int)count)];
            var result = RmGetList(
                sessionHandle,
                out needed,
                ref count,
                processes,
                out _);
            if (result == ErrorMoreData)
            {
                count = needed;
                processes = new RmProcessInfo[checked((int)count)];
                result = RmGetList(
                    sessionHandle,
                    out needed,
                    ref count,
                    processes,
                    out _);
            }

            if (result != 0)
            {
                blockers.Add(new MigrationBlocker(
                    "occupancy-probe",
                    "无法读取 Windows 占用检测结果，迁移已阻止。"));
                return;
            }

            for (var index = 0; index < Math.Min(count, processes.Length); index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var process = processes[index];
                if (process.Process.ProcessId == Environment.ProcessId)
                {
                    continue;
                }

                blockers.Add(new MigrationBlocker(
                    "process-handle",
                    string.IsNullOrWhiteSpace(process.ApplicationName)
                        ? $"进程 {process.Process.ProcessId} 持有待迁移文件句柄。"
                        : $"进程 {process.ApplicationName} 持有待迁移文件句柄。"));
            }
        }
        finally
        {
            _ = RmEndSession(sessionHandle);
        }
    }

    private const int ErrorMoreData = 234;

    private enum RmAppType
    {
        Unknown = 0,
        MainWindow = 1,
        OtherWindow = 2,
        Service = 3,
        Explorer = 4,
        Console = 5,
        Critical = 1000
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RmFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public RmFileTime ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ApplicationName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string ServiceShortName;

        public RmAppType ApplicationType;
        public uint ApplicationStatus;
        public uint TsSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }

    [DllImport(
        "rstrtmgr.dll",
        CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(
        out uint sessionHandle,
        int flags,
        StringBuilder sessionKey);

    [DllImport(
        "rstrtmgr.dll",
        CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint fileCount,
        string[] fileNames,
        uint applications,
        RmUniqueProcess[]? applicationList,
        uint services,
        string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint sessionHandle,
        out uint needed,
        ref uint count,
        [In, Out] RmProcessInfo[] processInfo,
        out uint rebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);
}
