using System.Text.Json;
using System.Text.Json.Serialization;
using EssSimulator.Core;
using EssSimulator.EmsStrategy.Application;
using EssSimulator.EmsStrategy.Domain;

namespace EssSimulator.EmsStrategy.Adapter;

/// <summary>策略运行时：配置热更新、占用闸门、引擎单例、成功后落盘。</summary>
public sealed class EmsStrategyRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();
    private readonly string? _persistPath;

    public IEmsStrategyEngine Engine { get; }

    /// <summary>与 <see cref="LoadFileOrDefault"/> 读取的同一路径。</summary>
    public static string DefaultConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "configs", "ems-strategy.json");

    public EmsStrategyRuntime() : this(new EmsStrategyEngine(), LoadFileOrDefault(), DefaultConfigPath) { }

    public EmsStrategyRuntime(IEmsStrategyEngine engine, EmsStrategyConfig? config = null, string? persistPath = null)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _persistPath = persistPath;
        var cfg = (config ?? EmsStrategyConfig.CreateDefault()).Clone();
        EmsStrategyConfig.NormalizeModes(cfg);
        Engine.Initialize(cfg);
        lock (_gate)
        {
            Config = cfg;
        }
        SyncGate(cfg.Enabled);
    }

    public EmsStrategyConfig Config { get; private set; }

    public bool TrySetEnabled(bool enabled, out string error)
    {
        lock (_gate)
        {
            var next = Config.Clone();
            next.Enabled = enabled;
            return TryReplaceLocked(next, out error);
        }
    }

    public bool TryReplace(EmsStrategyConfig config, out string error)
    {
        lock (_gate)
            return TryReplaceLocked(config.Clone(), out error);
    }

    public EmsStrategySnapshot Snapshot()
    {
        lock (_gate)
            return Engine.GetSnapshot();
    }

    private bool TryReplaceLocked(EmsStrategyConfig next, out string error)
    {
        if (next.Enabled)
        {
            if (!ExternalControlGate.TryOccupy(ExternalControlOwner.EmsStrategy, out error))
                return false;
        }
        else if (ExternalControlGate.Owner == ExternalControlOwner.EmsStrategy)
        {
            ExternalControlGate.Release(ExternalControlOwner.EmsStrategy);
        }

        EmsStrategyConfig.NormalizeModes(next);
        Engine.UpdateConfig(next);
        Config = next;
        PersistLocked();
        error = "";
        return true;
    }

    private void PersistLocked()
    {
        if (string.IsNullOrWhiteSpace(_persistPath))
            return;
        try
        {
            string? dir = Path.GetDirectoryName(_persistPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_persistPath, JsonSerializer.Serialize(Config, JsonOptions));
        }
        catch
        {
            // 引擎已热更新；落盘失败不回滚占用。
        }
    }

    private static void SyncGate(bool enabled)
    {
        if (enabled)
            ExternalControlGate.TryOccupy(ExternalControlOwner.EmsStrategy, out _);
        else if (ExternalControlGate.Owner == ExternalControlOwner.EmsStrategy)
            ExternalControlGate.Release(ExternalControlOwner.EmsStrategy);
    }

    public static EmsStrategyConfig LoadFileOrDefault()
    {
        string path = DefaultConfigPath;
        if (!File.Exists(path))
            return EmsStrategyConfig.CreateDefault();
        try
        {
            string json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<EmsStrategyConfig>(json, JsonOptions)
                      ?? EmsStrategyConfig.CreateDefault();
            EmsStrategyConfig.NormalizeModes(cfg);
            return cfg;
        }
        catch
        {
            return EmsStrategyConfig.CreateDefault();
        }
    }
}
