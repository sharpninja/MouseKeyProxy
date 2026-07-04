using MouseKeyProxy.Repl;

namespace MouseKeyProxy.Service.Tests;

internal sealed class RecordingProcessRunner : IProcessRunner
{
    private readonly Dictionary<(string FileName, string Arguments), ProcessRunResult> _responses = new();
    public List<(string FileName, string Arguments)> Calls { get; } = new();

    public void SetResponse(string fileName, string arguments, ProcessRunResult result) =>
        _responses[(fileName, arguments)] = result;

    public void SetDefaultSuccess() =>
        SetResponse("*", "*", new ProcessRunResult(0, "OK", string.Empty));

    public ProcessRunResult Run(string fileName, string arguments, bool redirectOutput = true)
    {
        Calls.Add((fileName, arguments));
        if (_responses.TryGetValue((fileName, arguments), out var exact))
        {
            return exact;
        }

        foreach (var kv in _responses)
        {
            if (kv.Key.FileName == "*" || (fileName.Contains(kv.Key.FileName, StringComparison.OrdinalIgnoreCase) &&
                (kv.Key.Arguments == "*" || arguments.Contains(kv.Key.Arguments, StringComparison.Ordinal))))
            {
                return kv.Value;
            }
        }

        return new ProcessRunResult(0, "OK", string.Empty);
    }
}