namespace WinAdmin.Core.Models;

public sealed record ModuleState(string Id, bool Enabled, bool Available, string? UnavailableReason);

/// <summary>Поле настроек модуля для формы UI. Kind: string | number | boolean | stringList | secret.</summary>
public sealed record SettingsField(string Name, string Title, string Kind);
