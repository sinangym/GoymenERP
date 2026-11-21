# W8 — Manufacturing birim testi kaynak kapanışı

Taban: `1c3e7d51c97db6248c55fbb2e94e024d7bd2d618`.
Kapsam: ilk tam birim koşumundaki dokuz Manufacturing başarısız örneği; altı test dosyası.
Durum: **SOURCE_READY; derleme ve test koşumu NOT_RUN**. İlk başarısız TRX korunur.

| İlk başarısız örnek | Kaynak düzeltmesi ve dayanak |
|---|---|
| `An_approved_plan_uses_the_work_center_capacity_record_in_force` | Onay snapshot'ı artık boş olmayan sentetik Settings parametresi taşır. Kapasite yine testin gerçek iş merkezi kaydından okunur; kapasite beklentisi değişmedi. `PlanParameterSnapshotV1` değişmezi / URT-D9. |
| `Draft_and_cancelled_order_plan_revisions_share_the_physical_cap_without_a_free_quantity_read` — kabul edilen iki örnek | İlk onay ve güncel typed Settings okuması aynı geçerli profili taşır (URT-D39). FakeStore gerçek `ProductionPlan.Revise` çağrısını, serbest bırakılan plan portu gerçek yazma callback'ini çalıştırır. URT-D111 fiziksel üst sınır, sürüm ve tek command kaydı doğrulanır; iki ret örneği korunur. |
| `The_plan_input_allocation_is_keyed_by_plan_and_roll_and_its_rollback_refuses_while_one_exists` | Güncel anahtardaki `PlanVersion` onay nesli beklentiye eklendi (URT-D21/URT-D33). İlk migration Up ve veri varken Down `51191` reddi değişmedi; mevcut iptal migration'ı/`51870` testi korunur. |
| `Plans_and_orders_persist_exactly_the_five_locked_process_values` | Beş süreç değişmeden, emirlerin `Cancelled=3` durumu ve yalnız etkin emir için `[Status] <> 3` benzersizlik filtresi doğrulanır (URT-D28/URT-D33). |
| `A_list_request_outside_the_contract_is_invalid_and_the_store_is_not_asked` — eski `Cancelled` örneği | Geçerli `Cancelled` pozitif liste teorisine eklendi. Negatif örnek strict wire sözleşmesindeki küçük harfli `cancelled` oldu; mağazaya gitmeden ret beklentisi korunur. |
| `Output_correction_keeps_the_inventory_refusal_code_and_replays_it_for_the_same_key` | Test, `Output_correction_keeps_a_shipped_refusal_retryable_then_replays_only_the_recorded_success` adıyla ret → aynı anahtarla yeniden değerlendirme → sevk kilidi kalkınca tek ters kayıt → saklanan başarı replay zincirini sınar. URT-D31 kilit kalkınca düzeltmeye izin verir; ADR-011 §7 yalnız ilgili terminal sonuçları saklar. Gerçek hostun Inventory reddinde rollback davranışıyla uyumludur. Retlerde emir sürümü, ters kayıtlar ve command deposu değişmez; başarı replay'inde yeni owner çağrısı/yazım yoktur. |
| `Outputs_are_grouped_by_class_in_enum_order_and_counted_net_of_their_reversals` | Terslenecek çıktıda gerçek `PlanOutputLineReference.ClassLine` bulunur (spec'siz planın geçerli satır referansı; URT-D30). Beklenen net miktarlar değişmedi. |
| `The_timeline_spans_every_stored_fact_and_names_the_missing_time_records` | Aynı gerçek çıktı satırı fixture düzeltmesi; zaman çizelgesi ve eksik zaman kaydı beklentileri değişmedi. |

Üretim, API, domain, persistence, migration veya yetki kodu değiştirilmedi. Test silinmedi, atlanmadı ya da karantinaya alınmadı. FakeStore'da ikinci iş kuralı/formül uygulaması yerine mevcut domain komutu kullanılır; gerçek SQL kabulü iddia edilmez.

Doğrulama: `git diff --check` exit 0. Derleme, Manufacturing birim koşumu, gerçek SQL, browser, CI ve bağımsız K3 incelemesi bu kaynak checkpoint'inde **NOT_RUN**. Ağır süreç başlamadı; birleştirilmiş kaynakta ilgili birim projesinin koşumu koordinatörün doğrulama sırasındadır.
