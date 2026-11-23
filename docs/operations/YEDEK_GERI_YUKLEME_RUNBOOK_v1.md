# Yedek ve Geri Yükleme Runbook'u v1 (RPO 15 dk / RTO 4 saat)

- Tarih: 2026-11-15
- Dilim: PLT-S15 (F-016)
- Hedef kaynağı: `KLN-D36` — `docs/decisions/SINAN_KARARLARI_2026-11-14_NS_CEVAPLARI.md`
  ("RPO 15 dakika, RTO 4 saat: günlük tam yedek + 15 dakikada bir işlem günlüğü yedeği;
  ayda bir geri yükleme denemesi").
- İlgili kararlar: `DEC-TEC-004` (tesis dışı şifreli yedek ve felaket senaryosu zorunlu),
  `DEC-TEC-007` (RPO/RTO hedefi), `docs/architecture/DEPLOY_BACKUP_RPO_RTO.md`
  ("Production kapısı"), `docs/features/ERP-000N_BACKUP_RESTORE_READINESS.md`.
- **Açık kalanlar:** `OPEN-017` ve `OPEN-014` bu runbook ile **kapanmaz**. KLN-D36 bir
  hedeftir. Hedefin gerçek yük ve gerçek geri yükleme ölçümüyle kilitlenmesi, saklama
  süresi, tesis dışı hedef, şifreleme yöntemi ve operasyon sahipleri açık karardır (§9).

Bu runbook yalnız kodu, betikleri ve doğrulama aracını anlatır. Repo ve CI hiçbir
zaman yedek almaz, geri yükleme yapmaz, SQL Server'a bağlanmaz ve zamanlanmış iş
etkinleştirmez.

---

## 1. İnsan kapıları

Aşağıdakilerin hepsi **somut insan kararı** ister; bu runbook bunların hiçbirini kendisi
yapmaz veya yetkilendirmez:

1. Production veritabanının kurtarma modelini `FULL` yapmak ve günlük zincirini başlatmak.
2. Yedekleme işini kurmak ve **etkinleştirmek** (SQL Server Agent, Windows Görev
   Zamanlayıcı veya cron). `70_agent_jobs_template_disabled.sql` işleri kapalı oluşturur.
3. Yedek klasörünü, saklama süresini, tesis dışı hedefi ve şifreleme yöntemini seçmek
   (§9, `OPEN-017`/`OPEN-014`, S10).
4. Production ortamında gerçek geri yükleme başlatmak (§7) ve geri yüklenen veritabanını
   kullanıcılara açmak.
5. Geri yüklemeden sonra outbox dağıtıcısını yeniden açmak (§7 adım 8).

---

## 2. Ön koşullar

- SQL Server veritabanı `FULL` kurtarma modelinde, `PAGE_VERIFY CHECKSUM` açık.
  `00_preflight_readonly.sql` bunu salt okunur kontrol eder.
- Yedek klasörü SQL Server hizmet hesabının yazabildiği, veritabanı dosyalarından ayrı bir
  diskte.
- SQL Server yerel şifrelemesi kullanılacaksa sertifika ve özel anahtarının yedeği
  **ayrı ve tesis dışında** saklanır. Sertifika kaybolursa şifreli yedek geri yüklenemez.
  Ön kontrol `pvt_key_last_backup_date` değerini gösterir. Anahtar saklama yeri S9
  (gizli kasa) kararıdır.
- Python 3 (yalnız standart kütüphane) ve `sqlcmd` operatör makinesinde var.
- Belge Kasası klasörü (`Documents:ObjectStore:RootPath`) için ayrı bir kopyalama yolu
  var (§5.2).

---

## 3. Dosyalar

| Dosya | Ne yapar | Yazar mı? |
|---|---|---|
| `infra/backup/backup-restore-policy.v1.json` | KLN-D36 hedefi: RPO 15, RTO 240 dakika, günlük tam, 15 dakikalık günlük, aylık deneme | — |
| `infra/backup/sql/00_preflight_readonly.sql` | Kurtarma modeli, PAGE_VERIFY, sertifikalar ve son yedekler | Hayır |
| `infra/backup/sql/10_backup.sql` | `FULL` veya `LOG` yedeği (`CHECKSUM`, isteğe bağlı sıkıştırma/şifreleme) ve hemen `RESTORE VERIFYONLY` | Yedek dosyası |
| `infra/backup/sql/30_verify_backup_file.sql` | Tek dosya için `VERIFYONLY`, `HEADERONLY`, `FILELISTONLY` | Hayır |
| `infra/backup/sql/40_export_backup_history.sql` | msdb yedek geçmişini JSON olarak dışa aktarır | Hayır |
| `infra/backup/sql/50_export_document_blob_manifest.sql` | Geri yüklenen veritabanındaki belge nesnesi listesini JSON olarak dışa aktarır | Hayır |
| `infra/backup/sql/60_restore_drill_post_checks.sql` | Geri yüklenen kopyada `DBCC CHECKDB` ve migration geçmişi | Hayır |
| `infra/backup/sql/70_agent_jobs_template_disabled.sql` | İsteğe bağlı SQL Server Agent iş şablonu; işleri ve zamanlamaları **kapalı** oluşturur | msdb (kapalı iş) |
| `infra/backup/restore-drill-evidence.example.json` | Deneme kanıtı biçimi. Örnektir, doğrulamadan geçmez | — |
| `tools/verify_backup_restore.py` | Çevrimdışı doğrulayıcı ve geri yükleme planı üreticisi | Yalnız `--output` plan dosyası |

Doğrulayıcının çıkış kodları: `0` geçti, `1` bir kontrol kaldı, `2` girdi okunamadı veya
şemaya uymuyor. Ölçümlerde tolerans yoktur. Örneğin 15 dakika 1 saniyelik açık RPO'yu aşar.
Komutların hepsi depo kökünden şu biçimde çalışır:

```
python3 -I -S -B tools/verify_backup_restore.py <komut> ...
```

---

## 4. Kurulum (bir kez, insan kapısından sonra)

1. `00_preflight_readonly.sql` çalıştırılır. `59102` (kurtarma modeli FULL değil) veya
   `59103` (PAGE_VERIFY CHECKSUM değil) hatası varsa düzeltme kararı verilir.
2. İlk tam yedek alınır (`10_backup.sql`, `BackupKind="FULL"`). Günlük zinciri bu tam
   yedekle başlar.
3. Zamanlama kurulur:
   - **SQL Server Agent varsa:** `70_agent_jobs_template_disabled.sql` çalıştırılır.
     `LogBackupIntervalMinutes` politikadaki RPO'yu aşamaz. İşler kapalı oluşur;
     etkinleştirme insan kapısıdır.
   - **Agent yoksa (ör. Express):** `10_backup.sql` işletim sistemi zamanlayıcısıyla
     çalıştırılır: günde bir `FULL`, 15 dakikada bir `LOG`.
4. Başka bir yedek aracı (sanallaştırma anlık görüntüsü, üçüncü taraf ajan) aynı
   veritabanından yedek alıyorsa `COPY_ONLY` kullanmalıdır. Aksi halde günlük zinciri
   bölünür (§8).

---

## 5. Günlük işletim

### 5.1 Veritabanı yedeği ve izleme

Yedek geçmişi dışa aktarılır ve politikaya göre denetlenir:

```
sqlcmd -S <sunucu> -E -b -y 0 -h -1 -f 65001 -v DatabaseName="GoymenErp" ^
  SinceUtc="2026-11-01T00:00:00Z" -i infra/backup/sql/40_export_backup_history.sql -o backup-history.json

python3 -I -S -B tools/verify_backup_restore.py history --history backup-history.json ^
  --require-native-encryption no
```

`--require-native-encryption` açıkça `yes` veya `no` verilmelidir. Yerel şifrelemenin
zorunlu olup olmadığı açık bir site kararıdır (§9).

`history` şunları denetler:

- Kurtarma modeli `FULL`.
- İlk tam yedekten bugüne kadar her UTC gününde en az bir tam yedek. Bugün henüz
  alınmadıysa yalnız bilgi verir.
- Hasarlı (`isDamaged`), `CHECKSUM`'sız ve istenirse şifresiz yedekler. Hasarlı yedek
  zincirde kullanılmaz.
- Günlük yedeklerinin LSN zinciri: kopukluk (`LOG_CHAIN_BREAK`) ve bir tam yedeğe
  bağlanmayan günlük (`LOG_UNANCHORED`).
- Ardışık kurtarma noktaları arasındaki süre (`RPO_GAP`) ve son noktadan değerlendirme
  anına kadar geçen açık süre (`RPO_EXPOSURE`). İkisi de RPO'yu aşamaz.

Kurtarma noktası her yedeğin **başlangıç** zamanı olarak alınır. Bu yaklaşım ihtiyatlıdır:
gerçek nokta biraz daha yenidir, ama araç onu varsaymaz.

Aracın hangi sıklıkla çalışacağı ve alarm alıcıları S11 (`DEC-TEC-009`) kararıdır. Araç
kendisi zamanlanmaz. Çıkış kodu `1` alarm, `2` dışa aktarma hatası olarak ele alınır.

msdb sunucuyla birlikte kaybolabilir. Bu yüzden `backup-history.json` yedek dosyalarıyla
birlikte tesis dışına kopyalanır (§7 adım 3).

### 5.2 Belge Kasası kopyası

Belge baytları SQL Server'da değil, nesne klasöründe tutulur. Nesne anahtarı
`companies/{şirket}/sha256/{özet}` biçimindedir. Veritabanı yedeği bu baytları içermez.
Kurallar:

1. **Eklemeli kopya:** Kopyalama, kaynakta silinen bir dosyayı hedefte silmez (silmeyi
   yansıtan "mirror" kullanılmaz). Bugünkü kodda belge silme yolu yoktur. Kopyaların
   temizlenmesi saklama politikasına bağlıdır ve o politika açıktır (§9).
2. **Sıklık RPO'yu aşmaz:** İki kopya arasındaki süre politikadaki RPO'dan büyük olamaz.
   Aksi halde veritabanı 15 dakikalık noktaya dönebilir, ama belgeler daha eskide kalır.
3. **Sıra:** T anına geri yüklenen veritabanı için, başlangıcı T'den **sonra** olan ilk
   nesne kopyası kullanılır. Nesneler içerik özetiyle adlandırıldığı ve üzerine
   yazılmadığı için, daha yeni bir kopya T anındaki bütün nesneleri içerir. Fazlası
   zararsızdır.
4. `.partial` uzantılı dosyalar yarım kalmış yüklemelerdir ve geri yüklemede okunmaz.
5. Tesis dışı ve şifreli kopya kuralı belge klasörü için de geçerlidir (`DEC-TEC-004`).

---

## 6. Aylık geri yükleme denemesi (KLN-D36)

Deneme production'da **yapılmaz**. Hedef veritabanının adı kaynaktan farklı olmalıdır;
araç ve betikler bunu ayrıca denetler. Önerilen ad: `GoymenErp_Drill`.

1. **Olay anı:** Denemenin olay anı (`simulatedIncidentUtc`) seçilir ve not edilir. RTO
   saati bu anda başlar.
2. **Geçmiş:** Kaynak sunucuda `40_export_backup_history.sql` çalıştırılır ve
   `history` kontrolü yapılır (§5.1).
3. **Plan:**

   ```
   python3 -I -S -B tools/verify_backup_restore.py restore-plan --history backup-history.json ^
     --recovery-point latest --target-database GoymenErp_Drill ^
     --data-directory "E:\SqlData" --log-directory "F:\SqlLog" --output restore-drill.sql
   ```

   `--recovery-point` olarak `latest` veya `YYYY-MM-DDTHH:MM:SSZ` biçiminde bir UTC anı
   verilir. Araç en son uygun tam yedeği ve kesintisiz günlük zincirini seçer. `STOPAT`
   değerini msdb'nin kaydettiği sunucu saat farkıyla yerel saate çevirir. Nokta zincirle
   kapsanmıyorsa plan yazılmaz (`POINT_NOT_COVERED`). Plan:
   - hedef veritabanı zaten varsa `59171` ile durur ve üzerine yazmaz (`REPLACE` kullanılmaz);
   - var olan bir plan dosyasının üzerine yazmaz.
4. **Geri yükleme:** Plan okunur ve deneme sunucusunda `sqlcmd -b -i restore-drill.sql`
   ile çalıştırılır. Başlangıç anı `restoreStartedUtc` olarak not edilir. Planın yazdığı
   "ulaşılan nokta" `recoveredToUtc` olur.
5. **Veritabanı kontrolleri:**

   ```
   sqlcmd -S <deneme-sunucusu> -E -b -d GoymenErp_Drill -v DrillDatabase="GoymenErp_Drill" ^
     SourceDatabase="GoymenErp" -i infra/backup/sql/60_restore_drill_post_checks.sql
   ```

   `DBCC CHECKDB` hatasız bitmeli ve migration geçmişi görünmelidir.
6. **Belge nesneleri:** §5.2 adım 3'e uyan nesne kopyası deneme klasörüne açılır.
   Manifesto geri yüklenen veritabanından alınır ve karşılaştırılır:

   ```
   sqlcmd -S <deneme-sunucusu> -E -b -y 0 -h -1 -f 65001 -d GoymenErp_Drill ^
     -v DatabaseName="GoymenErp_Drill" -i infra/backup/sql/50_export_document_blob_manifest.sql -o blob-manifest.json

   python3 -I -S -B tools/verify_backup_restore.py objects --manifest blob-manifest.json --root <deneme-nesne-klasörü>
   ```

   Eksik, boyutu veya SHA-256 özeti tutmayan ya da sembolik bağlantı olan nesne denemeyi
   kaldırır. Manifestoda olmayan fazla nesneler yalnız bilgi olarak raporlanır.
7. **Uygulama duman testi:** API deneme veritabanına ve deneme nesne klasörüne bağlanır.
   `Work:OutboxDispatcher:Enabled=false` olmalıdır, böylece deneme kopyası dış etki
   üretmez. Oturum açma, bir kaydı ve bir belgeyi açma denenir. Hizmetin doğrulandığı an
   `serviceVerifiedUtc` olarak not edilir.
8. **Kanıt:** `restore-drill-evidence.example.json` kopyalanır. `isExample` `false`
   yapılır, `drillId` benzersiz verilir ve zamanlar, kontrol sonuçları (`passed`, `failed`,
   `not-run`) ve `objects` sayıları girilir. Ardından:

   ```
   python3 -I -S -B tools/verify_backup_restore.py drill --evidence drill-2026-11.json
   python3 -I -S -B tools/verify_backup_restore.py drill-cadence --evidence drill-*.json --as-of 2026-11-30T00:00:00Z
   ```

   `drill` şunları denetler:
   - Gözlenen RPO (`simulatedIncident − recoveredTo`) en fazla 15 dakika.
   - Gözlenen RTO (`serviceVerified − simulatedIncident`) en fazla 4 saat.
   - Zaman sırası `recoveredTo ≤ simulatedIncident ≤ restoreStarted ≤ serviceVerified`.
   - Bütün kontroller `passed`.

   `drill-cadence` her UTC ayında geçen en az bir deneme arar. Geçmeyen deneme o ayı
   doldurmaz. `--since YYYY-MM` ile ilk denemeden önceki aylar da sayıma katılır.
9. Deneme veritabanı ve klasörü denemeden sonra kaldırılır. Kanıt dosyaları saklanır;
   saklama süresi açıktır (§9).

---

## 7. Gerçek geri yükleme (olay)

1. **Karar ve durdurma:** Geri yükleme kararını yetkili kişi verir. API durdurulur, yeni
   yazma alınmaz. Olay anı not edilir.
2. **Son günlük (tail-log):** Veritabanı hâlâ erişilebilirse, son işlem günlüğü yedeği
   değerlendirilir:
   `BACKUP LOG [GoymenErp] TO DISK = N'...' WITH CHECKSUM, NO_TRUNCATE, NORECOVERY`.
   Bu adım kaynak veritabanını `RESTORING` durumuna alır ve geri alınamaz; insan kararıdır.
   `10_backup.sql` bu adımı yapmaz.
3. **Geçmiş:** Sunucu ayaktaysa `40_export_backup_history.sql` çalıştırılır. msdb
   kaybolduysa iki yol vardır:
   - tesis dışındaki son `backup-history.json` kopyası kullanılır, ondan yeni dosyalar
     `30_verify_backup_file.sql` (`HEADERONLY`) ile okunur ve zincirin kalanı iki kişilik
     incelemeyle elle eklenir;
   - ya da msdb yedeği geri yüklenir.
4. **Plan:** `restore-plan` ile istenen noktaya plan yazılır (§6 adım 3).
   - Varsayılan yol yeni bir ad kullanır (ör. `GoymenErp_Restore`). Uygulamanın bağlantı
     dizesi daha sonra bu ada çevrilir.
   - Aynı adla geri yükleme yalnız `--allow-source-name` ile seçilir. Plan, var olan
     veritabanının üzerine yazmaz; hasarlı veritabanının adının değiştirilmesi veya
     çevrimdışı alınması ayrı bir insan kararıdır.
5. **Geri yükleme:** Plan iki kişi tarafından okunur, sonra çalıştırılır.
6. **Veritabanı kontrolleri:** Ad kaynaktan farklıysa `60_restore_drill_post_checks.sql`
   çalıştırılır. Aynı adla geri yüklendiyse betik `59152` ile durur; o zaman
   `DBCC CHECKDB WITH NO_INFOMSGS, ALL_ERRORMSGS` elle çalıştırılır.
7. **Belge nesneleri:** Nesne klasörü olaydan sağ çıktıysa doğrudan kullanılır; içinde
   kurtarma noktasındaki bütün nesneler vardır. Klasör kaybolduysa §5.2 adım 3'e uyan
   kopya açılır. Her iki durumda da `50_export_document_blob_manifest.sql` ve `objects`
   kontrolü yapılır (§6 adım 6).
8. **Dış etkiler:** API ilk kez `Work:OutboxDispatcher:Enabled=false` ile açılır.
   Kurtarma noktası ile olay anı arasında gönderilmiş dış etkiler geri yüklenen
   veritabanında "gönderilmemiş" görünebilir. Dağıtıcı açılırsa bunlar tekrar
   gönderilebilir. Bekleyen outbox kayıtları incelenmeden dağıtıcı açılmaz; açma kararı
   insan kapısıdır (S14).
9. **Duman testi ve açılış:** §6 adım 7'deki duman testi yapılır, sonra kullanıcılar
   açılır. Olay anı, ulaşılan nokta ve hizmetin açıldığı an olay kaydına yazılır.
   Gözlenen RPO ve RTO KLN-D36 hedefiyle karşılaştırılır.
10. **Yeni zincir:** Geri yüklemeden hemen sonra yeni bir tam yedek alınır.
    `history` bunu `LOG_CHAIN_RESTARTED` bilgisi olarak gösterir.

---

## 8. Zincir kopukluğu ve hatalar

| Bulgu | Olası neden | Yapılacak |
|---|---|---|
| `RECOVERY_MODEL_NOT_FULL` | Model `SIMPLE`'a çevrilmiş | Modeli FULL'a döndürme kararı; hemen tam yedek |
| `LOG_CHAIN_BREAK`, `LOG_UNANCHORED` | `COPY_ONLY` olmadan başka araçla günlük yedeği, model değişikliği, kayıp `.trn` | Eksik dosya msdb `mediaFamilies` alanından aranır; bulunamazsa hemen tam yedek alınır (zincir yeniden başlar) |
| `RPO_GAP` | Günlük işi durmuş veya yedek hasarlı | İş durumu ve disk alanı kontrol edilir; hemen günlük yedeği |
| `RPO_EXPOSURE` | Günlük işi şu anda çalışmıyor | Aynı |
| `FULL_BACKUP_MISSING_DAY` | Tam yedek işi atlanmış | İş geçmişi incelenir; hemen tam yedek |
| `BACKUP_DAMAGED` | Ortam veya disk hatası | Dosya `30_verify_backup_file.sql` ile okunur; hemen tam yedek |
| `BACKUP_WITHOUT_CHECKSUM` | Yedek `10_backup.sql` dışında alınmış | Yedekler `10_backup.sql` ile alınır |
| `BACKUP_NOT_ENCRYPTED` | Şifreleme değişkenleri boş | Site şifreleme kararına göre (§9) |
| `POINT_NOT_COVERED` | İstenen noktayı kapsayan kesintisiz zincir yok | Ulaşılabilen son nokta seçilir veya eksik `.trn` bulunur |
| `OBJECT_MISSING`, `OBJECT_HASH_MISMATCH` | Nesne kopyası eski veya bozuk | §5.2 adım 3'e uyan daha yeni kopya kullanılır |

Girdi hatası (çıkış kodu `2`): JSON okunamıyor, şema tutmuyor, saat dilimi bilinmiyor
(msdb `time_zone=127`) veya `--as-of` dışa aktarma anından sonra. Araç bu durumlarda tahmin
yapmaz.

---

## 9. Açık noktalar (`OPEN-017`, `OPEN-014`, S10)

Aşağıdakiler bu runbook ile seçilmemiştir. Sinan/BT kararını bekler:

- Yedek kopyalarının ve deneme kanıtlarının **saklama süresi**. Yasal alt sınırla
  çelişmemelidir (`CANLI_ORTAM_KURULUM_REHBERI_v1.md` §5).
- **Tesis dışı hedef** ve kopyalama yöntemi (`DEC-TEC-004`).
- SQL Server **yerel şifrelemesinin zorunlu olup olmadığı**, algoritma ve sertifika
  saklama yeri. Doğrulayıcı bu yüzden `--require-native-encryption` değerini açıkça ister.
- İzleme sıklığı ve alarm alıcıları (S11) ve operasyon sahipleri.
- RPO/RTO hedefinin gerçek yük ve geri yükleme ölçümüyle **kilitlenmesi** (`OPEN-017`).
