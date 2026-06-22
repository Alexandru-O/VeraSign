namespace MasterSTI.Shared.DTOs.Dashboard;

/// <summary>
/// Aggregate dashboard KPI payload computed server-side for a given time range.
/// All counts scope to the current user's organization. Returned by
/// <c>GET /api/dashboard/stats?range=7d|30d|90d</c>.
/// </summary>
public sealed record DashboardStatsDto(
    string Range,
    int Sent,
    int WalletQesSignatures,
    double WalletKeyBindingRate,
    int Pending,
    int UrgentToday,
    int Declined,
    double RejectionRate,
    int SentDeltaCount,
    IReadOnlyList<int> WeekValues,
    IReadOnlyList<string> WeekLabels,
    int? PreviousPeriodTotal);

/// <summary>
/// One row in the "Documente recente" table on the dashboard / documents list.
/// </summary>
public sealed record DocumentListItemDto(
    Guid Id,
    string Name,
    string? RecipientPrimary,
    string Level,
    string Status,
    DateTime UpdatedAt,
    Guid? SignedDocumentId = null);

/// <summary>
/// Paged result envelope shared by listing endpoints.
/// </summary>
public sealed record PagedResultDto<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>
/// Aggregated counters for the 5-stage signing pipeline shown on the Dashboard
/// (EUDIW auth -> SD-JWT verify -> SAD consent -> CSC signHash -> PAdES embed).
/// Returned by <c>GET /api/dashboard/pipeline-stats</c>.
/// </summary>
public sealed record PipelineStatsDto(
    string Window,
    DateTime From,
    DateTime To,
    IReadOnlyList<PipelineStageDto> Stages,
    int FailedTotal);

/// <summary>
/// One stage in <see cref="PipelineStatsDto"/>. Health is "ok" | "warn" | "err".
/// </summary>
public sealed record PipelineStageDto(
    int Order,
    string Key,
    int Count,
    string Health,
    int Failed = 0);

/// <summary>
/// One row in the "Necesita atentia ta" widget. Computed server-side from
/// overdue documents, idle recipients, and wallet-revalidation candidates.
/// Returned by <c>GET /api/dashboard/upcoming</c>.
/// </summary>
public sealed record UpcomingItemDto(
    string Title,
    string Subtitle,
    string Icon,
    string Tone,
    string? Badge,
    Guid? DocumentId);

/// <summary>
/// Aggregate health probe payload for the bottom InfraHealthBar.
/// Returned by <c>GET /api/dashboard/diagnostics</c>.
/// </summary>
public sealed record DiagnosticsDto(
    DateTime ProbedAt,
    IReadOnlyList<DiagnosticNodeDto> Nodes);

/// <summary>
/// One probed component. Health is "ok" | "warn" | "err"; Detail is a
/// short, user-facing string (no secrets) — e.g. "certSIGN RO · /signHash".
/// <see cref="Sparkline"/> is the 7-day uptime trail, one entry per
/// <c>~2h</c> bucket from oldest to newest. Each entry is "ok" | "warn" |
/// "err" | "na" (no sample in that bucket). Null when no history is
/// available (fresh DB or feature disabled).
/// </summary>
public sealed record DiagnosticNodeDto(
    string Key,
    string Name,
    string Detail,
    string Health,
    long? RttMs,
    IReadOnlyList<string>? Sparkline = null);

/// <summary>
/// Current EU Wallet (EUDIW) enrollment status for the signed-in user.
/// HolderName, IssuedAt and ExpiresAt are surfaced on the Dashboard
/// WalletStatusBanner; CnfThumbprint is the SHA-256 fingerprint of the
/// last KB-JWT verification key bound to the user.
/// </summary>
public sealed record WalletStatusDto(
    bool Enrolled,
    string? HolderName,
    DateTime? IssuedAt,
    DateTime? ExpiresAt,
    string? CnfThumbprint);
