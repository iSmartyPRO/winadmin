using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Security;

namespace WinAdmin.Infrastructure.ActiveDirectory.Users;

public sealed partial class AdUsersService
{
    public async Task<AdUserView> UpdateAttributesAsync(IAccessContext actor, string sam, IReadOnlyDictionary<string, string?> changes, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var clean = AdUserValidation.Clean(changes, settings.EditableAttributes);
        var located = await ManagedAsync(actor, sam, PermissionIds.AdUsersEdit, st, settings, ct);
        var user = located.User;

        var diff = clean.Where(kv => !string.Equals(Normalize(user.Attr(kv.Key)), kv.Value, StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        if (diff.Count == 0) return View(user, st, settings);

        string details = string.Join("; ", diff.Select(kv => $"{kv.Key}: «{user.Attr(kv.Key) ?? ""}» → «{kv.Value ?? ""}»"));
        await WriteAuditedAsync(actor, "user.attributes.update", user.Dn, details,
            () => writer.ModifyAttributesAsync(user.Dn, diff, ct), ct);

        var updated = user.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in diff) updated[k] = v;
        return View(user with
        {
            Attributes = updated,
            DisplayName = diff.TryGetValue("displayName", out var dn) ? dn : user.DisplayName,
        }, st, settings);
    }

    public async Task SetPhotoAsync(IAccessContext actor, string sam, byte[]? photo, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        if (photo is not null)
        {
            if (!AdUserValidation.IsImage(photo)) throw new ArgumentException("Фото должно быть в формате JPEG или PNG.");
            if (photo.Length > settings.PhotoMaxKb * 1024) throw new ArgumentException($"Фото больше {settings.PhotoMaxKb} КБ.");
        }
        var located = await ManagedAsync(actor, sam, PermissionIds.AdUsersEdit, st, settings, ct);
        await WriteAuditedAsync(actor, photo is null ? "user.photo.remove" : "user.photo.update", located.User.Dn,
            photo is null ? "фото удалено" : $"фото {photo.Length / 1024 + 1} КБ",
            () => writer.SetPhotoAsync(located.User.Dn, photo, ct), ct);
    }

    public async Task<AdUserView> MoveAsync(IAccessContext actor, string sam, string projectDn, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var located = await ManagedAsync(actor, sam, PermissionIds.AdUsersMove, st, settings, ct);
        string target = AdGuard.EnsureManaged(projectDn, st);
        AdGuard.EnsureInScope(actor, PermissionIds.AdUsersMove, target);
        string targetOu = $"OU={st.UsersOuName},{target}";
        if (!await reader.ExistsAsync(targetOu, ct))
            throw new InvalidOperationException($"В проекте нет OU «{st.UsersOuName}».");
        if (DnUtils.IsUnderOrSame(AdWriteRequests.Parent(located.User.Dn), targetOu) && DnUtils.IsUnderOrSame(targetOu, AdWriteRequests.Parent(located.User.Dn)))
            return View(located.User, st, settings);

        string newDn = "";
        string from = DnUtils.RelativeOuPath(located.ProjectDn!, st.RootOu!)[0];
        string to = DnUtils.RelativeOuPath(target, st.RootOu!)[0];
        await WriteAuditedAsync(actor, "user.move", located.User.Dn, $"{from} → {to}",
            async () => newDn = await writer.MoveAsync(located.User.Dn, targetOu, ct), ct);
        return View(located.User with { Dn = newDn }, st, settings);
    }

    public async Task<string?> ResetPasswordAsync(IAccessContext actor, string sam, string? password, bool generate, bool mustChange, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        if (!generate && (password is null || password.Length < 8))
            throw new ArgumentException("Пароль не короче 8 символов (или выберите «сгенерировать»).");
        var located = await ManagedAsync(actor, sam, PermissionIds.AdUsersPassword, st, settings, ct);
        string value = generate ? PasswordGenerator.Generate() : password!;
        await WriteAuditedAsync(actor, "user.password.reset", located.User.Dn,
            (generate ? "сгенерирован" : "задан оператором") + (mustChange ? ", смена при входе" : ""),
            () => writer.ResetPasswordAsync(located.User.Dn, value, mustChange, ct), ct);
        return generate ? value : null;
    }

    /// <summary>Только управляемые (не уволенные) пользователи: зона + область.</summary>
    private async Task<Located> ManagedAsync(IAccessContext actor, string sam, string permission,
        AdStructureSettings st, AdUsersSettings settings, CancellationToken ct)
    {
        var located = await LocateAsync(actor, sam, permission, st, settings, ct);
        if (located.Terminated)
            throw new InvalidOperationException("Пользователь уволен — сначала восстановите его.");
        return located;
    }

    private async Task WriteAuditedAsync(IAccessContext actor, string action, string target, string details, Func<Task> write, CancellationToken ct)
    {
        try
        {
            await write();
        }
        catch (Exception ex) when (ex is AdWriteException or DirectoryUnavailableException)
        {
            await AuditAsync(actor, action, target, false, details + ": " + ex.Message, ct);
            throw;
        }
        await AuditAsync(actor, action, target, true, details, ct);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
