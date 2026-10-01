using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Runtimes;

namespace AgentEnvManager.Cli;

internal static class RuntimeReportRenderer
{
    public static void WriteProviders(
        IReadOnlyList<RuntimeProviderDescriptor> providers,
        TextWriter output)
    {
        output.WriteLine("运行时提供者");
        foreach (var provider in providers)
        {
            output.WriteLine();
            output.WriteLine(
                $"- {provider.Name} ({provider.Id}) " +
                $"[{FormatMode(provider.Mode)}]");
            output.WriteLine($"  来源: {provider.OfficialSource}");
            output.WriteLine($"  许可证: {provider.License}");
            foreach (var artifact in provider.Artifacts)
            {
                output.WriteLine(
                    $"  - {artifact.Version} " +
                    $"[{artifact.InstallStrategy}] " +
                    $"{artifact.DownloadUrl}");
            }
        }
    }

    public static void WritePreview(
        RuntimeInstallPreview preview,
        TextWriter output)
    {
        output.WriteLine("运行时安装预览");
        output.WriteLine(
            $"{preview.Provider.Name} {preview.Artifact.Version}");
        output.WriteLine($"安装目录: {preview.InstallRoot}");
        output.WriteLine($"可执行文件: {preview.ExecutablePath}");
        output.WriteLine($"稳定激活路径: {preview.StableActivationPath}");
        output.WriteLine($"受管入口: {preview.ManagedEntryPath}");
        output.WriteLine(
            $"镜像: {(string.IsNullOrWhiteSpace(preview.MirrorUrl) ? "未指定" : preview.MirrorUrl)}");
        output.WriteLine(
            $"缓存制品: {(preview.IsCachedArtifactAvailable ? "可用" : "不可用")}");
        output.WriteLine(
            $"当前状态: {(preview.IsAlreadyInstalled ? "已安装" : "未安装")}");
        output.WriteLine($"影响范围: {preview.Impact}");
        output.WriteLine(
            $"操作 ID: {preview.OperationId ?? "无需操作"}");
        output.WriteLine(
            $"恢复点: {preview.RecoveryPointId ?? "无需恢复"}");
    }

    public static void WriteInstalled(
        InstalledRuntime installed,
        RuntimeInstallPreview preview,
        TextWriter output)
    {
        output.WriteLine(
            $"安装完成: {installed.Provider.Name} " +
            $"{installed.Artifact.Version}");
        output.WriteLine($"可执行文件: {installed.ExecutablePath}");
        output.WriteLine($"受管入口: {installed.ManagedEntryPath}");
        output.WriteLine(
            $"操作 ID: {preview.OperationId ?? "无需操作"}");
        output.WriteLine(
            $"恢复点: {preview.RecoveryPointId ?? "无需恢复"}");
    }

    public static void WriteImportResult(
        RuntimeArtifactCacheEntry entry,
        OperationRecord? operation,
        string providerId,
        string version,
        string sourcePath,
        TextWriter output)
    {
        output.WriteLine(
            $"离线制品已导入: {providerId} {version}");
        output.WriteLine($"源文件: {sourcePath}");
        output.WriteLine($"缓存路径: {entry.Path}");
        output.WriteLine($"SHA-256: {entry.Sha256}");
        output.WriteLine($"来源: {entry.Source}");
        output.WriteLine($"校验: {entry.VerificationResult}");
        output.WriteLine(
            $"影响范围: 校验 {sourcePath} 并写入制品缓存；" +
            "后续安装将复用该缓存。");
        output.WriteLine(
            $"操作 ID: {entry.OperationId ?? "未记录"}");
        output.WriteLine(
            $"恢复点: {operation?.RecoveryPointId ?? "未记录"}");
    }

    private static string FormatMode(RuntimeProviderMode mode)
    {
        return mode switch
        {
            RuntimeProviderMode.Installable => "可安装",
            RuntimeProviderMode.ObservedOnly => "仅观测",
            _ => mode.ToString()
        };
    }
}
