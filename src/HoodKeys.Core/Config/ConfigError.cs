namespace HoodKeys.Core.Config;

/// <summary>One problem in a config file: which file, which JSON field path, and what is wrong.</summary>
public sealed record ConfigError(string File, string Path, string Message)
{
    public override string ToString() =>
        Path.Length == 0 ? $"{File}: {Message}" : $"{File}: {Path}: {Message}";
}

/// <summary>Outcome of loading a config: either a config or at least one error.</summary>
public sealed record ConfigLoadResult(HoodKeysConfig? Config, IReadOnlyList<ConfigError> Errors)
{
    public bool IsValid => Config is not null;
}
