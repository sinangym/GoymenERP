using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Startup;

/// <summary>
/// The Turkish operator texts of <c>identity bootstrap-founder</c> in one place (localizable). No text echoes a
/// password; the result names only the outcome and the founder's user id.
/// </summary>
internal static class FounderBootstrapTexts
{
    internal const string Usage = "Kullanım: identity bootstrap-founder";

    internal const string LocalAccountModeRequired =
        "Kurucu hesabı yalnız yerel hesap kimlik doğrulaması (Authentication:Mode=LocalAccount) açıkken oluşturulabilir.";

    internal const string UserNamePrompt = "Kullanıcı adı: ";

    internal const string DisplayNamePrompt = "Görünen ad: ";

    internal const string PasswordPrompt = "Parola: ";

    internal const string PasswordConfirmationPrompt = "Parola (tekrar): ";

    internal const string PolicyViolationPrefix = "Parola politikası karşılanmadı: ";

    internal static string Completed(Guid founderUserId, int companyCount) =>
        $"Kurucu hesabı oluşturuldu (kullanıcı {founderUserId:D}, {companyCount} şirkette yönetici). "
        + "İlk girişte parola değiştirilmelidir.";

    internal static string Refusal(FounderBootstrapStatus status) => status switch
    {
        FounderBootstrapStatus.NotInteractive =>
            "Komut yalnız etkileşimli bir terminalden çalışır; parola argüman, ortam değişkeni veya yönlendirilmiş girişle verilemez.",
        FounderBootstrapStatus.AdminRoleNotConfigured =>
            "Identity:AdminRoleCode yapılandırılmamış; yönetici rolü tahmin edilmez.",
        FounderBootstrapStatus.AdminRoleInvalid =>
            "Identity:AdminRoleCode etkin ve en az bir kritik yetki taşıyan bir rolü göstermiyor.",
        FounderBootstrapStatus.AlreadyBootstrapped =>
            "Kurucu hesabı daha önce oluşturulmuş; komut yalnız bir kez çalışır.",
        FounderBootstrapStatus.AdminAlreadyAssigned =>
            "Sistemde süresi dolmamış bir yönetici ataması var; kurucu hesabı oluşturulmadı.",
        FounderBootstrapStatus.NoCompanies =>
            "Tanımlı şirket yok; önce şirket kaydı oluşturulmalıdır.",
        FounderBootstrapStatus.UserNameInvalid => "Kullanıcı adı geçerli değil.",
        FounderBootstrapStatus.UserNameTaken => "Bu kullanıcı adı kullanılıyor.",
        FounderBootstrapStatus.DisplayNameInvalid => "Görünen ad boş veya çok uzun.",
        FounderBootstrapStatus.PasswordMismatch => "Parolalar eşleşmiyor.",
        FounderBootstrapStatus.PolicyViolation => "Parola politikası karşılanmadı.",
        FounderBootstrapStatus.SecurityEventRefused =>
            "Güvenlik olayı kaydedilemedi; hiçbir değişiklik yazılmadı.",
        FounderBootstrapStatus.OperatorDeclarationMissing =>
            "Operatör beyanı (işletim sistemi kullanıcısı ve makine adı) okunamadı; hiçbir değişiklik yazılmadı.",
        FounderBootstrapStatus.AuditRefused => "Denetim kaydı yazılamadı; hiçbir değişiklik yazılmadı.",
        _ => "Kurucu hesabı oluşturulamadı.",
    };
}
