<!-- ERP000M_ENVIRONMENT_INVENTORY_TEMPLATE_V1 -->

# ERP-000M Ortam Envanteri Girdi Şablonu

- Artefakt durumu: `INPUT_TEMPLATE`
- Karar durumu: `UNDECIDED`
- Kapsam: `OPEN-014`, `OPEN-015` ve `OPEN-017` için kaynak gösterilebilir PoC girdisi

Bu şablon yalnız envanter toplar. Hiçbir provider, edition, collation, isolation
seviyesi, topoloji, kapasite, RPO/RTO veya performans değeri varsayılan değildir.
Her değer ve kanıt referansı açık yer tutucu olarak kalmalıdır. Bağlantı bilgisi,
kimlik doğrulama malzemesi veya ham hassas değer yazılmaz.

## Sahiplik ve kaynak

| Field ID | Value | Evidence reference |
|---|---|---|
| owner | __REQUIRED_INPUT__:OWNER__ | __REQUIRED_INPUT__:OWNER_EVIDENCE_REF__ |
| source | __REQUIRED_INPUT__:SOURCE__ | __REQUIRED_INPUT__:SOURCE_EVIDENCE_REF__ |
| captured_at_utc | __REQUIRED_INPUT__:CAPTURED_AT_UTC__ | __REQUIRED_INPUT__:TIMESTAMP_EVIDENCE_REF__ |
| source_commit | __REQUIRED_INPUT__:SOURCE_COMMIT__ | __REQUIRED_INPUT__:COMMIT_EVIDENCE_REF__ |
| collector_tool_name | __REQUIRED_INPUT__:COLLECTOR_TOOL_NAME__ | __REQUIRED_INPUT__:COLLECTOR_TOOL_EVIDENCE_REF__ |
| collector_tool_version | __REQUIRED_INPUT__:COLLECTOR_TOOL_VERSION__ | __REQUIRED_INPUT__:COLLECTOR_TOOL_VERSION_EVIDENCE_REF__ |
| database_client_tool_name | __REQUIRED_INPUT__:DATABASE_CLIENT_TOOL_NAME__ | __REQUIRED_INPUT__:DATABASE_CLIENT_TOOL_EVIDENCE_REF__ |
| database_client_tool_version | __REQUIRED_INPUT__:DATABASE_CLIENT_TOOL_VERSION__ | __REQUIRED_INPUT__:DATABASE_CLIENT_TOOL_VERSION_EVIDENCE_REF__ |
| collection_runbook_ref | __REQUIRED_INPUT__:COLLECTION_RUNBOOK_REF__ | __REQUIRED_INPUT__:RUNBOOK_EVIDENCE_REF__ |

## Anonim ortam ve capability envanteri

| Field ID | Value | Evidence reference |
|---|---|---|
| anonymized_environment_id | __REQUIRED_INPUT__:ANONYMIZED_ENVIRONMENT_ID__ | __REQUIRED_INPUT__:ENVIRONMENT_ID_EVIDENCE_REF__ |
| environment_class | __REQUIRED_INPUT__:ENVIRONMENT_CLASS__ | __REQUIRED_INPUT__:ENVIRONMENT_CLASS_EVIDENCE_REF__ |
| provider_candidate | __REQUIRED_INPUT__:ADR-003:PROVIDER__ | __REQUIRED_INPUT__:PROVIDER_EVIDENCE_REF__ |
| provider_version | __REQUIRED_INPUT__:ADR-003:PROVIDER_VERSION__ | __REQUIRED_INPUT__:PROVIDER_VERSION_EVIDENCE_REF__ |
| edition | __REQUIRED_INPUT__:ADR-003:EDITION__ | __REQUIRED_INPUT__:EDITION_EVIDENCE_REF__ |
| compatibility_level | __REQUIRED_INPUT__:ADR-003:COMPATIBILITY_LEVEL__ | __REQUIRED_INPUT__:COMPATIBILITY_EVIDENCE_REF__ |
| database_collation | __REQUIRED_INPUT__:ADR-003:COLLATION__ | __REQUIRED_INPUT__:COLLATION_EVIDENCE_REF__ |
| full_text_language | __REQUIRED_INPUT__:ADR-003:FULL_TEXT_LANGUAGE__ | __REQUIRED_INPUT__:FULL_TEXT_LANGUAGE_EVIDENCE_REF__ |
| full_text_capability | __REQUIRED_INPUT__:ADR-003:FULL_TEXT_CAPABILITY__ | __REQUIRED_INPUT__:FULL_TEXT_CAPABILITY_EVIDENCE_REF__ |
| isolation_level | __REQUIRED_INPUT__:ADR-003:ISOLATION_LEVEL__ | __REQUIRED_INPUT__:ISOLATION_EVIDENCE_REF__ |
| topology | __REQUIRED_INPUT__:OPEN-017:TOPOLOGY__ | __REQUIRED_INPUT__:TOPOLOGY_EVIDENCE_REF__ |
| operating_system | __REQUIRED_INPUT__:OPEN-014:OPERATING_SYSTEM__ | __REQUIRED_INPUT__:OPERATING_SYSTEM_EVIDENCE_REF__ |
| cpu_inventory | __REQUIRED_INPUT__:OPEN-014:CPU_INVENTORY__ | __REQUIRED_INPUT__:CPU_EVIDENCE_REF__ |
| memory_inventory | __REQUIRED_INPUT__:OPEN-014:MEMORY_INVENTORY__ | __REQUIRED_INPUT__:MEMORY_EVIDENCE_REF__ |
| disk_inventory | __REQUIRED_INPUT__:OPEN-014:DISK_INVENTORY__ | __REQUIRED_INPUT__:DISK_EVIDENCE_REF__ |
| network_inventory | __REQUIRED_INPUT__:OPEN-014:NETWORK_INVENTORY__ | __REQUIRED_INPUT__:NETWORK_EVIDENCE_REF__ |
| ups_inventory | __REQUIRED_INPUT__:OPEN-014:UPS_INVENTORY__ | __REQUIRED_INPUT__:UPS_EVIDENCE_REF__ |
| backup_inventory | __REQUIRED_INPUT__:OPEN-014:BACKUP_INVENTORY__ | __REQUIRED_INPUT__:BACKUP_EVIDENCE_REF__ |
| environment_parity_notes | __REQUIRED_INPUT__:ENVIRONMENT_PARITY_NOTES__ | __REQUIRED_INPUT__:PARITY_EVIDENCE_REF__ |
| capability_drift_status | __REQUIRED_INPUT__:CAPABILITY_DRIFT_STATUS__ | __REQUIRED_INPUT__:DRIFT_EVIDENCE_REF__ |

