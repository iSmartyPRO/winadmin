using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Security;

/// <summary>Пишет и читает журнал аудита управляющих действий.</summary>
public sealed class AuditService : IAuditService
{
    private readonly WinAdminDbContext _db;

    public AuditService(WinAdminDbContext db) => _db = db;

    public async Task WriteAsync(AuditEntryDto entry, CancellationToken ct = default)
    {
        _db.AuditEntries.Add(new AuditEntryEntity
        {
            Timestamp = entry.Timestamp == default ? DateTimeOffset.UtcNow : entry.Timestamp,
            Actor = entry.Actor,
            Action = entry.Action,
            Target = entry.Target,
            Success = entry.Success,
            Details = entry.Details,
            SourceIp = entry.SourceIp,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEntryDto>> QueryAsync(int limit = 200, string? actor = null, string? target = null, CancellationToken ct = default)
    {
        IQueryable<AuditEntryEntity> q = _db.AuditEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(actor))
            q = q.Where(e => e.Actor == actor);
        if (!string.IsNullOrWhiteSpace(target))
            q = q.Where(e => e.Target == target);

        var rows = await q.OrderByDescending(e => e.Timestamp)
            .Take(Math.Clamp(limit, 1, 2000))
            .ToListAsync(ct);

        return rows.Select(e => new AuditEntryDto
        {
            Id = e.Id,
            Timestamp = e.Timestamp,
            Actor = e.Actor,
            Action = e.Action,
            Target = e.Target,
            Success = e.Success,
            Details = e.Details,
            SourceIp = e.SourceIp,
        }).ToList();
    }
}
