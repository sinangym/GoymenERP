namespace GoymenERP.Modules.Work.Domain;

/// <summary>
/// W1-C: the Work module's own permission names (spelled exactly as <c>ErpPermissions</c> registers them). Registered,
/// not granted: no seeded role holds any of them; giving one to a role is a human gate. Reading notes and tasks of a record
/// needs no Work permission of its own — the record owner's read decides; the personal inbox and "my tasks" are own-only.
/// </summary>
public static class WorkPermissionManifestV1
{
    public const int Version = 1;
    public const string NotesWrite = "work.notes.write";
    public const string TasksAssign = "work.tasks.assign";
    public const string TasksComplete = "work.tasks.complete";

    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(
        [NotesWrite, TasksAssign, TasksComplete], StringComparer.Ordinal);
}
