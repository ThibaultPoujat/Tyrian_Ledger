using System.Text.Json;
using Gw2Tp.Application.LocalData;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Gw2Tp.Web.Hosting;

internal static class LocalDataEndpoints
{
    internal const string RestoreConfirmation = "RESTORE LOCAL DATA";
    internal const string ClearConfirmation = "CLEAR PERSONAL DATA";
    internal const long MaxRestoreBackupBytes = 512L * 1024 * 1024;

    public static IEndpointRouteBuilder MapLocalDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/local-data", (ILocalDataRecoveryService recoveryService) =>
            LocalDataResponseWriter.WriteLocationAsync(recoveryService.GetLocation()));

        endpoints.MapPost("/api/local-data/backup", async (
            ILocalDataRecoveryService recoveryService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var backup = await recoveryService.CreateBackupAsync(cancellationToken).ConfigureAwait(false);
                return LocalDataResponseWriter.CreateBackupResponse(backup);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return LocalDataResponseWriter.FailedOperation("backup_failed");
            }
        });

        var restoreEndpoint = endpoints.MapPost("/api/local-data/restore", async (
            HttpRequest request,
            ILocalDataRecoveryService recoveryService,
            CancellationToken cancellationToken) =>
        {
            var requestSizeLimit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (requestSizeLimit is { IsReadOnly: false })
            {
                requestSizeLimit.MaxRequestBodySize = MaxRestoreBackupBytes;
            }

            if (!request.HasFormContentType)
            {
                return LocalDataResponseWriter.InvalidRequest("restore_file_required");
            }

            var form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            var backupFile = form.Files.GetFile("backup");
            if (!string.Equals(form["confirmation"].ToString(), RestoreConfirmation, StringComparison.Ordinal) ||
                backupFile is null || backupFile.Length == 0)
            {
                return LocalDataResponseWriter.InvalidRequest("restore_confirmation_or_file_invalid");
            }

            await using var stream = backupFile.OpenReadStream();
            var result = await recoveryService.RestoreAsync(stream, cancellationToken).ConfigureAwait(false);
            return LocalDataResponseWriter.CreateRestoreResponse(result);
        });
        restoreEndpoint.WithMetadata(
            new RequestSizeLimitAttribute(MaxRestoreBackupBytes),
            new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxRestoreBackupBytes });

        endpoints.MapPost("/api/local-data/clear-personal", async (
            LocalDataConfirmationRequest confirmation,
            ILocalDataRecoveryService recoveryService,
            CancellationToken cancellationToken) =>
        {
            if (!string.Equals(confirmation.Confirmation, ClearConfirmation, StringComparison.Ordinal))
            {
                return LocalDataResponseWriter.InvalidRequest("clear_confirmation_invalid");
            }

            try
            {
                await recoveryService.ClearPersonalDataAsync(cancellationToken).ConfigureAwait(false);
                return LocalDataResponseWriter.CreateClearResponse();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return LocalDataResponseWriter.FailedOperation("clear_failed");
            }
        });

        return endpoints;
    }
}

internal sealed record LocalDataConfirmationRequest(string? Confirmation);

internal static class LocalDataResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    internal static IResult WriteLocationAsync(LocalDataLocation location) =>
        NoStoreJson(StatusCodes.Status200OK, new
        {
            databasePath = location.DatabasePath,
            backupDirectoryPath = location.BackupDirectoryPath,
        });

    internal static IResult CreateBackupResponse(LocalDataBackup backup) =>
        NoStoreJson(StatusCodes.Status201Created, new
        {
            fileName = backup.FileName,
            createdAtUtc = backup.CreatedAtUtc,
        });

    internal static IResult CreateRestoreResponse(LocalDataRestoreResult result) => result.Outcome switch
    {
        LocalDataRestoreOutcome.Restored => NoStoreJson(StatusCodes.Status200OK, new
        {
            outcome = "restored",
            preRestoreBackupFileName = result.PreRestoreBackupFileName,
        }),
        LocalDataRestoreOutcome.InvalidBackup => NoStoreJson(StatusCodes.Status400BadRequest, new { error = "invalid_backup" }),
        LocalDataRestoreOutcome.RestoreFailed => NoStoreJson(StatusCodes.Status500InternalServerError, new { error = "restore_failed" }),
        _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Unknown local-data restore outcome."),
    };

    internal static IResult CreateClearResponse() =>
        NoStoreJson(StatusCodes.Status200OK, new { outcome = "personal_data_cleared" });

    internal static IResult InvalidRequest(string error) => NoStoreJson(StatusCodes.Status400BadRequest, new { error });

    internal static IResult FailedOperation(string error) => NoStoreJson(StatusCodes.Status500InternalServerError, new { error });

    private static IResult NoStoreJson(int statusCode, object payload) => new NoStoreJsonResult(statusCode, payload);

    private sealed class NoStoreJsonResult(int statusCode, object payload) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            httpContext.Response.Headers.CacheControl = "no-store";
            await JsonSerializer.SerializeAsync(httpContext.Response.Body, payload, SerializerOptions, httpContext.RequestAborted).ConfigureAwait(false);
        }
    }
}
