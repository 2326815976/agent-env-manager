using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Cli;

internal static class AdoptionReportRenderer
{
    public static void Write(AdoptionPreview preview, TextWriter output)
    {
        output.WriteLine("纳管前预览");
        output.WriteLine($"环境身份指纹: {preview.Fingerprint.Value}");
        output.WriteLine($"名称: {preview.Asset.Name}");
        output.WriteLine($"版本: {preview.Asset.Version ?? "未知"}");
        output.WriteLine($"物理路径: {preview.Asset.Location}");
        output.WriteLine($"来源: {preview.Asset.Source.Description}");
        output.WriteLine($"资产哈希: {preview.AssetHash}");
        output.WriteLine($"稳定激活路径: {preview.StableActivationPath}");
        output.WriteLine($"影响范围: {preview.Impact}");
        output.WriteLine(
            preview.IsAlreadyManaged
                ? $"当前状态: 已纳管 ({preview.ExistingIdentity!.Value})"
                : "当前状态: 仅观测");
    }

    public static void Write(ManagedEnvironment managed, TextWriter output)
    {
        output.WriteLine($"纳管完成: {managed.Manifest.Name}");
        output.WriteLine($"环境身份: {managed.Identity.Value}");
        output.WriteLine($"操作 ID: {managed.Manifest.OperationId}");
        output.WriteLine($"指纹: {managed.Manifest.Fingerprint.Value}");
        output.WriteLine($"版本: {managed.Manifest.Version ?? "未知"}");
        output.WriteLine($"稳定激活路径: {managed.Manifest.StableActivationPath}");
        output.WriteLine($"资产哈希: {managed.Manifest.AssetHash}");
    }
}
