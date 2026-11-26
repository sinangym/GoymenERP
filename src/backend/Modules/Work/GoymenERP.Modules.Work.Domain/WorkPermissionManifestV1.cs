namespace GoymenERP.Modules.Work.Domain;

/// <summary>
/// W1-C: the Work module's own permission names (spelled exactly as <c>ErpPermissions</c> registers them). Registered,
/// not granted: no seeded role holds any of them; giving one to a role is a human gate. Reading notes and tasks of a record
/// needs no Work permission of its own — the record owner's read decides; the personal inbox and "my tasks" are own-only. Own assistant memory needs no Work permission to see or delete;
/// writing it needs <c>ai.panel.use</c> (the assistant capability) and the open pilot.
/// </summary>
public static class WorkPermissionManifestV1
{
    public const int Version = 4;
    public const string NotesWrite = "work.notes.write";
    public const string TasksAssign = "work.tasks.assign";
    public const string TasksComplete = "work.tasks.complete";

    /// <summary>WP-C6 (AI-D30): view and close another person's assistant memory; every view is audited.</summary>
    public const string AssistantMemoryOversee = "work.assistant-memory.oversee";

    /// <summary>WP2 (YOL-D28): "onay yükseltme" — escalated approvals and the "sahipsiz onay" view; decides nothing.</summary>
    public const string ApprovalsEscalate = "work.approvals.escalate";

    /// <summary>ROL-D27: company delegation management, without approval authority or automatic grants.</summary>
    public const string DelegationsManage = "work.delegations.manage";

    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(
        [NotesWrite, TasksAssign, TasksComplete, AssistantMemoryOversee, ApprovalsEscalate, DelegationsManage], StringComparer.Ordinal);
}
