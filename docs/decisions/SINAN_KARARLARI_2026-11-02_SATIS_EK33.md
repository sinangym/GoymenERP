# Sinan Kararları — 2026-11-02 (fatura onayını müdürlere açma; kurun elle içe aktarılması)

Bu belge önceki karar dosyalarını silmez; SAT-D204 ve SAT-D205'i ekler. Persona kartında EK31/EK32 güncellemesinin açtığı
iki hücre grubunu (**NS-13** fatura taslağı ve tolerans farkı onayı, **NS-14** kurun elle içe aktarılması) cevaplar.
SAT-D88, SAT-D106, SAT-D169, EK28 SAT-D187/D188/D190, EK31 SAT-D198/D199 ve EK32 SAT-D202 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **SAT-D88'in onaylayan kümesi genişler:** "Fatura taslağını **Muhasebe** onaylar" ifadesi yerine fatura taslağını
  **Muhasebe, Muhasebe Müdürü, Genel Müdür veya Admin** onaylar (SAT-D204). Aynı genişleme SAT-D106'daki tolerans aşımı
  onayı ("faturayı onaylayan Muhasebe onaylar") için de geçerlidir. Hazırlayan ≠ onaylayan kuralı (SAT-D88/SAT-D106, P6)
  aynen kalır. EK32 SAT-D202'nin "bu karar onaylayan kümesini genişletmez" cümlesi kendi kapsamı için doğrudur; genişleme
  bu belgeyle yapılır.

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevaplar: bir tur, iki soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Fatura taslağını onaylama (ve resmî fatura ile ERP taslağı arasındaki tolerans farkını onaylama) şu an yalnız Muhasebe'de. Muhasebe Müdürü, Genel Müdür ve Admin de onaylayabilsin mi? (Hazırlayan kendi taslağını yine onaylayamaz.) | Evet, üçü de onaylasın (Önerilen) | "Muhasebe, Muhasebe Müdürü, Genel Müdür ve Admin fatura taslağını ve tolerans farkını onaylayabilir. Muhasebeci izindeyken iş durmaz." (seçilmeyenler: "Muhasebe Müdürü ve GM" — "Admin onaylamaz (yalnız sistem yöneticisi); Muhasebe, Muhasebe Müdürü ve Genel Müdür onaylar."; "Yalnız Muhasebe" — "Bugünkü gibi kalsın; müdürler onaylamaz.") |
| 2 | TCMB kurlarını elle 'şimdi çek' (içe aktarma) düğmesini kim kullanabilsin? Otomatik çekim zaten olacak; bu, eksik günü elle tetiklemek için. | Muhasebe ve üstü (Önerilen) | "Muhasebe, Muhasebe Müdürü, Genel Müdür ve Admin — kuru düzeltebilen kişilerle aynı." (seçilmeyenler: "Yalnız Admin" — "Elle tetikleme sistem işi sayılsın, yalnız Admin yapsın."; "Kur görenlerin hepsi" — "Satış dahil kurları görebilen herkes elle çekebilsin.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D204 | Fatura taslağı ve tolerans farkı onayı | Fatura taslağını (API-33) ve resmî fatura ile ERP taslağı arasındaki SAT-D89 toleransını aşan farkı (SAT-D106, API-35) **Muhasebe, Muhasebe Müdürü, Genel Müdür ve Admin** onaylar (soru 1). **Taslağı hazırlayan kişi onu onaylayamaz** (SAT-D88/SAT-D106, kişi düzeyinde hazırlayan ≠ onaylayan; P6). Uygulama ayrıntısı (geri alınabilir): kural personaya değil kayda bağlıdır — aynı kişiye birden fazla persona verilmişse (SAT-D187) de kendi hazırladığı taslağı onaylayamaz. Satış personaları, Satış Destek, Sevkiyatçı ve Finans personaları onaylamaz (seçenekler bu dört kişiyi saydı). Gerçek grant ataması insan kapısıdır | Persona kartı NS-13, fatura dalgası (API-33, API-35) |
| SAT-D205 | Kurun elle içe aktarılması | `settings.exchange-rates.import` ("şimdi çek", eksik günü elle tetikleme) **Muhasebe, Muhasebe Müdürü, Genel Müdür ve Admin**'dedir; kuru düzeltebilen kişilerle aynıdır (SAT-D169/SAT-D188) (soru 2). Otomatik çekim asıl yoldur (SAT-D169). Uygulama ayrıntısı (geri alınabilir): elle tetikleme yalnız yapılandırılmış resmî kaynaktan çeker; kuru elle girmek değildir (o düzeltmedir, SAT-D169) ve daha önce kesilmiş faturanın kuru değişmez (SAT-D172). Resmî kaynak bağlantısının herhangi bir ortamda açılması insan kapısıdır; gerçek grant ataması insan kapısıdır | Persona kartı NS-14, kur kaydı (#906) |

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`, dış kaynak ve e-Belge bağlantısının açılması) değiştirmez. OPEN-012
  açık kalır. Persona tanımı karardır; gerçek kişilere atama insan kapısındadır.
