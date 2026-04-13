namespace LogsResolver.Models;

public enum LogDiagnosticIssueKind
{
    MalformedJsonLine,
    InconsistentMirrorEvent,
    OrphanScopedEvent,
    MissingScopedMirror,
    MissingRequiredField,
    EmptySubsystemOnScopedFile,
    PersonIdMismatch,
    SuspiciousSourceDuplication,
    StructuredJsonUnavailable
}
