using System.Text.Json;
using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Core.Storage;

public sealed class FileOperationJournal(string operationDirectory)
    : IOperationJournal
{
    public async Task SaveAsync(
        OperationRecord operation,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(operationDirectory);
        var path = Path.Combine(
            operationDirectory,
            $"{operation.Id}.json");
        await AtomicJsonFile.WriteAsync(
            path,
            operation,
            cancellationToken);
    }

    public async Task<OperationRecord?> GetAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(
            operationDirectory,
            $"{operationId}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<OperationRecord>(
            json,
            ManagerJson.Options);
    }

}
