using WinAdmin.Core.ActiveDirectory.Folders;

namespace WinAdmin.Infrastructure.ActiveDirectory.Folders;

/// <summary>Каталог папок на 60 с (чтение всех групп и участников — дорогое); запись сбрасывает.</summary>
public sealed class FolderCatalogCache(TimeProvider? time = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset At, FolderCatalogResult Value)? _entry;

    public async Task<FolderCatalogResult> GetAsync(Func<Task<FolderCatalogResult>> load)
    {
        if (_entry is { } e && _time.GetUtcNow() - e.At < Lifetime) return e.Value;
        await _gate.WaitAsync();
        try
        {
            if (_entry is { } again && _time.GetUtcNow() - again.At < Lifetime) return again.Value;
            var value = await load();
            _entry = (_time.GetUtcNow(), value);
            return value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _entry = null;
}
