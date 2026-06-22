using System.Threading;
using MasterSTI.Shared.DTOs.Signing;
using MasterSTI.Wallet.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;

namespace MasterSTI.Wallet.Pages;

/// <summary>
/// Signing status. Polls <c>GET /api/signing/{id}/status</c> every 1 s and maps
/// the real <c>SigningRequest.Status</c> machine onto the 5-row UI pipeline.
/// "Detaliat" mode swaps in the technical-detail block fed by
/// <c>GET /api/signing/{id}/technical-detail</c>.
///
/// Visual cadence: each row is held Active for ≥ 900 ms (matches prototype
/// MSigning) even when server transitions through Pending → Embedded faster
/// than that. Server is still the authority: pacing only advances once
/// <see cref="_serverHighestDone"/> confirms the row complete. On Failed
/// the pacing loop is preempted and the failing row is painted red.
///
/// New (v2) visual layer: top-of-page document card + central progress ring
/// driven by <see cref="_pacedPhase"/>/<see cref="_rows"/>.Count with a 100 ms
/// tick for the elapsed counter and a smoothed interpolated ring value so the
/// progress arc grows visibly instead of jumping in 20 % increments.
/// </summary>
[QueryProperty(nameof(SigningRequestId), "SigningRequestId")]
public sealed partial class StatusPage : ContentPage
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private const int MinStepHoldMs = 900;
    private const int LegacyMockStepMs = 900;
    private const double DocProgressBarMaxWidth = 24;

    private readonly IWalletApiClient _api;
    private readonly IPendingSignContext _pendingSign;
    private readonly IRenderCommitmentCarrier _renderCommitments;

    private bool _detailed;
    private CancellationTokenSource? _cts;
    private List<StepRow> _rows = new();
    private TechnicalDetailDto? _technicalDetail;
    private bool _navigatedToDone;
    private bool _pipelineStarted;

    // Highest stage index (0..4) the server has confirmed complete. -1 = none.
    private int _serverHighestDone = -1;
    private bool _serverFailed;
    private int _serverFailedStage;

    // Paced UI phase (0..total). Incremented when a row visually settles Done;
    // drives the central progress ring + doc-card fill bar.
    private int _pacedPhase;
    private double _animatedProgress;
    private long _startTickMs;
    private IDispatcherTimer? _tickTimer;
    private readonly RingDrawable _ringDrawable = new();

    /// <summary>
    /// Empty/zero means "legacy mock flow" — the page falls back to the original
    /// 900 ms-per-step animation. A real Guid switches the page into polling mode.
    /// </summary>
    public string SigningRequestId { get; set; } = string.Empty;

    public StatusPage(
        IWalletApiClient api,
        IPendingSignContext pendingSign,
        IRenderCommitmentCarrier renderCommitments)
    {
        InitializeComponent();
        _api = api;
        _pendingSign = pendingSign;
        _renderCommitments = renderCommitments;
        RingView.Drawable = _ringDrawable;
        ApplyRingColors();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _navigatedToDone = false;
        _pipelineStarted = false;
        _serverHighestDone = -1;
        _serverFailed = false;
        _serverFailedStage = 0;
        _pacedPhase = 0;
        _animatedProgress = 0;
        _startTickMs = Environment.TickCount64;
        PercentLabel.Text = "0";
        ElapsedLabel.Text = "0.0S";
        DocProgressFill.WidthRequest = 0;
        ApplyToggleLabel();
        RebuildSteps();
        ApplyRingColors();
        RingView.Invalidate();
        StartTickTimer();

        if (TryParseSigningRequestId(out var id))
        {
            StartPipelineTasks(id, _cts.Token);
            return;
        }

        var pending = _pendingSign.Consume();
        if (pending is not null)
        {
            _ = RunBackgroundSignAsync(pending, _cts.Token);
            return;
        }

        _ = RunMockPipelineAsync(_cts.Token);
    }

    private void StartPipelineTasks(Guid id, CancellationToken ct)
    {
        if (_pipelineStarted) return;
        _pipelineStarted = true;
        _ = LoadTechnicalDetailAsync(id, ct);
        _ = PollStatusAsync(id, ct);
        _ = RunPacedPipelineAsync(ct);
    }

    /// <summary>
    /// Drives the Prepare + Sign chain after ConsentPage handed off without a
    /// SigningRequestId. Prepare yields the id, then the existing
    /// poll/pace/technical tasks fire so the ring + step list animate while
    /// the long-running <c>/sign</c> call (CSC + PAdES embed) is still in
    /// flight. Sign failures surface through the same <see cref="ShowFailure"/>
    /// path the poll loop uses.
    /// </summary>
    private async Task RunBackgroundSignAsync(PendingSign pending, CancellationToken ct)
    {
        try
        {
            // Render Commitment (ADR-0008) — soft-fail: null means the wallet
            // either skipped the precompute (legacy / debug path) or the
            // server endpoint returned 503/422/409. Either way, plain PAdES-B-LTA
            // signing proceeds.
            var commitment = _renderCommitments.Get(pending.DocumentId);

            var prep = await _api.PrepareSigningAsync(
                pending.DocumentId, pending.RecipientId, commitment, ct);
            if (ct.IsCancellationRequested) return;
            if (prep is null)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _serverFailedStage = 1;
                    _serverFailed = true;
                    ApplyServerFailure();
                });
                return;
            }

            SigningRequestId = prep.SigningRequestId.ToString();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!ct.IsCancellationRequested)
                    StartPipelineTasks(prep.SigningRequestId, ct);
            });

            var sign = await _api.SignAsync(prep.SigningRequestId, pending.Sad, pending.Factor, ct);
            if (ct.IsCancellationRequested) return;
            if (sign.Ok) return;

            // Sign failed before poll observed Failed (e.g. transport error
            // returning to wallet). Map to a stage so the paced loop preempts.
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _serverFailedStage = sign.Error?.Kind switch
                {
                    SignErrorKind.PinRejected => 3,
                    _                         => 4,
                };
                _serverFailed = true;
                ApplyServerFailure();
            });
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StatusPage] RunBackgroundSign crashed: {ex}");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _serverFailedStage = 4;
                _serverFailed = true;
                ApplyServerFailure();
            });
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _cts?.Cancel();
        _cts = null;
        StopTickTimer();
        foreach (var r in _rows) r.AbortAnimations();
    }

    private bool TryParseSigningRequestId(out Guid id)
    {
        if (!string.IsNullOrWhiteSpace(SigningRequestId) &&
            Guid.TryParse(SigningRequestId, out var parsed) &&
            parsed != Guid.Empty)
        {
            id = parsed;
            return true;
        }

        id = Guid.Empty;
        return false;
    }

    private void ApplyToggleLabel()
    {
        // Toggle text shows the *destination* mode, matching prototype.
        ToggleDetailLabel.Text = _detailed ? "SIMPLU" : "TEHNIC";
    }

    private void OnToggleDetailTapped(object? sender, TappedEventArgs e)
    {
        _detailed = !_detailed;
        ApplyToggleLabel();
        RebuildSteps();
        RenderTechnicalDetail();
    }

    private static readonly (string label, string sub)[] StepsSimple =
    {
        ("Verificare identitate",  "EU Wallet · atestare PID"),
        ("Activare cheie",         "certSIGN · QSCD calificat"),
        ("Semnare document",       "Hash SHA-256 semnat în HSM"),
        ("Marcă temporală",        "TSA RFC 3161 · certSIGN TSA"),
        ("Finalizare PAdES-LTV",   "Embed semnătură în PDF"),
    };

    private static readonly (string label, string sub)[] StepsDetail =
    {
        ("OID4VP /verify",                 "POST · vp_token + presentation_submission"),
        ("CSC /credentials/authorize",     "PIN relay · SAD obținut"),
        ("CSC /signatures/signHash",       "RSA-PSS · SHA-256 · 2048"),
        ("RFC 3161 timestamp",             "TimeStampReq → TimeStampResp"),
        ("PAdES-B-LTA embed",              "DSS dictionary · cross-cert"),
    };

    private (string label, string sub)[] CurrentSteps => _detailed ? StepsDetail : StepsSimple;

    private static Color Res(string lightKey, string darkKey)
    {
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
        var key = theme == AppTheme.Dark ? darkKey : lightKey;
        return (Color)Application.Current!.Resources[key];
    }

    private void ApplyRingColors()
    {
        _ringDrawable.RingColor = Res("BorderLight", "BorderDark");
        _ringDrawable.AccentColor = Res("AccentLight", "AccentDark");
        _ringDrawable.DottedColor = Res("BorderSubtleLight", "BorderSubtleDark");
    }

    private void RebuildSteps()
    {
        foreach (var r in _rows) r.AbortAnimations();
        StepsHost.Children.Clear();
        _rows.Clear();

        var fontFamily = _detailed ? "GeistMono" : "Geist";

        foreach (var (label, sub) in CurrentSteps)
        {
            var row = new StepRow(label, sub, fontFamily);
            _rows.Add(row);
            StepsHost.Children.Add(row.Root);
        }
    }

    private async Task LoadTechnicalDetailAsync(Guid id, CancellationToken ct)
    {
        try
        {
            _technicalDetail = await _api.GetTechnicalDetailAsync(id, ct);
            if (!ct.IsCancellationRequested)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ApplyDocCard(_technicalDetail);
                    RenderTechnicalDetail();
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StatusPage] GetTechnicalDetail failed: {ex.Message}");
        }
    }

    private void ApplyDocCard(TechnicalDetailDto? detail)
    {
        if (detail is null)
        {
            DocNameLabel.Text = "Document";
            DocMetaLabel.Text = "—";
            return;
        }

        DocNameLabel.Text = string.IsNullOrWhiteSpace(detail.DocumentName)
            ? "Document"
            : detail.DocumentName;

        // Mirror the prototype meta line: "12 pagini · 2.4 MB · QES".
        var parts = new List<string>(3);
        if (detail.Pages > 0)
            parts.Add($"{detail.Pages} pagini");
        if (detail.SizeBytes > 0)
            parts.Add(FormatSize(detail.SizeBytes));
        var level = ExtractLevelTag(detail.Level);
        if (!string.IsNullOrWhiteSpace(level))
            parts.Add(level);
        DocMetaLabel.Text = parts.Count > 0 ? string.Join(" · ", parts) : "—";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return string.Empty;
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024):0.#} MB";
    }

    /// <summary>
    /// Doc-card meta line displays an eIDAS level tag (SES/AdES/QES) only — the
    /// full PAdES level string (<c>PAdES-B-LTA</c>) is too long for the row. Falls
    /// back to "QES" when the level can't be parsed.
    /// </summary>
    private static string ExtractLevelTag(string? level)
    {
        if (string.IsNullOrWhiteSpace(level)) return "QES";
        var upper = level.ToUpperInvariant();
        if (upper.Contains("QES")) return "QES";
        if (upper.Contains("ADES")) return "AdES";
        if (upper.Contains("SES")) return "SES";
        // PAdES-B-* → QES by default (the demo always issues QES via CSC).
        return "QES";
    }

    private void RenderTechnicalDetail()
    {
        DetailHost.Children.Clear();
        if (!_detailed || _technicalDetail is null)
        {
            DetailHost.IsVisible = false;
            return;
        }

        DetailHost.IsVisible = true;

        AddDetailRow("Hash prefix",   _technicalDetail.HashPrefix);
        AddDetailRow("Cert SHA-256",  ShortenFingerprint(_technicalDetail.CertificateFingerprint));
        AddDetailRow("TSP",           _technicalDetail.TspName);
        AddDetailRow("Algoritm",      _technicalDetail.Algorithm);
        AddDetailRow("Nivel PAdES",   _technicalDetail.Level);
    }

    private static string ShortenFingerprint(string fingerprint)
    {
        // Compact display: first 8 + ellipsis + last 8 hex pairs.
        if (string.IsNullOrEmpty(fingerprint))
            return "—";
        var pairs = fingerprint.Split(':');
        if (pairs.Length <= 8)
            return fingerprint;
        return string.Join(':', pairs[..4]) + "…" + string.Join(':', pairs[^4..]);
    }

    private void AddDetailRow(string label, string value)
    {
        var displayValue = string.IsNullOrWhiteSpace(value) ? "—" : value;
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 12,
        };
        var labelView = new Label
        {
            Text = label,
            FontSize = 11,
            FontFamily = "Geist",
            TextColor = Res("FgMutedLight", "FgMutedDark"),
            VerticalTextAlignment = TextAlignment.Center,
        };
        var valueView = new Label
        {
            Text = displayValue,
            FontSize = 11,
            FontFamily = "GeistMono",
            TextColor = Res("FgLight", "FgDark"),
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalTextAlignment = TextAlignment.Center,
        };
        Grid.SetColumn(labelView, 0);
        Grid.SetColumn(valueView, 1);
        grid.Children.Add(labelView);
        grid.Children.Add(valueView);
        DetailHost.Children.Add(grid);
    }

    /// <summary>
    /// Poll <c>/api/signing/{id}/status</c> every <see cref="PollInterval"/>.
    /// Updates <see cref="_serverHighestDone"/> (which the paced pipeline
    /// gates on) and flips <see cref="_serverFailed"/> on Failed. Does NOT
    /// paint rows itself — paced pipeline owns presentation.
    /// </summary>
    private async Task PollStatusAsync(Guid id, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var snapshot = await _api.GetSigningStatusAsync(id, ct);
                if (snapshot is not null)
                {
                    if (snapshot.Status == "Failed")
                    {
                        _serverFailedStage = snapshot.FailedAtStage ?? 5;
                        _serverFailed = true;
                        return;
                    }

                    var highest = snapshot.Status switch
                    {
                        "Pending" => -1,
                        "HashPrepared" => -1,
                        "EudiwAuthorized" => 0,
                        "CredentialAuthorized" => 1,
                        "Signed" => 2,
                        "Embedded" => 4,
                        _ => -1,
                    };
                    if (highest > _serverHighestDone)
                        _serverHighestDone = highest;

                    if (snapshot.Status == "Embedded")
                    {
                        // Stash SignedDocumentId on the snapshot via field; navigation
                        // happens inside paced pipeline once min-floor pacing finishes.
                        _pendingSignedDocId = snapshot.SignedDocumentId;
                        return;
                    }
                }

                try { await Task.Delay(PollInterval, ct); }
                catch (TaskCanceledException) { return; }
            }
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StatusPage] PollStatus crashed: {ex}");
        }
    }

    private Guid? _pendingSignedDocId;

    /// <summary>
    /// Paced pipeline: walks 5 rows at min ≥ 900 ms each. Each row's transition
    /// from Active → Done waits for server confirmation (<see cref="_serverHighestDone"/>)
    /// AND for the min-hold timer. On <see cref="_serverFailed"/> the loop preempts,
    /// paints the failed row red, and surfaces the error block. The phase counter
    /// (<see cref="_pacedPhase"/>) drives the central progress ring.
    /// </summary>
    private async Task RunPacedPipelineAsync(CancellationToken ct)
    {
        try
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (ct.IsCancellationRequested) return;
                if (_serverFailed) { ApplyServerFailure(); return; }

                _rows[i].SetState(StepState.Active);
                var started = Environment.TickCount;

                // Wait for: server confirms stage i complete, OR failure, OR cancel.
                while (!ct.IsCancellationRequested && !_serverFailed && _serverHighestDone < i)
                {
                    try { await Task.Delay(100, ct); }
                    catch (TaskCanceledException) { return; }
                }

                if (_serverFailed) { ApplyServerFailure(); return; }
                if (ct.IsCancellationRequested) return;

                // Min-floor hold: ensure user sees the active state for ≥ 900 ms.
                var elapsed = Environment.TickCount - started;
                var remaining = MinStepHoldMs - elapsed;
                if (remaining > 0)
                {
                    try { await Task.Delay(remaining, ct); }
                    catch (TaskCanceledException) { return; }
                }

                if (_serverFailed) { ApplyServerFailure(); return; }
                _rows[i].SetState(StepState.Done);
                _pacedPhase = i + 1;
            }

            // Brief settle + navigate.
            try { await Task.Delay(400, ct); }
            catch (TaskCanceledException) { return; }

            if (_navigatedToDone) return;
            _navigatedToDone = true;

            var route = _pendingSignedDocId is Guid sid && sid != Guid.Empty
                ? $"done?signedDocId={sid}"
                : "done";
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StatusPage] RunPacedPipeline crashed: {ex}");
        }
    }

    private void ApplyServerFailure()
    {
        var failedRow = FailedStageToRow(_serverFailedStage);
        for (var i = 0; i < failedRow && i < _rows.Count; i++)
            _rows[i].SetState(StepState.Done);
        if (failedRow >= 0 && failedRow < _rows.Count)
            _rows[failedRow].SetState(StepState.Error);
        for (var i = failedRow + 1; i < _rows.Count; i++)
            _rows[i].SetState(StepState.Idle);
        _pacedPhase = Math.Max(0, failedRow);
        ShowFailure(_serverFailedStage);
    }

    /// <summary>
    /// Map <c>SigningRequest.FailedAtStage</c> (1–5) to a row index in the 5-row UI.
    /// Stages 1 (EUDIW auth) and 2 (SD-JWT verify) both surface on row 0; stage 5
    /// (PAdES embed) collapses timestamp + embed onto row 4.
    /// </summary>
    private static int FailedStageToRow(int stage) => stage switch
    {
        1 => 0,
        2 => 0,
        3 => 1,
        4 => 2,
        5 => 4,
        _ => 4,
    };

    private void ShowFailure(int stage)
    {
        TitleLabel.Text = "Semnare eșuată";
        SubtitleLabel.Text = "Procesul s-a oprit. Poți reîncerca de mai jos.";
        ErrorMessageLabel.Text = stage switch
        {
            1 => "Eroare la autentificarea EUDIW (etapa 1).",
            2 => "Verificarea SD-JWT a eșuat (etapa 2).",
            3 => "Consimțământul SAD a eșuat (etapa 3).",
            4 => "Apelul CSC signHash a eșuat (etapa 4).",
            5 => "Embed-ul PAdES a eșuat (etapa 5).",
            _ => "Eroare necunoscută în pipeline-ul de semnare.",
        };
        ErrorHost.IsVisible = true;
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        // Retry semantics: bounce the user back to the consent step so they can
        // restart the flow. The wallet doesn't own server-side retry — the new
        // attempt will create / advance its own SigningRequest.
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>
    /// Legacy fallback for the demo flow that arrives via PinPage without a
    /// real <c>SigningRequestId</c>. Preserves the original visual cadence.
    /// </summary>
    private async Task RunMockPipelineAsync(CancellationToken ct)
    {
        try
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                _rows[i].SetState(StepState.Active);
                await Task.Delay(LegacyMockStepMs, ct);
                _rows[i].SetState(StepState.Done);
                _pacedPhase = i + 1;
            }

            await Task.Delay(500, ct);
            await Shell.Current.GoToAsync("done");
        }
        catch (TaskCanceledException) { }
    }

    // ─── Tick timer: drives ring + elapsed + doc-card fill ───────────────

    private void StartTickTimer()
    {
        StopTickTimer();
        _tickTimer = Dispatcher.CreateTimer();
        _tickTimer.Interval = TimeSpan.FromMilliseconds(100);
        _tickTimer.Tick += OnTick;
        _tickTimer.Start();
    }

    private void StopTickTimer()
    {
        if (_tickTimer is null) return;
        _tickTimer.Stop();
        _tickTimer.Tick -= OnTick;
        _tickTimer = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var elapsed = (Environment.TickCount64 - _startTickMs) / 1000.0;
        ElapsedLabel.Text = $"{elapsed:0.0}S";

        var total = Math.Max(1, _rows.Count);
        var target = Math.Min(1.0, (double)_pacedPhase / total);

        // Smooth interpolation toward target — gives the visible "growing arc"
        // effect even when the server jumps phases (mock CSC is near-instant).
        var delta = target - _animatedProgress;
        _animatedProgress += delta * 0.18;
        if (Math.Abs(delta) < 0.001) _animatedProgress = target;

        _ringDrawable.Progress = _animatedProgress;
        _ringDrawable.OuterRotationDegrees =
            (_ringDrawable.OuterRotationDegrees + 4.5) % 360;
        RingView.Invalidate();

        PercentLabel.Text = ((int)Math.Round(_animatedProgress * 100))
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        DocProgressFill.WidthRequest = _animatedProgress * DocProgressBarMaxWidth;
    }

    // ─── Ring drawable ──────────────────────────────────────────────────

    private sealed class RingDrawable : IDrawable
    {
        public double Progress { get; set; }
        public double OuterRotationDegrees { get; set; }
        public Color RingColor { get; set; } = Colors.LightGray;
        public Color AccentColor { get; set; } = Colors.Black;
        public Color DottedColor { get; set; } = Colors.LightGray;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var cx = dirtyRect.Center.X;
            var cy = dirtyRect.Center.Y;
            var size = Math.Min(dirtyRect.Width, dirtyRect.Height);
            var stroke = 3f;
            var ringR = (size - stroke) / 2f;

            // Outer rotating dotted accent (offset slightly outside the main ring).
            canvas.SaveState();
            canvas.Rotate((float)OuterRotationDegrees, cx, cy);
            canvas.StrokeColor = DottedColor;
            canvas.StrokeSize = 1;
            canvas.StrokeDashPattern = new float[] { 2, 6 };
            var outerR = ringR + 8;
            canvas.DrawEllipse(cx - outerR, cy - outerR, outerR * 2, outerR * 2);
            canvas.StrokeDashPattern = null;
            canvas.RestoreState();

            // Background ring (full circle).
            canvas.StrokeColor = RingColor;
            canvas.StrokeSize = stroke;
            canvas.DrawEllipse(cx - ringR, cy - ringR, ringR * 2, ringR * 2);

            // Progress arc — starts at 12 o'clock, sweeps clockwise.
            // MAUI angle convention: 0° = right, 90° = top, counterclockwise positive.
            // For clockwise sweep starting from top, use startAngle=90, endAngle=90-sweep, clockwise=true.
            if (Progress > 0)
            {
                canvas.StrokeColor = AccentColor;
                canvas.StrokeLineCap = LineCap.Round;
                var sweep = (float)(Math.Min(Progress, 1.0) * 360.0);
                canvas.DrawArc(
                    cx - ringR, cy - ringR, ringR * 2, ringR * 2,
                    startAngle: 90f,
                    endAngle: 90f - sweep,
                    clockwise: true,
                    closed: false);

                // Pulsing dot at the arc head — leading indicator.
                if (Progress < 1)
                {
                    var angleRad = (-Math.PI / 2.0) + (Progress * 2.0 * Math.PI);
                    var dotX = cx + (float)(ringR * Math.Cos(angleRad));
                    var dotY = cy + (float)(ringR * Math.Sin(angleRad));
                    canvas.FillColor = AccentColor;
                    canvas.FillCircle(dotX, dotY, 3.5f);
                }
            }
        }
    }

    // ─── Step row (compact list, mirrors v2 mock) ───────────────────────

    private enum StepState { Idle, Active, Done, Error }

    private sealed class StepRow
    {
        public View Root { get; }
        private readonly Border _container;
        private readonly Border _bullet;
        private readonly BoxView _bulletPulse;
        private readonly Border _bulletRing;
        private readonly Controls.IconView _bulletCheck;
        private readonly Label _title;
        private readonly Label _sub;
        private readonly Label _okBadge;

        private const string PulseAnim = "step-pulse";
        private const string RingAnim = "step-ring";

        public StepRow(string title, string sub, string labelFontFamily)
        {
            _bullet = new Border
            {
                BackgroundColor = Colors.Transparent,
                Stroke = Res("BorderStrongLight", "BorderStrongDark"),
                StrokeThickness = 1.5,
                WidthRequest = 16,
                HeightRequest = 16,
                Padding = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                VerticalOptions = LayoutOptions.Center,
            };

            _bulletPulse = new BoxView
            {
                Color = Res("AccentLight", "AccentDark"),
                CornerRadius = 3,
                WidthRequest = 5,
                HeightRequest = 5,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                IsVisible = false,
            };

            _bulletRing = new Border
            {
                BackgroundColor = Colors.Transparent,
                Stroke = Res("AccentLight", "AccentDark"),
                StrokeThickness = 1,
                WidthRequest = 24,
                HeightRequest = 24,
                Padding = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Opacity = 0.3,
                IsVisible = false,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };

            _bulletCheck = new Controls.IconView
            {
                Name = "check",
                Size = 9,
                TintColor = Res("AccentFgLight", "AccentFgDark"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                IsVisible = false,
            };

            _bullet.Content = new Grid
            {
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children = { _bulletPulse, _bulletCheck },
            };

            // Wrap bullet + outer ring pulse in a fixed-size cell so the ring can
            // grow without disturbing the row layout.
            var bulletStack = new Grid
            {
                WidthRequest = 24,
                HeightRequest = 24,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children = { _bulletRing, _bullet },
            };

            _title = new Label
            {
                Text = title,
                FontSize = 12,
                FontFamily = labelFontFamily,
                TextColor = Res("FgSubtleLight", "FgSubtleDark"),
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
            };

            _sub = new Label
            {
                Text = sub,
                FontSize = 10,
                FontFamily = labelFontFamily,
                TextColor = Res("FgSubtleLight", "FgSubtleDark"),
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
            };

            var textStack = new VerticalStackLayout
            {
                Spacing = 1,
                VerticalOptions = LayoutOptions.Center,
                Children = { _title, _sub },
            };

            _okBadge = new Label
            {
                Text = "OK",
                FontSize = 10,
                FontFamily = "GeistMono",
                TextColor = Res("SuccessLight", "SuccessDark"),
                VerticalTextAlignment = TextAlignment.Center,
                IsVisible = false,
            };

            var rowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
                ColumnSpacing = 10,
                VerticalOptions = LayoutOptions.Center,
            };
            Grid.SetColumn(bulletStack, 0);
            Grid.SetColumn(textStack, 1);
            Grid.SetColumn(_okBadge, 2);
            rowGrid.Children.Add(bulletStack);
            rowGrid.Children.Add(textStack);
            rowGrid.Children.Add(_okBadge);

            _container = new Border
            {
                BackgroundColor = Colors.Transparent,
                Stroke = Colors.Transparent,
                StrokeThickness = 0,
                Padding = new Thickness(10, 8),
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                Content = rowGrid,
            };

            Root = _container;
        }

        public void SetState(StepState state)
        {
            AbortAnimations();

            switch (state)
            {
                case StepState.Idle:
                    _container.BackgroundColor = Colors.Transparent;
                    _bullet.BackgroundColor = Colors.Transparent;
                    _bullet.Stroke = Res("BorderStrongLight", "BorderStrongDark");
                    _bulletPulse.IsVisible = false;
                    _bulletRing.IsVisible = false;
                    _bulletCheck.IsVisible = false;
                    _okBadge.IsVisible = false;
                    _title.TextColor = Res("FgSubtleLight", "FgSubtleDark");
                    _sub.TextColor = Res("FgSubtleLight", "FgSubtleDark");
                    break;

                case StepState.Active:
                    _container.BackgroundColor = Res("BgSunkenLight", "BgSunkenDark");
                    _bullet.BackgroundColor = Colors.Transparent;
                    _bullet.Stroke = Res("AccentLight", "AccentDark");
                    _bulletPulse.IsVisible = true;
                    _bulletPulse.Scale = 1;
                    _bulletPulse.Opacity = 1;
                    _bulletRing.IsVisible = true;
                    _bulletRing.Scale = 1;
                    _bulletRing.Opacity = 0.3;
                    _bulletCheck.IsVisible = false;
                    _okBadge.IsVisible = false;
                    _title.TextColor = Res("FgLight", "FgDark");
                    _sub.TextColor = Res("FgSubtleLight", "FgSubtleDark");
                    StartPulse();
                    StartRing();
                    break;

                case StepState.Done:
                    _container.BackgroundColor = Colors.Transparent;
                    _bullet.BackgroundColor = Res("AccentLight", "AccentDark");
                    _bullet.Stroke = Res("AccentLight", "AccentDark");
                    _bulletPulse.IsVisible = false;
                    _bulletRing.IsVisible = false;
                    _bulletCheck.IsVisible = true;
                    _okBadge.IsVisible = true;
                    _title.TextColor = Res("FgLight", "FgDark");
                    _sub.TextColor = Res("FgSubtleLight", "FgSubtleDark");
                    break;

                case StepState.Error:
                    _container.BackgroundColor = Colors.Transparent;
                    _bullet.BackgroundColor = Colors.Transparent;
                    _bullet.Stroke = Res("DangerLight", "DangerDark");
                    _bulletPulse.IsVisible = false;
                    _bulletRing.IsVisible = false;
                    _bulletCheck.IsVisible = false;
                    _okBadge.IsVisible = false;
                    _title.TextColor = Res("DangerLight", "DangerDark");
                    _sub.TextColor = Res("DangerLight", "DangerDark");
                    break;
            }
        }

        public void AbortAnimations()
        {
            _bulletPulse.AbortAnimation(PulseAnim);
            _bulletRing.AbortAnimation(RingAnim);
        }

        private void StartPulse()
        {
            // msPulse: scale 1.0 → 0.7 → 1.0 + opacity 1 → 0.6 → 1, 1.2 s loop.
            var anim = new Animation();
            anim.Add(0.0, 0.5, new Animation(v => _bulletPulse.Scale = v, 1.0, 0.7, Easing.SinInOut));
            anim.Add(0.5, 1.0, new Animation(v => _bulletPulse.Scale = v, 0.7, 1.0, Easing.SinInOut));
            anim.Add(0.0, 0.5, new Animation(v => _bulletPulse.Opacity = v, 1.0, 0.6, Easing.SinInOut));
            anim.Add(0.5, 1.0, new Animation(v => _bulletPulse.Opacity = v, 0.6, 1.0, Easing.SinInOut));
            anim.Commit(_bulletPulse, PulseAnim, length: 1200, repeat: () => _bulletPulse.IsVisible);
        }

        private void StartRing()
        {
            // msRing: scale 1.0 → 1.6 + opacity 0.3 → 0, 1.6 s ease-out, repeat.
            var anim = new Animation();
            anim.Add(0.0, 1.0, new Animation(v => _bulletRing.Scale = v, 1.0, 1.6, Easing.CubicOut));
            anim.Add(0.0, 1.0, new Animation(v => _bulletRing.Opacity = v, 0.3, 0.0, Easing.CubicOut));
            anim.Commit(_bulletRing, RingAnim, length: 1600, repeat: () =>
            {
                if (!_bulletRing.IsVisible) return false;
                // Reset to start of next cycle.
                _bulletRing.Scale = 1.0;
                _bulletRing.Opacity = 0.3;
                return true;
            });
        }
    }
}