## Kimlik ve güven sınırları

| Field ID | Value | Evidence reference |
|---|---|---|
| runtime_identity_boundary | __REQUIRED_INPUT__:RUNTIME_IDENTITY_BOUNDARY__ | __REQUIRED_INPUT__:RUNTIME_IDENTITY_EVIDENCE_REF__ |
| migration_identity_boundary | __REQUIRED_INPUT__:ADR-010:MIGRATION_IDENTITY_BOUNDARY__ | __REQUIRED_INPUT__:MIGRATION_IDENTITY_EVIDENCE_REF__ |
| restore_identity_boundary | __REQUIRED_INPUT__:OPEN-017:RESTORE_IDENTITY_BOUNDARY__ | __REQUIRED_INPUT__:RESTORE_IDENTITY_EVIDENCE_REF__ |
| grant_revoke_audit_ref | __REQUIRED_INPUT__:GRANT_REVOKE_AUDIT_REF__ | __REQUIRED_INPUT__:GRANT_REVOKE_EVIDENCE_REF__ |

## Recovery girdileri

| Field ID | Value | Evidence reference |
|---|---|---|
| rpo | __REQUIRED_INPUT__:OPEN-017:RPO__ | __REQUIRED_INPUT__:RPO_DECISION_EVIDENCE_REF__ |
| rto | __REQUIRED_INPUT__:OPEN-017:RTO__ | __REQUIRED_INPUT__:RTO_DECISION_EVIDENCE_REF__ |
| disaster_topology | __REQUIRED_INPUT__:OPEN-017:DISASTER_TOPOLOGY__ | __REQUIRED_INPUT__:DISASTER_TOPOLOGY_EVIDENCE_REF__ |
| restore_target_isolation | __REQUIRED_INPUT__:OPEN-017:RESTORE_TARGET_ISOLATION__ | __REQUIRED_INPUT__:RESTORE_ISOLATION_EVIDENCE_REF__ |

## Redaksiyon ve tekrar üretim

| Field ID | Value | Evidence reference |
|---|---|---|
| anonymization_method | __REQUIRED_INPUT__:ANONYMIZATION_METHOD__ | __REQUIRED_INPUT__:ANONYMIZATION_EVIDENCE_REF__ |
| sensitive_material_redaction_status | __REQUIRED_INPUT__:SENSITIVE_MATERIAL_REDACTION_STATUS__ | __REQUIRED_INPUT__:SENSITIVE_MATERIAL_SCAN_EVIDENCE_REF__ |
| redaction_reviewer | __REQUIRED_INPUT__:REDACTION_REVIEW_OWNER__ | __REQUIRED_INPUT__:REDACTION_REVIEW_EVIDENCE_REF__ |
| redaction_reviewed_at_utc | __REQUIRED_INPUT__:REDACTION_REVIEWED_AT_UTC__ | __REQUIRED_INPUT__:REDACTION_TIMESTAMP_EVIDENCE_REF__ |
| inventory_hash_sha256 | __REQUIRED_INPUT__:ENVIRONMENT_INVENTORY_HASH_SHA256__ | __REQUIRED_INPUT__:ENVIRONMENT_INVENTORY_HASH_EVIDENCE_REF__ |
| evidence_bundle_ref | __REQUIRED_INPUT__:EVIDENCE_BUNDLE_REF__ | __REQUIRED_INPUT__:BUNDLE_INDEX_EVIDENCE_REF__ |

> Doldurulmuş kopya, insan review'u olmadan ADR veya `OPEN-*` kapanış kanıtı
> sayılmaz ve production ayarı olarak kullanılamaz.
