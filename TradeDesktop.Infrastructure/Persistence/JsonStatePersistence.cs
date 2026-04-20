using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.Infrastructure.Persistence;

public sealed class JsonStatePersistence : IStatePersistence
{
    private static readonly TimeSpan[] RetryBackoffs =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(1000)
    ];

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _statePath;
    private readonly string _bakPath;
    private readonly string _tmpPath;

    public JsonStatePersistence()
        : this(AppContext.BaseDirectory)
    {
    }

    public JsonStatePersistence(string baseDirectory)
    {
        _statePath = Path.Combine(baseDirectory, "state.json");
        _bakPath = Path.Combine(baseDirectory, "state.json.bak");
        _tmpPath = Path.Combine(baseDirectory, "state.json.tmp");
    }

    public async Task SaveAsync(StateSnapshot snapshot, CancellationToken ct = default)
    {
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, _jsonOptions);
            await RetryIoAsync(async token =>
            {
                var dir = Path.GetDirectoryName(_statePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await File.WriteAllBytesAsync(_tmpPath, payload, token);

                if (File.Exists(_statePath))
                {
                    File.Move(_statePath, _bakPath, overwrite: true);
                }

                File.Move(_tmpPath, _statePath, overwrite: true);
            }, ct);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StatePersistence] Save failed: {ex}");
            TryDeleteSilently(_tmpPath);
        }
    }

    public StateSnapshot? Load()
    {
        foreach (var candidate in new[] { _statePath, _bakPath, _tmpPath })
        {
            var loaded = TryLoadFrom(candidate);
            if (loaded is not null)
            {
                return loaded;
            }
        }

        return null;
    }

    public void Clear()
    {
        TryDeleteSilently(_statePath);
        TryDeleteSilently(_bakPath);
        TryDeleteSilently(_tmpPath);
    }

    private StateSnapshot? TryLoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            return JsonSerializer.Deserialize<StateSnapshot>(bytes, _jsonOptions);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StatePersistence] Load failed from {path}: {ex.Message}");
            return null;
        }
    }

    private static async Task RetryIoAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        IOException? lastIo = null;

        for (var i = 0; i < RetryBackoffs.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await action(ct);
                return;
            }
            catch (IOException ex)
            {
                lastIo = ex;
                if (i == RetryBackoffs.Length - 1)
                {
                    throw;
                }

                await Task.Delay(RetryBackoffs[i], ct);
            }
        }

        if (lastIo is not null)
        {
            throw lastIo;
        }
    }

    private static void TryDeleteSilently(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}