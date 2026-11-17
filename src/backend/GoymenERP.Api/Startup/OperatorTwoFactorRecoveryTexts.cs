using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Startup;

internal static class OperatorTwoFactorRecoveryTexts
{
    internal const string Usage = "Kullanım: identity recover-two-factor (hedef, işlem anahtarı ve gerekçe yalnız etkileşimli terminalde sorulur).";
    internal const string TargetPrompt = "Kurtarılacak yerel kullanıcının kullanıcı adı: ";
    internal const string KeyPrompt = "İşlem anahtarı (yeni anahtar için boş bırakın; aynı işlemi yinelerken önceki anahtarı kullanın): ";
    internal const string ReasonPrompt = "Kurtarma gerekçesi (parola, doğrulayıcı tohumu veya kurtarma kodu yazmayın): ";
    internal const string ConfirmationPrompt = "Yalnız bu hedefin iki aşamalı girişini kaldırmayı onaylamak için KURTAR yazın: ";
    internal const string ConfirmationAnswer = "KURTAR";
    internal static string Key(string key) => $"İşlem anahtarı: {key}. Sonucu alamazsanız bu anahtarla aynı işlemi yeniden sorgulayın.";
    internal static string Target(LocalTwoFactorOperatorRecoveryConfirmationV1 value) =>
        $"Hedef: {value.TargetUserName} ({value.TargetUserId:D}); kullanıcı {(value.TargetIsActive ? "etkin" : "pasif")}; onay sürümleri kullanıcı {value.UserVersion}, kimlik {value.CredentialVersion}, doğrulayıcı {value.SecretVersion}.";
    internal static string Environment(string environment, string? server, string? catalog) => $"Ortam: {environment}; veritabanı: {server ?? "bilinmiyor"} / {catalog ?? "bilinmiyor"}.";
    internal static string Operator(LocalTwoFactorRecoveryOperatorDeclarationV1 value) => $"Sunucu operatörü: {value.OperatingSystemUser}; makine: {value.MachineName}.";
    internal const string Effect = "Parola, kullanıcı etkinliği, roller ve şirket yetkileri değişmez. İşlem başarılı olursa bu doğrulayıcı devre dışı kalır ve mevcut oturumlar sonlanır.";
    internal const string Replay = "Bu anahtarın daha önce kaydedilen sonucu gösterilecek. Sonradan kurulmuş bir doğrulayıcıya ve yeni oturumlara dokunulmaz.";
    internal static string Completed(LocalTwoFactorOperatorRecoveryResultV1 value) => value.IsReplay
        ? $"Özgün kurtarma sonucu: başarılı; hedef {value.TargetUserId:D}, kaydedilen kimlik sürümü {value.RecordedCredentialVersion}. Bu yanıt güncel iki aşamalı giriş durumunu göstermez."
        : $"Kurtarma kaydedildi; hedef {value.TargetUserId:D}, kimlik sürümü {value.RecordedCredentialVersion}. Kullanıcı yeniden giriş yaptıktan sonra yeni doğrulayıcı kurabilir.";
    internal static string Refused(LocalTwoFactorOperatorRecoveryResultV1 value) => (value.IsReplay ? "Özgün işlem sonucu: " : "Kurtarma yapılmadı: ") + (value.Status switch
    {
        LocalTwoFactorOperatorRecoveryStatusV1.AlreadyDisabled => "onaylanan hedefte iki aşamalı giriş zaten kapalıydı.",
        LocalTwoFactorOperatorRecoveryStatusV1.ConcurrentChange => "onaydan sonra kullanıcı, kimlik veya doğrulayıcı sürümü değişti; yeni durumu inceleyip yeni anahtar kullanın.",
        LocalTwoFactorOperatorRecoveryStatusV1.InvalidInvocation => "komut satırına ek argüman verilemez.",
        LocalTwoFactorOperatorRecoveryStatusV1.NotInteractive => "etkileşimli sunucu terminali gerekiyor.",
        LocalTwoFactorOperatorRecoveryStatusV1.InvalidRequest => "hedef, anahtar, gerekçe veya operatör bilgisi geçersiz.",
        LocalTwoFactorOperatorRecoveryStatusV1.TargetNotFound => "yerel kullanıcı bulunamadı.",
        LocalTwoFactorOperatorRecoveryStatusV1.NotConfirmed => "operatör onayı alınmadı.",
        LocalTwoFactorOperatorRecoveryStatusV1.IdempotencyConflict => "bu anahtar başka hedef, operatör veya gerekçeye bağlı; özgün sonuç açıklanmadı.",
        LocalTwoFactorOperatorRecoveryStatusV1.AuditUnavailable => "denetim kaydı yazılamadı; bu işlem geri alındı.",
        LocalTwoFactorOperatorRecoveryStatusV1.SecurityEventUnavailable => "güvenlik kaydı yazılamadı; bu işlem geri alındı.",
        _ => "kaynak veya işlem sonucu doğrulanamadı. Yeni anahtar üretmeden aynı anahtarla yeniden sorgulayın.",
    });
}
