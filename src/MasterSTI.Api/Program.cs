using FluentValidation;
using FluentValidation.AspNetCore;
using MasterSTI.Api.Common;
using MasterSTI.Api.Common.Audit;
using MasterSTI.Api.Common.Caching;
using MasterSTI.Api.Common.Auth;
using MasterSTI.Api.Common.Csc;
using MasterSTI.Api.Common.Diagnostics;
using MasterSTI.Api.Common.Eudiw;
using MasterSTI.Api.Common.Realtime;
using MasterSTI.Api.Common.Templates;
using MasterSTI.Api.Common.Trust;
using MasterSTI.Api.Common.Wysiwys;
using MasterSTI.Api.Data;
using MasterSTI.Api.Features.Audit;
using MasterSTI.Api.Features.Auth.Login;
using MasterSTI.Api.Features.Auth.Me;
using MasterSTI.Api.Features.Credentials.Info;
using MasterSTI.Api.Features.Credentials.List;
using MasterSTI.Api.Features.Documents.Cancel;
using MasterSTI.Api.Features.Documents.Delete;
using MasterSTI.Api.Features.Documents.Detail;
using MasterSTI.Api.Features.Documents.Download;
using MasterSTI.Api.Features.Documents.Fields;
using MasterSTI.Api.Features.Documents.FromTemplate;
using MasterSTI.Api.Features.Documents.Info;
using MasterSTI.Api.Features.Documents.Recipients;
using MasterSTI.Api.Features.Documents.Remind;
using MasterSTI.Api.Features.Documents.Render;
using MasterSTI.Api.Features.Documents.RenderCommitment;
using MasterSTI.Api.Features.Documents.Send;
using MasterSTI.Api.Features.Documents.Upload;
using MasterSTI.Api.Features.Eudiw.HandleResponse;
using MasterSTI.Api.Features.Eudiw.RequestPresentation;
using MasterSTI.Api.Features.Eudiw.RequestObject;
using MasterSTI.Api.Features.Eudiw.Status;
using MasterSTI.Api.Features.SignedDocuments.Download;
using MasterSTI.Api.Features.SignedDocuments.GetInfo;
using MasterSTI.Api.Features.SignedDocuments.Validate;
using MasterSTI.Api.Features.Signing.Embed;
using MasterSTI.Api.Features.Signing.GetTechnicalDetail;
using MasterSTI.Api.Features.Signing.Prepare;
using MasterSTI.Api.Features.Signing.Sign;
using MasterSTI.Api.Features.Signing.Status;
using MasterSTI.Api.Features.Templates.Create;
using MasterSTI.Api.Features.Templates.Delete;
using MasterSTI.Api.Features.Templates.Get;
using MasterSTI.Api.Features.Templates.List;
using MasterSTI.Api.Features.Templates.Pdf;
using MasterSTI.Api.Features.Templates.ReplacePdf;
using MasterSTI.Api.Features.Templates.Update;
using MasterSTI.Api.Features.Templates.UpdateContent;
using MasterSTI.Api.Features.Dashboard.Diagnostics;
using MasterSTI.Api.Features.Dashboard.PipelineStats;
using MasterSTI.Api.Features.Dashboard.Stats;
using MasterSTI.Api.Features.Dashboard.Upcoming;
using MasterSTI.Api.Features.Documents.List;
using MasterSTI.Api.Features.Handoff.Preview;
using MasterSTI.Api.Features.Wallet.Auth;
using MasterSTI.Api.Features.Wallet.History.List;
using MasterSTI.Api.Features.Wallet.Inbox;
using MasterSTI.Api.Features.Wallet.InboxItem.Get;
using MasterSTI.Api.Features.Wallet.Status;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Serilog;
using System.Text;
using System.Threading.RateLimiting;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, cfg) =>
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .Destructure.ByTransforming<MasterSTI.Api.Features.Signing.Sign.SignDocumentCommand>(
               cmd => new { cmd.SigningRequestId, cmd.Factor })
           .Destructure.ByTransforming<MasterSTI.Api.Features.Signing.Prepare.PrepareSigningCommand>(
               cmd => new
               {
                   cmd.DocumentId,
                   cmd.RecipientId,
                   RenderRoot = cmd.RenderCommitment?.RenderRootHex,
                   RenderProfile = cmd.RenderCommitment?.RenderProfile,
               })
           .Destructure.ByTransforming<MasterSTI.Api.Features.Eudiw.HandleResponse.HandleVpResponseCommand>(
               cmd => new { cmd.State })
           .Destructure.ByTransforming<MasterSTI.Api.Features.Auth.Login.LoginCommand>(
               cmd => new { cmd.Email }));

    builder.Services.AddDbContext<AppDbContext>(opts =>
        opts.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

    builder.Services.AddMediatR(cfg =>
        cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

    builder.Services.AddMemoryCache();

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>(
            name: "db",
            tags: new[] { "ready" })
        .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
            tags: new[] { "live" });

    builder.Services.AddScoped<DocumentStorage>();
    builder.Services.AddScoped<PadesService>();
    builder.Services.Configure<MasterSTI.Api.Common.Rendering.RenderCommitmentOptions>(
        builder.Configuration.GetSection("RenderCommitment"));
    // Singleton: PdfiumLoader installs a process-wide NativeLibrary
    // resolver. Scoped would re-attempt install per request and double-log.
    builder.Services.AddSingleton<MasterSTI.Api.Common.Rendering.IRenderCommitmentService,
                                  MasterSTI.Api.Common.Rendering.RenderCommitmentService>();
    // ADR-0008 step 4 — verifier-side seam over the same in-process PDFium pin.
    // Singleton because it holds nothing beyond the IRenderCommitmentService it wraps.
    builder.Services.AddSingleton<MasterSTI.Api.Common.Rendering.IReferenceRenderer,
                                  MasterSTI.Api.Common.Rendering.PdfiumReferenceRenderer>();
    builder.Services.AddScoped<ILtvService, LtvService>();
    builder.Services.AddSingleton<TemplatePdfRenderer>();
    builder.Services.AddSingleton<TemplateStoragePaths>();
    builder.Services.AddSingleton<MasterSTI.Api.Common.Signing.CscQesSigner>();
    builder.Services.AddSingleton<MasterSTI.Api.Common.Signing.ISigningLevelDispatcher,
        MasterSTI.Api.Common.Signing.SigningLevelDispatcher>();

    builder.Services.Configure<CscApiOptions>(builder.Configuration.GetSection(CscApiOptions.Section));
    builder.Services.Configure<EudiwOptions>(builder.Configuration.GetSection(EudiwOptions.Section));
    builder.Services.Configure<RequestObjectSigningOptions>(
        builder.Configuration.GetSection(RequestObjectSigningOptions.Section));
    builder.Services.AddSingleton<RequestObjectSigner>();

    builder.Services.AddHttpClient<ICscApiClient, CscApiClient>((sp, client) =>
    {
        var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CscApiOptions>>().Value;
        client.BaseAddress = new Uri(opts.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddResilienceHandler("csc-retry", pipeline =>
    {
        pipeline.AddRetry(new Polly.Retry.RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(500),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(r => (int)r.StatusCode >= 500)
        });
    });

    builder.Services.AddHttpClient("eudiw-jwks");

    builder.Services.AddHttpClient("diagnostics", c =>
    {
        c.Timeout = TimeSpan.FromMilliseconds(1500);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MasterSTI-Diagnostics/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler();
        if (builder.Environment.IsDevelopment())
        {
            // Dev probes hit https://localhost:7111 (Mock QTSP self-signed) — accept its cert.
            // Production must use a real CA-signed cert; do NOT trust arbitrary server certs.
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        return handler;
    });

    builder.Services.AddScoped<OpenId4VpService>();
    builder.Services.AddSingleton<IIssuerKeyHolder, IssuerKeyHolder>();
    builder.Services.AddHostedService<IssuerPemLoader>();
    builder.Services.AddSingleton<SdJwtValidator>();
    builder.Services.AddSingleton<ITrustListProvider, TrustListProvider>();
    builder.Services.AddSingleton<IPageManifestService, PageManifestService>();

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
    builder.Services.AddScoped<IRecipientAccessGuard, RecipientAccessGuard>();
    builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
    builder.Services.AddSingleton<IHandoffTokenService, HandoffTokenService>();
    builder.Services.AddScoped<IAuditWriter, AuditWriter>();
    builder.Services.AddScoped<MasterSTI.Api.Common.Email.IEmailSender, MasterSTI.Api.Common.Email.MockEmailSender>();
    builder.Services.AddSingleton<IDashboardCacheInvalidator, DashboardCacheInvalidator>();
    builder.Services.AddSignalR();
    builder.Services.AddSingleton<IDashboardNotifier, DashboardNotifier>();
    builder.Services.AddHostedService<ProbeWriterService>();

    builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));

    const string MultiAuthScheme = "MultiAuth";
    var jwtSection = builder.Configuration.GetSection(JwtOptions.Section);
    var jwtKey = jwtSection.GetValue<string>("Signing:Key");
    var jwtIssuer = jwtSection.GetValue<string>("Issuer") ?? "https://localhost:7001";
    var jwtAudience = jwtSection.GetValue<string>("Audience") ?? "https://localhost:7001";

    var authBuilder = builder.Services
        .AddAuthentication(MultiAuthScheme)
        .AddPolicyScheme(MultiAuthScheme, "ApiKey or JWT", o =>
        {
            o.ForwardDefaultSelector = ctx =>
            {
                var authz = ctx.Request.Headers.Authorization.ToString();
                if (!string.IsNullOrEmpty(authz) &&
                    authz.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    return JwtBearerDefaults.AuthenticationScheme;
                }

                // SignalR WebSocket/SSE upgrades cannot carry the Authorization header,
                // so the .NET client falls back to ?access_token=…  on /hubs/* paths.
                // Forward those to JwtBearer; the OnMessageReceived hook below copies
                // the query value into ctx.Token before validation.
                if (ctx.Request.Path.StartsWithSegments("/hubs") &&
                    !string.IsNullOrEmpty(ctx.Request.Query["access_token"]))
                {
                    return JwtBearerDefaults.AuthenticationScheme;
                }
                return ApiKeyOptions.Scheme;
            };
        })
        .AddScheme<ApiKeyOptions, ApiKeyAuthenticationHandler>(ApiKeyOptions.Scheme, opts =>
        {
            var section = builder.Configuration.GetSection("ApiKey");
            opts.Required = section.GetValue("Required", false);
            opts.Value = section.GetValue<string>("Value");
        });

    if (!string.IsNullOrWhiteSpace(jwtKey) && jwtKey.Length >= 32)
    {
        var keyBytes = Encoding.UTF8.GetBytes(jwtKey);
        authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opts =>
        {
            opts.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            opts.SaveToken = false;
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,
                ValidateAudience = true,
                ValidAudience = jwtAudience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                NameClaimType = System.Security.Claims.ClaimTypes.Name,
                RoleClaimType = System.Security.Claims.ClaimTypes.Role
            };
            opts.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    // SignalR WebSocket / SSE: pull bearer token out of the query string.
                    var token = ctx.Request.Query["access_token"].ToString();
                    if (!string.IsNullOrEmpty(token) &&
                        ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        ctx.Token = token;
                    }
                    return Task.CompletedTask;
                },
                // Stale-JWT guard: a token can outlive the User row it was issued for
                // (e.g. dev DB wipe / migration reset between wallet sessions). Without
                // this check, requests authenticate as a deleted user and every
                // user-scoped query (inbox, documents, …) silently returns empty.
                OnTokenValidated = async ctx =>
                {
                    var uid = ctx.Principal?.FindFirst(JwtTokenService.ClaimUserId)?.Value
                              ?? ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrWhiteSpace(uid) || !Guid.TryParse(uid, out var userId))
                        return;

                    var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var exists = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId);
                    if (!exists)
                        ctx.Fail("User no longer exists");
                },
                // Surface the actual validation exception so we don't have to guess
                // (IDX10223 signature, IDX10230 lifetime, IDX10204 issuer, IDX10208 audience, …).
                OnAuthenticationFailed = ctx =>
                {
                    ctx.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("JwtBearer")
                        .LogWarning(ctx.Exception, "JWT auth failed: {Type} {Msg}",
                            ctx.Exception?.GetType().Name, ctx.Exception?.Message);
                    return Task.CompletedTask;
                }
            };
        });
    }
    else if (builder.Environment.IsDevelopment())
    {
        Log.Warning("JWT signing key missing or shorter than 32 chars — JWT bearer auth disabled. Set 'Jwt:Signing:Key' in appsettings.Development.json or env var Jwt__Signing__Key.");
    }

    builder.Services.AddAuthorization(o =>
    {
        // Only list MultiAuth — listing ApiKey + JwtBearer alongside lets the
        // ApiKey scheme's dev-anonymous identity mask a failed JwtBearer (e.g.
        // stale-signature Bearer token), so the wallet never sees 401 and never
        // re-logs in. MultiAuth's ForwardDefaultSelector picks the right inner
        // scheme based on the request headers.
        o.FallbackPolicy = new AuthorizationPolicyBuilder(MultiAuthScheme)
            .RequireAuthenticatedUser()
            .Build();
    });

    builder.Services.AddCors(opts =>
    {
        opts.AddPolicy("BlazorDev", policy =>
            policy.WithOrigins(
                    "https://localhost:5001", "http://localhost:5000",
                    "https://localhost:7002", "http://localhost:5002",
                    "https://localhost:7042", "http://localhost:5042",
                    "https://localhost:7165", "http://localhost:5165")
                  .WithHeaders("Content-Type", "Accept", "Authorization", ApiKeyOptions.HeaderName,
                               "x-signalr-user-agent", "x-requested-with")
                  .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                  .AllowCredentials());
    });

    builder.Services.AddOpenApi();

    builder.Services.AddAntiforgery();

    builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(opts =>
    {
        opts.MultipartBodyLengthLimit = 60 * 1024 * 1024;
    });

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("upload", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "global",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 20,
                    QueueLimit = 0
                }));
    });

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }
    else
    {
        app.UseExceptionHandler("/error");
        app.UseHsts();
    }

    app.UseSerilogRequestLogging();

    // Skip HTTPS redirect when no HTTPS listener is configured (docker compose
    // runs plain HTTP). See MasterSTI.Web/Program.cs for the same gate.
    var apiUrls = app.Configuration["ASPNETCORE_URLS"] ?? string.Empty;
    if (apiUrls.Contains("https://", StringComparison.OrdinalIgnoreCase))
        app.UseHttpsRedirection();

    if (app.Environment.IsDevelopment())
        app.UseCors("BlazorDev");

    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    // Anonymous health-check endpoints for container orchestrators + the
    // Dashboard "API self" diagnostics probe. /healthz is everything;
    // /healthz/live is the cheap liveness ping (no DB roundtrip).
    app.MapHealthChecks("/healthz").AllowAnonymous();
    app.MapHealthChecks("/healthz/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains("live")
    }).AllowAnonymous();
    app.UseAntiforgery();

    app.MapUploadDocument();
    app.MapDownloadDocument();
    app.MapRenderDocument();
    app.MapGetRenderCommitment();
    app.MapGetDocumentInfo();
    app.MapGetDocumentDetail();
    app.MapCreateDocumentFromTemplate();

    app.MapPrepareSigning();
    app.MapEmbedSignature();
    app.MapSignDocument();
    app.MapGetSigningStatus();
    app.MapGetTechnicalDetail();

    app.MapListCredentials();
    app.MapGetCredentialInfo();

    app.MapDownloadSignedDocument();
    app.MapValidateSignature();
    app.MapGetSignedDocumentInfo();

    app.MapRequestPresentation();
    app.MapHandleVpResponse();
    app.MapGetEudiwStatus();
    app.MapGetRequestObject();

    app.MapLogin();
    app.MapMe();

    app.MapListTemplates();
    app.MapGetTemplate();
    app.MapCreateTemplate();
    app.MapUpdateTemplate();
    app.MapUpdateTemplateContent();
    app.MapReplaceTemplatePdf();
    app.MapGetTemplatePdf();
    app.MapDeleteTemplate();

    app.MapDocumentFields();
    app.MapDocumentRecipients();
    app.MapSendDocument();
    app.MapRemindDocument();
    app.MapCancelDocument();
    app.MapDeleteDocument();

    app.MapWalletAuth();
    app.MapGetWalletStatus();
    app.MapListInbox();
    app.MapGetInboxItem();
    app.MapListWalletHistory();
    app.MapHandoffPreview();
    app.MapGetAudit();

    app.MapListDocuments();
    app.MapGetDashboardStats();
    app.MapGetPipelineStats();
    app.MapGetUpcoming();
    app.MapGetDiagnostics();

    app.MapHub<DashboardHub>("/hubs/dashboard");

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        // In containerized / dev scenarios the SQL Server may not be ready when the API boots.
        // Run migrations + seed under a small Polly retry so the app self-recovers without
        // requiring a separate `dotnet ef database update` step.
        var migrateAndSeedPolicy = new ResiliencePipelineBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => ex is not OperationCanceledException),
                OnRetry = args =>
                {
                    logger.LogWarning(args.Outcome.Exception,
                        "Database not ready (attempt {Attempt}/{Max}); retrying in {Delay}…",
                        args.AttemptNumber + 1, 5, args.RetryDelay);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        try
        {
            await migrateAndSeedPolicy.ExecuteAsync(async ct =>
            {
                await db.Database.MigrateAsync(ct);
                await DbInitializer.SeedAsync(db, logger, app.Environment.ContentRootPath, ct);
            });
        }
        catch (Exception ex)
        {
            // Don't crash the host — log loudly and continue. Endpoints that require DB
            // will surface the failure with a 5xx, which is preferable to a hard boot loop.
            logger.LogError(ex, "Database migrate/seed failed after retries — continuing without seed.");
        }
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
