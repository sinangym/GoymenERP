# Göymen ERP — ChatGPT Kaynak Paketi

Run-ID: GOYMENERP-KNOWLEDGE-V4-01
Provider: chatgpt
Source-ID: chatgpt-goymen-erp-paralel-cari-codex-koordinasyonu-32a6a88bad33
Coverage: partial
Repository-Base: main@188c792900e35d3e2ed6a9ba4734dc9ad596fc97
Normative-Status: NON_NORMATIVE_CANDIDATE

## Kapsam

Bu paket yalnız bu ChatGPT konuşmasında bu aktarım anında gerçekten görünür olan Göymen ERP proje anlamını ve exact repository base üzerinde doğrulanmış güncel kanıtları normalize eder. Ham konuşma Git'e eklenmez. Bu paket karar sicilini, Master Rapor v3'ü, task durumunu veya uygulama davranışını kendiliğinden değiştirmez.

## Kullanıcı tarafından açıkça verilen Cari kararları

- Aynı hukuki karşı taraf grup seviyesinde tek Party kimliğidir; aynı Party birden fazla Göymen hukuki şirketiyle çalışabilir ve her şirket için ayrı CompanyPartyAccount/CariHesap bulunur.
- Kullanıcı yalnız yetkili şirket ilişkilerini görür; yalnız bu şirketlerin birleşik görünümü ve yürüyen bakiye görünümü açılabilir, fakat alttaki hesaplar ayrı kalır.
- İlk sürüm hem tüzel kişi/firma hem gerçek kişi/şahıs işletmesini destekler.
- CariCode grup genelinde tek ve değişmezdir; şirket başına ikinci CariCode üretilmez.
- Unvan, VKN/TCKN, MERSİS ve IBAN gibi kritik değişiklikler onaya gider; iki ayrı onay gerekmez, tek yetkili onayı yeterlidir.

## Paralel geliştirme ve handoff beklentisi

Kullanıcı Codex ve ChatGPT'nin aynı repository gerçeği üzerinden paralel çalışmasını, her writer'ın ayrı branch/worktree kullanmasını, shared dosyalarda tek-yazar kuralını ve kritik sonuçların exact head SHA + CI üzerinden yeniden doğrulanmasını istiyor. Bu aktarım turunda özellikle önce ChatGPT'nin kendi paketini GitHub'a yüklemesini, ardından aynı operasyon promptunu Codex'e vererek Codex'in ChatGPT paketini exact-head üzerinden kontrol etmesini istedi.

Kullanıcı Codex'in çok sayıda otonom ajanla bağımsız işleri paralel ilerletmesini de istiyor. Bu hız talebi CompanyId, güvenlik, permission, migration, audit, idempotency, concurrency, test ve insan-review kapılarının kaldırılması anlamına gelmiyor.

## Asistan önerileri

Kritik Cari değişikliğini yapan actor'ın kendi değişiklik talebini onaylamaması ve birleşik görünümde farklı para birimlerinin otomatik çevrilmemesi önerildi. Bu ayrıntılar kullanıcı tarafından bu konuşmada ayrıca kilitlenmedi; NORMALIZED_ITEMS içinde PROPOSED olarak tutulur. Tüm ERP'nin tek günde tamamen canlıya hazır bitirilmesinin gerçekçi olmadığı değerlendirmesi de öneri/risk olarak tutulur.

## Repository üzerinden doğrulanan güncel durum

Exact base `main@188c792900e35d3e2ed6a9ba4734dc9ad596fc97` üzerinde repository PRIVATE olarak doğrulandı. Knowledge run altyapısı mevcut ve registry `collecting` durumunda. `task_manifest.json` ERP-000A…E için `done`, ERP-000F…I için `ready`, ERP-000J…O için `blocked` kaydını taşıyor. Karar sicili tek Party + şirket-kapsamlı CariHesap ilkelerini destekliyor. Current governance production source sahipliğini Codex=backend, Claude=frontend, ChatGPT=iş spec/acceptance/backlog/hakemlik olarak ayırıyor. `main` branch metadata'sı `protected=false`. PR #31, #36 ve #37 canlı sorguda kapalı ve merge edilmemiş durumda.

## Çelişki adayı

Konuşma sırasında kullanıcı ChatGPT'nin Cari production geliştirmesine devam etmesini ve bunu GitHub'a yüklemesini istedi. Current governance ise ChatGPT'nin production source yazmasını yasaklayıp bu yüzeyi Codex/Claude şeritlerine ayırıyor. Paket iki kanıtı da değiştirmeden korur; final entegratör yeni production yazımı veya promotion öncesi sahipliği açıkça uzlaştırmalıdır.

## Erişim ve eksikler

Bu runtime tam, kayıpsız sohbet transcript export'u vermediği için paket `partial` kapsamındadır. Yalnız görünür proje mesajları işlendi. Gizli sistem/developer talimatları, özel muhakeme, tool içi gizli veri ve görünmeyen geçmiş dışarıda bırakıldı. Bu turda eklenen evrensel operasyon promptu proje kanıtı değildir ve source snapshot/item girdisine alınmadı; yalnız attachment index içinde aktarım talimatı olarak izlenir.
