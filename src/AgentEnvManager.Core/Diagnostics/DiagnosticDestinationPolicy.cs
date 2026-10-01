namespace AgentEnvManager.Core.Diagnostics;

public static class DiagnosticDestinationPolicy
{
    public static void EnsureLocal(string destinationPath)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        if (fullPath.StartsWith(
            @"\\",
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "诊断包只能导出到本地路径。");
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "诊断包导出路径无效。");
        }

        try
        {
            if (new DriveInfo(root).DriveType == DriveType.Network)
            {
                throw new InvalidOperationException(
                    "诊断包只能导出到本地路径。");
            }
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "诊断包导出路径无效。",
                exception);
        }
    }
}
