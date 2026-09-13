# Sipariş ve Finans — yetkili başlangıç
Tarih: 2026-09-13. Bu kayıt Sinan'ın iki yeni Backend–Claude çiftini başlatma talimatının uygulanmasıdır; yeni governance, ERP kabulü veya production yetkisi değildir. Başlangıç main: `f6fa07d026403649d6c815b84fd850e7ceeaf092`.

## Gerçek görevler

| İş | Görev / çalışma alanı | Başlangıç |
|---|---|---|
| SİPARİŞ BACKEND | Codex `01a09afa-9af2-7521-9a0a-dc851e496133`; `C:/Users/goyme/.codex/worktrees/c1f7/GoymenERP-codex` | Spark medium ile oluşturuldu. İlk tur context-window hatasından sonra aynı görev/modelde dar kurtarma tamamlandı; 11 satırlık kaynak/eksik/owner tablosu koordinatöre iletildi. Kod/test/runtime değişmedi. |
| FİNANS BACKEND | Codex `01a09afa-9af2-7521-9a0a-dca3f1da0e6a`; `C:/Users/goyme/.codex/worktrees/a583/GoymenERP-codex` | Spark medium, `codex/w4-finans-start-20260913`; başlangıç envanteri çıktı, coordinator kaynak/öncül sınırını daralttı. |
| SIPARIS-FRONTEND | [Claude Remote Control](https://claude.ai/code/session_01TKB6kmZnZc6ZZzWhKhuUTH); `C:/src/GoymenERP-siparis-frontend-20260913` | `claude/orders-frontend-20260913`; yerel session `a9de0095-3f39-46d3-9215-b3e9cb2c7021`; prompt okuma/kaynak incelemesi gerçekten başladı. |
| FINANS-FRONTEND | [Claude Remote Control](https://claude.ai/code/session_01XNySWYntuKPGW7DKu38Bhq); `C:/src/GoymenERP-finans-frontend-20260913` | `claude/finance-frontend-20260913`; yerel session `217ee592-b7a9-4fd9-9116-f05fcf8b12bc`; prompt okuma/kaynak incelemesi gerçekten başladı. |

Mevcut **KOORDİNATÖR** `01a08cbe-8af5-73d1-bf7e-39353ffc82da`, Astra; mevcut **ChatGPT-BUSINESS** `6aa2b843-6c04-83eb-9721-d4b1e69a06e4` korunur. Business'a başlangıç promptu araçla gönderildi, active durumu görüldü; model seçicisi bu tur değiştirilmedi veya yeniden doğrulanmadı. Koordinatöre gerçek kimlik/başlangıç/hata/Claude URL paketi gönderildi. İki yeni coordinator/Business veya mükerrer modül yazarı yok.

Claude kurulu CLI'nin desteklediği `--bg --name --remote-control` seçenekleriyle aynı makinede ayrı dizinlerde başlatıldı. Model ve izin ayarları değiştirilmedi. Yerel `claude agents --json` session kimliklerini ve çalışan durumunu doğruladı. Yeni app API/SQL/browser kabul ortamı başlatılmadı; worktree hazırlığı **ENV_READY değildir**.

## Altı hazır prompt

İlk gönderilmiş tam mesajlar aynı worktree'nin `artifacts/module-launch-20260913/` dizininde korunur. Buradaki Git sürümü aynı içeriği ortak bölümü tekilleştirerek saklar; canlı göreve yeniden gönderme:

- [Ortak başlangıç ve öğrenme kuralları](startup/COMMON.md)
- [Sipariş Backend](startup/01-siparis-backend.md)
- [Sipariş Claude Frontend](startup/02-siparis-frontend.md)
- [Finans Backend](startup/03-finans-backend.md)
- [Finans Claude Frontend](startup/04-finans-frontend.md)
- [Mevcut koordinatör](startup/05-koordinator.md)
- [Mevcut Business](startup/06-business.md)

## Başlangıçta somutlaştırılan öncüller

| İhtiyaç | Kaynak gözlemi | Mevcut sahip / sonraki iş |
|---|---|---|
| Siparişte kullanılabilir Ürün | ProductReader reference options eksik; code-only catalog birimi reddeder. Taslak kart/Sellable tek başına ticari uygunluk değildir. | Mevcut Ürün BE/FE; Business'ın ilk akışa gerçekten gereken birim/uygunluk/seçici sözleşmesi kadar ayrı görev. U01/U02 kapanışı yeniden açılmaz. |
| Şirkette kullanılabilir Cari | GetAuthorizedPartyAsync Party ile CompanyPartyAccount'ı ayırır; roles=0 geçerlidir. | Mevcut Cari BE/FE; aktiflik/görünürlük/account contract ihtiyacı varsa dar ek. Otomatik hesap açmanın B yetki önerisi hâlâ onaylı değildir. |
| Organizasyon / genel ayarlar ayrımı | IOrganizationService yetkili okumaları mevcut; OrganizationSettingsPage genel Policy Registry değildir. | Mevcut Org sahibi kendi okumasını sağlar; genel Settings sahibi/işi coordinator tarafından gerçekten atanır. |
| Ortak uygulama mount'u | product/SHARED_MOUNT_HANDOFF.md; shell router bağlama işi ayrı. | Mevcut shell sahibi; iki yeni Claude shared router'a paralel yazmaz. |
| Finans gerçek başlangıcı | W4-FINANS→W2-ACIKKALEM→W2-IRSALIYE+W1-SETTINGS; runtime ve ayrıntılı acceptance eksik. | Business kaynaklı minimum kesit/öncül ve açık sorular; Finance yeni permission/flag/formül/policy icat ederek hazır olmaz. |

Kaynaklar `origin/main@f6fa07d` üzerinden salt okunur doğrulandı; yeni test/SQL PASS iddiası değildir. Öncül tablosu mevcut sahipliği değiştirmez; coordinator görevlendirmesi kaynaklı kabul ihtiyacına göre yapılır. Genel bütün W1 görevi completed sayılmaz.

## Uygulama ve bitiş

Business bir küçük akış önde; Backend teknik contract ile baştan katılır; Claude ve Backend bu ortak sözleşmede paralel çalışır, gerçek bağlantı erken denenir. İlk kritik test geliştirmeyle birlikte; geniş doğrulama son aday ve zorunlu CI'da. İlgili risk/SQL/browser/inceleme kapısı korunur. Yeni defect→ortak kod veya meaningful regression test→kısa kaynaklı ders; yeni şablon/münazara/izleme kampanyası yok.

Cari'nin optional-role ACK'i Finance standardı değildir. Product'ın grup-geneli model/yetkisi şirket kapsamlı Finance veya Sales'e taşınamaz. Para kaydı, açık kalem, dağıtım ve muhasebe ayrı; dağıtım ikinci para hareketi üretmez. Unknown sonuç/key/context/payload korunur; geç yanıt/abort readiness'i yanlış açamaz. Test örneği yeni modül kanıtı diye etiketlenmez.

Kapanan teslimleri/başarılı CI'ı yeni tanışma veya makbuz için tekrar çalıştırma. İlk başlangıçta Sipariş Spark bağlam sınırı gerçekten görüldü; aynı geniş repo çıktısını tekrar etmek yerine bounded recovery coordinator'a bildirildi. Finans ilk keşfindeki kaynaksız somut flag/limit adları ham öneridir, contract değildir. Bunlar profesyonel teslimde review/gerçek test gereğini kaldırmayan erken teşhislerdir.

Bu dosya anlık sürekli status registry değildir. Sonraki görev/PR/kanıt/merge gerçekliği mevcut coordinator ve Issue #66 üzerinden takip edilir; source kabulü, runtime kabulü, tam modül ve production birbirinin yerine geçmez.
