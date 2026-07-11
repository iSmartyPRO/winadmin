using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.EventLogs;

/// <summary>Читает журналы событий Windows через Event Log API с фильтрацией на стороне ОС.</summary>
[SupportedOSPlatform("windows")]
public sealed class EventLogService : IEventLogService
{
    private const int ScanCap = 5000;
    private readonly ILogger<EventLogService> _logger;

    public EventLogService(ILogger<EventLogService> logger) => _logger = logger;

    public IReadOnlyList<string> GetLogNames()
    {
        try
        {
            var session = new EventLogSession();
            return session.GetLogNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось перечислить журналы событий");
            return Array.Empty<string>();
        }
    }

    public EventLogQueryResult Query(EventLogQueryRequest request)
    {
        var xpath = EventLogQueryHelpers.BuildXPathFilter(request);
        var entries = new List<EventLogEntryDto>();
        var scanned = 0;
        var truncated = false;

        try
        {
            var query = new EventLogQuery(request.LogName, PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(query);

            EventRecord? record;
            while ((record = reader.ReadEvent()) != null)
            {
                using (record)
                {
                    scanned++;
                    if (scanned > ScanCap)
                    {
                        truncated = true;
                        break;
                    }

                    string? message;
                    try { message = record.FormatDescription(); }
                    catch (EventLogException) { message = null; }

                    if (!EventLogQueryHelpers.MatchesSubstring(message, request.Keyword))
                        continue;

                    var user = EventLogQueryHelpers.ExtractUserNameFromXml(record.ToXml())
                        ?? TryTranslateSid(record.UserId);

                    if (!EventLogQueryHelpers.MatchesSubstring(user, request.User))
                        continue;

                    entries.Add(new EventLogEntryDto
                    {
                        Id = record.RecordId ?? 0,
                        TimeCreated = record.TimeCreated ?? DateTime.MinValue,
                        LogName = request.LogName,
                        ProviderName = record.ProviderName,
                        EventId = record.Id,
                        Level = record.Level?.ToString(),
                        LevelDisplayName = SafeLevelDisplayName(record),
                        User = user,
                        Message = message,
                        MachineName = record.MachineName,
                    });

                    if (entries.Count >= request.MaxRecords)
                    {
                        truncated = true;
                        break;
                    }
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Нет доступа к журналу {LogName}", request.LogName);
            throw new EventLogAccessDeniedException(
                $"Нет доступа к журналу «{request.LogName}» — требуются права администратора.");
        }
        catch (EventLogNotFoundException ex)
        {
            _logger.LogWarning(ex, "Журнал {LogName} не найден", request.LogName);
            throw new EventLogMissingException($"Журнал «{request.LogName}» не найден на этой машине.");
        }

        return new EventLogQueryResult { Entries = entries, Truncated = truncated, ScannedCount = scanned };
    }

    private static string? TryTranslateSid(SecurityIdentifier? sid)
    {
        if (sid == null) return null;
        try { return sid.Translate(typeof(NTAccount)).ToString(); }
        catch (IdentityNotMappedException) { return sid.Value; }
    }

    private static string? SafeLevelDisplayName(EventRecord record)
    {
        try { return record.LevelDisplayName; }
        catch (EventLogNotFoundException) { return record.Level?.ToString(); }
    }
}
