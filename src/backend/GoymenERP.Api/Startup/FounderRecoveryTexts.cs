using System.Globalization;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Startup;

/// <summary>
/// The Turkish operator texts of <c>identity recover-founder</c> (spec ID-2r) in one place (localizable). No text echoes
/// a password, hash, stamp or connection secret; a refusal also prints its stable English code for the runbook.
/// </summary>
internal static class FounderRecoveryTexts
{
    internal const string Usage = "Kullanım: identity recover-founder reactivate | identity recover-founder reset-password";

    internal const string ReactivateSubcommand = "reactivate";

    internal const string ResetPasswordSubcommand = "reset-password";

    internal const string PasswordPrompt = "Yeni parola: ";

    internal const string PasswordConfirmationPrompt = "Yeni parola (tekrar): ";

    internal const string PolicyViolationPrefix = "Parola politikası karşılanmadı: ";

    internal const string ConfirmationHeader = "Kurucu hesabı kurtarma işlemi aşağıdaki hedefte çalışacak:";

    internal const string ConfirmationAnswer = "evet";

    internal const string ConfirmationPrompt = "Devam etmek için 'evet' yazın: ";

    internal const string UnknownValue = "(bilinmiyor)";

    internal static string Subcommand(FounderRecoverySubcommand subcommand) => subcommand switch
    {
        FounderRecoverySubcommand.Reactivate => "Pasif kurucu hesabını yeniden etkinleştir (reactivate)",
        FounderRecoverySubcommand.ResetPassword => "Kurucu hesabına tek kullanımlık geçici parola ver (reset-password)",
        _ => throw new ArgumentOutOfRangeException(nameof(subcommand), subcommand, null),
    };

    internal static string Environment(string environmentName) => $"  Ortam: {environmentName}";

    internal static string Database(string? server, string? catalog) =>
        $"  Veritabanı: {server ?? UnknownValue} / {catalog ?? UnknownValue}";

    internal static string FounderUserName(string userName) => $"  Kurucu kullanıcı adı: {userName}";

    internal static string Operation(FounderRecoverySubcommand subcommand) => $"  İşlem: {Subcommand(subcommand)}";

    internal const string CompaniesHeader = "  Şirketler:";

    internal const string NoCompanies = "  Şirketler: (üyelik yok)";

    internal static string Company(FounderRecoveryCompany company) => $"    - {company.Code} {company.LegalName}";

    internal static string Reactivated(Guid founderUserId, int companyCount) =>
        $"Kurucu hesabı yeniden etkinleştirildi (kullanıcı {founderUserId:D}, {companyCount} şirkette yönetici). "
        + "Girilen parola ilk girişte değiştirilmelidir.";

    internal static string TemporaryPasswordIssued(Guid founderUserId, DateTimeOffset expiresAtUtc) =>
        $"Kurucu hesabına geçici parola verildi (kullanıcı {founderUserId:D}). Geçici parola tek kullanımlıktır, "
        + $"{expiresAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC'ye kadar geçerlidir "
        + "ve ilk girişte değiştirilmelidir.";

    internal static string Refusal(FounderRecoveryStatus status) =>
        $"{RefusalText(status)} ({FounderRecoveryRefusalCodes.Of(status)})";

    private static string RefusalText(FounderRecoveryStatus status) => status switch
    {
        FounderRecoveryStatus.SubcommandRequired =>
            "Alt komut verilmedi; 'reactivate' veya 'reset-password' açıkça yazılmalıdır.",
        FounderRecoveryStatus.NotBootstrapped => "Kurucu hesabı hiç oluşturulmamış; önce bootstrap-founder çalışmalıdır.",
        FounderRecoveryStatus.FounderNotFound => "Kurucu kullanıcısı veya yerel kimlik bilgisi bulunamadı.",
        FounderRecoveryStatus.FounderActive => "Kurucu hesabı zaten etkin; reactivate gerekmez.",
        FounderRecoveryStatus.FounderInactive => "Kurucu hesabı pasif; önce reactivate çalışmalıdır.",
        FounderRecoveryStatus.NotNeeded =>
            "Kurtarma gerekmiyor; işlem ekrandan yetkili bir kullanıcı tarafından yapılabilir.",
        FounderRecoveryStatus.ConcurrentChange =>
            "Hesap onaydan sonra değişti veya şirket kilidi meşgul; hiçbir değişiklik yazılmadı.",
        FounderRecoveryStatus.ConfigurationMissing =>
            "Identity:FounderRecovery:TemporaryPasswordLifetime geçersiz; pozitif bir süre olmalıdır.",
        FounderRecoveryStatus.AdminRoleNotConfigured =>
            "Identity:AdminRoleCode yapılandırılmamış veya rol bulunamadı; yönetici rolü tahmin edilmez.",
        FounderRecoveryStatus.NotInteractive =>
            "Komut yalnız etkileşimli bir terminalden çalışır; parola argüman, ortam değişkeni veya yönlendirilmiş girişle verilemez.",
        FounderRecoveryStatus.PasswordMismatch => "Parolalar eşleşmiyor.",
        FounderRecoveryStatus.PolicyViolation => "Parola politikası karşılanmadı.",
        FounderRecoveryStatus.NotConfirmed => "İşlem onaylanmadı; hiçbir değişiklik yazılmadı.",
        FounderRecoveryStatus.SecurityEventRefused => "Güvenlik olayı kaydedilemedi; hiçbir değişiklik yazılmadı.",
        _ => "Kurucu hesabı kurtarılamadı.",
    };
}
