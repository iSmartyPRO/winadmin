using System.DirectoryServices.Protocols;
using System.Text;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Значения LDAP для записи и перевод ошибок в тексты для оператора.</summary>
public static class AdWriteRequests
{
    private const int AccountDisable = 0x2;

    /// <summary>Глобальная группа безопасности (ADS_GROUP_TYPE_GLOBAL_GROUP | ADS_GROUP_TYPE_SECURITY_ENABLED).</summary>
    public static readonly string GlobalSecurityGroupType = unchecked((int)0x80000002).ToString();

    public static byte[] PasswordValue(string password) => Encoding.Unicode.GetBytes("\"" + password + "\"");

    public static int ToggleDisabled(int uac, bool enabled) => enabled ? uac & ~AccountDisable : uac | AccountDisable;

    /// <summary>
    /// Изменение атрибутов одним (атомарным) запросом. Пустое значение — Replace без значений: удаляет атрибут,
    /// а если его нет — не ошибка (Delete отсутствующего атрибута провалил бы весь запрос).
    /// </summary>
    public static ModifyRequest BuildModify(string dn, IReadOnlyDictionary<string, string?> changes)
    {
        var request = new ModifyRequest(dn);
        foreach (var (name, value) in changes)
        {
            var mod = new DirectoryAttributeModification { Name = name, Operation = DirectoryAttributeOperation.Replace };
            if (!string.IsNullOrEmpty(value)) mod.Add(value);
            request.Modifications.Add(mod);
        }
        return request;
    }

    public static string Rid(string sid) => sid[(sid.LastIndexOf('-') + 1)..];

    public static string Parent(string dn) => string.Join(",", DnUtils.Split(dn).Skip(1));

    public static string Rdn(string dn) => DnUtils.Split(dn)[0];

    public static Exception Translate(ResultCode code, string account, string operation, string target, string? serverMessage) => code switch
    {
        ResultCode.InsufficientAccessRights => new AdWriteException((int)code,
            $"Учётке {account} не хватает прав на {operation} для {target}. См. «Проверка окружения»."),
        ResultCode.NoSuchObject => new AdWriteException((int)code, "Объект не найден в AD."),
        ResultCode.EntryAlreadyExists => new AdWriteException((int)code, "Объект уже существует."),
        ResultCode.ConstraintViolation => new AdWriteException((int)code,
            "Значение не принято: пароль не соответствует политике домена (длина, сложность, история) или нарушено ограничение атрибута."),
        ResultCode.UnwillingToPerform => new AdWriteException((int)code, $"AD отклонил операцию «{operation}» для {target}."),
        ResultCode.Busy or ResultCode.Unavailable => new DirectoryUnavailableException("Контроллер домена недоступен"),
        _ => new AdWriteException((int)code, $"Ошибка AD при операции «{operation}» ({code})."),
    };
}
