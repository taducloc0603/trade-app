using System.Text.Json;
using TradeDesktop.Application.Abstractions;
using TradeDesktop.Application.Models;

namespace TradeDesktop.Infrastructure.Persistence;

public sealed class SessionPersistenceService : ISessionPersistenceService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private readonly IMachineIdentityService _machineIdentityService;
    private readonly IRuntimeConfigProvider _runtimeConfigProvider;
    private readonly PersistenceWriteQueue _writeQueue = new();
    private readonly CancellationTokenSource _queueCts = new();
    private readonly Task _queueWorker;
    private readonly object _sync = new();

    private PersistedSession? _cache;
    private FileStream? _instanceLockStream;

    private readonly string _stateDirectory;
    private readonly string _sessionFilePath;
    private readonly string _sessionBackupPath;
    private readonly string _sessionTmpPath;
    private readonly string _sessionLockPath;

    public SessionPersistenceService(
        IMachineIdentityService machineIdentityService,
        IRuntimeConfigProvider runtimeConfigProvider)
    {
        _machineIdentityService = machineIdentityService;
        _runtimeConfigProvider = runtimeConfigProvider;

        var hostName = (_machineIdentityService.GetHostName() ?? string.Empty).Trim().ToLowerInvariant();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _stateDirectory = Path.Combine(localAppData, "TradeDesktop", "state", hostName);
        _sessionFilePath = Path.Combine(_stateDirectory, "session.json");
        _sessionBackupPath = Path.Combine(_stateDirectory, "session.json.bak");
        _sessionTmpPath = Path.Combine(_stateDirectory, "session.tmp");
        _sessionLockPath = Path.Combine(_stateDirectory, "session.lock");

        Directory.CreateDirectory(_stateDirectory);
        if (File.Exists(_sessionTmpPath))
        {
            File.Delete(_sessionTmpPath);
        }

        _queueWorker = Task.Run(() => _writeQueue.RunAsync(_queueCts.Token));
    }

    public async Task<PersistedSession?> LoadAsync(CancellationToken ct)
    {
        PersistedSession? loaded = null;

        if (File.Exists(_sessionFilePath))
        {
            loaded = await TryLoadFromFileAsync(_sessionFilePath, ct);
        }

        if (loaded is null && File.Exists(_sessionBackupPath))
        {
            loaded = await TryLoadFromFileAsync(_sessionBackupPath, ct);
        }

        if (loaded is null)
        {
            return null;
        }

        lock (_sync)
        {
            _cache = loaded;
        }

        return CloneSession(loaded);
    }

    public void EnqueueUpsertPair(PersistedPair pair)
    {
        ArgumentNullException.ThrowIfNull(pair);
        _writeQueue.Enqueue(async ct =>
        {
            lock (_sync)
            {
                var session = EnsureCache();
                var index = session.Pairs.FindIndex(x => string.Equals(x.PairId, pair.PairId, StringComparison.Ordinal));
                if (index >= 0)
                {
                    session.Pairs[index] = ClonePair(pair);
                }
                else
                {
                    session.Pairs.Add(ClonePair(pair));
                }
            }

            await PersistCacheAsync(ct);
        });
    }

    public void EnqueueRemovePair(string pairId)
    {
        if (string.IsNullOrWhiteSpace(pairId))
        {
            return;
        }

        _writeQueue.Enqueue(async ct =>
        {
            lock (_sync)
            {
                var session = EnsureCache();
                session.Pairs.RemoveAll(x => string.Equals(x.PairId, pairId, StringComparison.Ordinal));
            }

            await PersistCacheAsync(ct);
        });
    }

    public void EnqueueUpsertWaitWindow(PersistedWaitWindow waitWindow)
    {
        ArgumentNullException.ThrowIfNull(waitWindow);
        _writeQueue.Enqueue(async ct =>
        {
            lock (_sync)
            {
                var session = EnsureCache();
                session.WaitWindow = CloneWaitWindow(waitWindow);
            }

            await PersistCacheAsync(ct);
        });
    }

    public void EnqueueRemoveWaitWindow()
    {
        _writeQueue.Enqueue(async ct =>
        {
            lock (_sync)
            {
                var session = EnsureCache();
                session.WaitWindow = null;
            }

            await PersistCacheAsync(ct);
        });
    }

    public Task FlushAsync(CancellationToken ct) => _writeQueue.FlushAsync(ct);

    public Task AcquireInstanceLockAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (_instanceLockStream is not null)
        {
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(_stateDirectory);
        _instanceLockStream = new FileStream(
            _sessionLockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        return Task.CompletedTask;
    }

    public ValueTask ReleaseInstanceLockAsync()
    {
        _instanceLockStream?.Dispose();
        _instanceLockStream = null;
        return ValueTask.CompletedTask;
    }

    private async Task PersistCacheAsync(CancellationToken ct)
    {
        PersistedSession snapshot;
        lock (_sync)
        {
            snapshot = CloneSession(EnsureCache());
            snapshot.SavedAtUtc = DateTime.UtcNow;
        }

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await File.WriteAllTextAsync(_sessionTmpPath, json, ct);

        using (var fs = new FileStream(_sessionTmpPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            fs.Flush(true);
        }

        if (File.Exists(_sessionFilePath))
        {
            File.Replace(_sessionTmpPath, _sessionFilePath, _sessionBackupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(_sessionTmpPath, _sessionFilePath, overwrite: true);
        }
    }

    private async Task<PersistedSession?> TryLoadFromFileAsync(string path, CancellationToken ct)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var session = JsonSerializer.Deserialize<PersistedSession>(json, JsonOptions);
            if (session is null)
            {
                return null;
            }

            session.Pairs ??= [];
            return session;
        }
        catch
        {
            return null;
        }
    }

    private PersistedSession EnsureCache()
    {
        _cache ??= new PersistedSession
        {
            HostName = (_machineIdentityService.GetHostName() ?? string.Empty).Trim().ToLowerInvariant(),
            MapNameA = (_runtimeConfigProvider.CurrentMapName1 ?? string.Empty).Trim(),
            MapNameB = (_runtimeConfigProvider.CurrentMapName2 ?? string.Empty).Trim(),
            SavedAtUtc = DateTime.UtcNow,
            Pairs = []
        };

        _cache.MapNameA = (_runtimeConfigProvider.CurrentMapName1 ?? string.Empty).Trim();
        _cache.MapNameB = (_runtimeConfigProvider.CurrentMapName2 ?? string.Empty).Trim();
        _cache.HostName = (_machineIdentityService.GetHostName() ?? string.Empty).Trim().ToLowerInvariant();
        return _cache;
    }

    public async ValueTask DisposeAsync()
    {
        _queueCts.Cancel();
        try
        {
            await _queueWorker;
        }
        catch
        {
            // no-op
        }

        _queueCts.Dispose();
        await ReleaseInstanceLockAsync();
    }

    private static PersistedSession CloneSession(PersistedSession source)
    {
        return new PersistedSession
        {
            SchemaVersion = source.SchemaVersion,
            HostName = source.HostName,
            SavedAtUtc = source.SavedAtUtc,
            MapNameA = source.MapNameA,
            MapNameB = source.MapNameB,
            WaitWindow = source.WaitWindow is null ? null : CloneWaitWindow(source.WaitWindow),
            Pairs = source.Pairs.Select(ClonePair).ToList()
        };
    }

    private static PersistedPair ClonePair(PersistedPair source)
    {
        return new PersistedPair
        {
            PairId = source.PairId,
            IsAutoFlow = source.IsAutoFlow,
            SlotNumber = source.SlotNumber,
            OpenedAtUtc = source.OpenedAtUtc,
            CurrentHoldingSeconds = source.CurrentHoldingSeconds,
            CurrentPhase = source.CurrentPhase,
            CurrentPositionSide = source.CurrentPositionSide,
            CurrentOpenMode = source.CurrentOpenMode,
            LastSeenStartTimeHold = source.LastSeenStartTimeHold,
            LastSeenEndTimeHold = source.LastSeenEndTimeHold,
            Stt = source.Stt,
            LegA = CloneLeg(source.LegA),
            LegB = CloneLeg(source.LegB)
        };
    }

    private static PersistedPairLeg CloneLeg(PersistedPairLeg source)
    {
        return new PersistedPairLeg
        {
            MapName = source.MapName,
            Ticket = source.Ticket,
            Symbol = source.Symbol,
            Volume = source.Volume,
            TradeType = source.TradeType,
            OpenPrice = source.OpenPrice,
            OpenEaTimeLocal = source.OpenEaTimeLocal,
            AppOpenRequestRawMs = source.AppOpenRequestRawMs,
            Platform = source.Platform,
            OpenExecutionMs = source.OpenExecutionMs
        };
    }

    private static PersistedWaitWindow CloneWaitWindow(PersistedWaitWindow source)
    {
        return new PersistedWaitWindow
        {
            PreviousPairId = source.PreviousPairId,
            ClosedAtUtc = source.ClosedAtUtc,
            CurrentWaitSeconds = source.CurrentWaitSeconds,
            LastSeenStartWaitTime = source.LastSeenStartWaitTime,
            LastSeenEndWaitTime = source.LastSeenEndWaitTime,
            LastSeenStartTimeHold = source.LastSeenStartTimeHold,
            LastSeenEndTimeHold = source.LastSeenEndTimeHold
        };
    }
}
