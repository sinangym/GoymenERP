__PATCH__
*** Begin Patch
*** Update File: tests/GoymenERP.Api.IntegrationTests/CounterpartyAuditEventTenantGuardSqlServerRelationalAcceptanceTests.cs
@@
-        var exception = await Assert.ThrowsAsync<ArgumentException>(
+        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
             () => SaveCollectorEventAsync(
                 factory,
                 TestCompanyIds.CompanyA,
                 CreateDraft(Guid.Empty, correlationId)));
 
-        Assert.Contains(
-            nameof(AuditEvent.CompanyId),
-            exception.ToString(),
-            StringComparison.Ordinal);
+        Assert.Equal("Audit kaydı CompanyId olmadan yazılamaz.", exception.Message);
*** End Patch