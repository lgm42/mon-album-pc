using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Pairing;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Server;

/// <summary>HTTPS API of protocol v1, see docs/protocol.md §5.</summary>
internal static class ApiEndpoints
{
    public const int Protocol = 1;
    private const int MaxCheckItems = 1000;
    private const string DeviceKey = "device";

    public static void Map(WebApplication app, CoreServices core, ServerOptions options)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MonAlbumPc.Server.Api");

        // Secrets travel in headers: the API is never served on the plain HTTP download port.
        var api = app.MapGroup("/api/v1").AddEndpointFilter(async (context, next) =>
            context.HttpContext.Request.IsHttps ? await next(context) : Results.NotFound());

        // A failure nobody expected still gets a line in the logs and an answer the phone can show.
        api.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            try
            {
                return await next(context);
            }
            catch (Exception e) when (!http.RequestAborted.IsCancellationRequested)
            {
                log.LogError(e, "Erreur inattendue sur {Method} {Path} ({Phone})", http.Request.Method, http.Request.Path,
                    (http.Items[DeviceKey] as Device)?.Name ?? "téléphone non identifié");
                return Error(StatusCodes.Status500InternalServerError, "internal_error", $"Erreur inattendue du serveur : {e.Message}");
            }
        });

        api.MapPost("/pair", (PairRequest request) => Pair(core, request));

        var device = api.MapGroup("").AddEndpointFilter(async (context, next) =>
        {
            var authenticated = core.Authenticator.Authenticate(context.HttpContext.Request.Headers.Authorization);
            if (authenticated is null)
            {
                return Error(StatusCodes.Status401Unauthorized, "device_unknown", "Ce téléphone n'est pas relié à ce PC.");
            }
            context.HttpContext.Items[DeviceKey] = authenticated;
            core.Devices.Touch(authenticated.Id, core.Clock.GetLocalNow(), null);
            return await next(context);
        });

        device.MapGet("/status", (HttpContext http) => Status(core, options, Current(http)));
        device.MapPost("/check", (HttpContext http, CheckRequest request) => Check(core, Current(http), request));
        device.MapPost("/check-names", (HttpContext http, NameCheckRequest request) => CheckNames(core, Current(http), request));
        device.MapPost("/uploads", (HttpContext http, UploadRequest request) => BeginUpload(core, log, Current(http), request));
        device.MapMethods("/uploads/{id}", ["HEAD"], (HttpContext http, string id) => UploadOffset(core, http, Current(http), id));
        device.MapPatch("/uploads/{id}", (HttpContext http, string id) => AppendUpload(core, log, http, Current(http), id));
        device.MapPost("/backup-finished", (HttpContext http, BackupFinishedRequest request) =>
        {
            var phone = Current(http);
            core.Sessions.RecordFinished(phone.Id, phone.AlbumId, ImportChannel.Wifi, complete: request is { Pending: 0, Errors: 0 });
            return Results.NoContent();
        });
        device.MapDelete("/pairing", (HttpContext http) =>
        {
            core.Devices.Revoke(Current(http).Id, core.Clock.GetLocalNow());
            return Results.NoContent();
        });
        device.MapGet("/apk", () => options.Apk is { } apk
            ? Results.File(apk.Path, "application/vnd.android.package-archive", "monalbumpc.apk")
            : Results.NotFound());
    }

    private static Device Current(HttpContext http) => (Device)http.Items[DeviceKey]!;

    private static IResult Pair(CoreServices core, PairRequest request)
    {
        var info = new DeviceInfo(request.Device.Name, request.Device.Model, request.Device.AndroidVersion,
            request.Device.AppVersion, request.Device.AppVersionCode, request.Device.HardwareId);
        try
        {
            var result = request.Token is { Length: > 0 } token
                ? core.Pairing.PairWithToken(token, info)
                : core.Pairing.PairWithCode(request.Code ?? "", info);
            return Results.Ok(new PairResponse(result.Device.Id, result.Secret, core.Identity.PcId, core.PcName,
                result.Album.Name, core.CertificateFingerprint));
        }
        catch (PairingException e) when (e.Error == PairingError.Expired)
        {
            return Error(StatusCodes.Status410Gone, "pairing_expired", e.Message);
        }
        catch (PairingException e)
        {
            return Error(StatusCodes.Status400BadRequest, "pairing_invalid", e.Message);
        }
    }

    private static IResult Status(CoreServices core, ServerOptions options, Device device)
    {
        var album = AlbumOf(core, device);
        return Results.Ok(new StatusResponse(
            Protocol,
            core.PcName,
            album.Name,
            Directory.Exists(album.DestinationRoot),
            core.Locks.IsUsbBusy(device.Id) ? "usb" : null,
            ApkInfoCache.Get(options.Apk),
            core.Clock.GetLocalNow()));
    }

    private static IResult Check(CoreServices core, Device device, CheckRequest request)
    {
        if (request.Items.Count > MaxCheckItems)
        {
            return Error(StatusCodes.Status400BadRequest, "too_many_items", $"{MaxCheckItems} éléments au maximum.");
        }
        core.Sessions.Comparing(device.Id, device.AlbumId, ImportChannel.Wifi);
        var have = core.History.ContainsMany(device.AlbumId, request.Items.Select(i => i.Sha256.ToLowerInvariant()));
        return Results.Ok(new CheckResponse([.. have]));
    }

    private static IResult CheckNames(CoreServices core, Device device, NameCheckRequest request)
    {
        if (request.Items.Count > MaxCheckItems)
        {
            return Error(StatusCodes.Status400BadRequest, "too_many_items", $"{MaxCheckItems} éléments au maximum.");
        }
        core.Sessions.Comparing(device.Id, device.AlbumId, ImportChannel.Wifi);
        var have = request.Items
            .Select((item, index) => (item, index))
            .Where(x => core.History.ContainsNameAndSize(device.AlbumId, x.item.Name, x.item.Size))
            .Select(x => x.index)
            .ToList();
        return Results.Ok(new NameCheckResponse(have));
    }

    private static IResult BeginUpload(CoreServices core, ILogger log, Device device, UploadRequest request)
    {
        var album = AlbumOf(core, device);
        var sha256 = request.Sha256.ToLowerInvariant();
        if (sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigit) || request.Size < 0)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "Empreinte ou taille invalide.");
        }
        if (core.Locks.IsUsbBusy(device.Id))
        {
            return Error(StatusCodes.Status409Conflict, "busy_usb", "Import USB en cours.");
        }
        if (core.History.Contains(album.Id, sha256)
            || (request.TakenAt is { } taken && core.History.ContainsByNameAndDate(album.Id, request.Name, taken.DateTime)))
        {
            return Results.Ok(new UploadResponse("exists"));
        }
        return Guard(album, () =>
        {
            var upload = core.Uploads.Begin(album, device.Id, new UploadDeclaration(
                sha256, request.Size, request.Name, request.SourceFolder, request.Mime, request.TakenAt, request.ModifiedAt));
            var offset = core.Uploads.Offset(album, upload);
            core.Sessions.Receiving(device.Id, album.Id, ImportChannel.Wifi, request.Name, request.Size, offset,
                request.RunPosition, request.RunTotal);
            return offset == request.Size && request.Size > 0
                ? Complete(core, log, album, device, upload)
                : Results.Ok(new UploadResponse("upload", upload.Id, offset));
        });
    }

    private static IResult UploadOffset(CoreServices core, HttpContext http, Device device, string id)
    {
        var album = AlbumOf(core, device);
        return Guard(album, () =>
        {
            var upload = core.Uploads.Find(album, id);
            if (upload is null || upload.DeviceId != device.Id)
            {
                return Results.NotFound();
            }
            http.Response.Headers["Upload-Offset"] = core.Uploads.Offset(album, upload).ToString();
            return Results.Ok();
        });
    }

    private static async Task<IResult> AppendUpload(CoreServices core, ILogger log, HttpContext http, Device device, string id)
    {
        var album = AlbumOf(core, device);
        if (core.Locks.IsUsbBusy(device.Id))
        {
            return Error(StatusCodes.Status409Conflict, "busy_usb", "Import USB en cours.");
        }
        if (!long.TryParse(http.Request.Headers["Upload-Offset"], out var offset))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "En-tête Upload-Offset manquant.");
        }
        try
        {
            var upload = core.Uploads.Find(album, id);
            if (upload is null || upload.DeviceId != device.Id)
            {
                return Results.NotFound();
            }
            var declaration = upload.Declaration;
            core.Sessions.Receiving(device.Id, album.Id, ImportChannel.Wifi, declaration.Name, declaration.Size, offset);
            // A video may come in one long request: the status follows the bytes as they are written.
            var newOffset = await core.Uploads.AppendAsync(album, upload, offset, http.Request.Body, http.RequestAborted,
                received => core.Sessions.Progress(device.Id, received));
            return newOffset == declaration.Size
                ? Complete(core, log, album, device, upload)
                : Results.Ok(new OffsetResponse(newOffset));
        }
        catch (OffsetMismatchException e)
        {
            http.Response.Headers["Upload-Offset"] = e.Expected.ToString();
            return Error(StatusCodes.Status409Conflict, "offset_mismatch", e.Message);
        }
        catch (InvalidDataException e)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", e.Message);
        }
        catch (DestinationUnavailableException e)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "destination_unavailable", e.Message);
        }
    }

    private static IResult Complete(CoreServices core, ILogger log, Album album, Device device, Upload upload)
    {
        var declaration = upload.Declaration;
        var incoming = new IncomingFile(
            core.Uploads.DataPath(album, upload),
            declaration.Sha256,
            declaration.Name,
            album.Id,
            device.Id,
            ImportChannel.Wifi,
            SourcePath: declaration.SourceFolder is null ? declaration.Name : $"{declaration.SourceFolder}/{declaration.Name}",
            PhoneTakenAt: declaration.TakenAt?.DateTime,
            ModifiedAt: declaration.ModifiedAt);
        try
        {
            var result = core.Import.Import(incoming);
            core.Uploads.Delete(album, upload);
            if (result.Outcome == ImportOutcome.Saved)
            {
                core.Sessions.RecordSaved(device.Id, album.Id, ImportChannel.Wifi, MediaTypes.IsVideo(declaration.Name));
                log.LogDebug("{File} de {Phone} rangé dans {Destination}", declaration.Name, device.Name, result.DestinationPath);
            }
            return Results.Ok(new OffsetResponse(declaration.Size, result.Outcome == ImportOutcome.Saved ? "saved" : "exists"));
        }
        catch (HashMismatchException e)
        {
            log.LogWarning("{File} de {Phone} arrivé abîmé, il sera renvoyé : {Message}", declaration.Name, device.Name, e.Message);
            core.Uploads.Delete(album, upload);
            return Error(StatusCodes.Status422UnprocessableEntity, "hash_mismatch", e.Message);
        }
        catch (DestinationUnavailableException e)
        {
            log.LogWarning("Destination introuvable, {File} de {Phone} attendra : {Message}", declaration.Name, device.Name, e.Message);
            core.Sessions.RecordError(device.Id, album.Id, ImportChannel.Wifi, "destination_unavailable", e.Message);
            return Error(StatusCodes.Status503ServiceUnavailable, "destination_unavailable", e.Message);
        }
        catch (DiskFullException e)
        {
            log.LogWarning("Disque plein, {File} de {Phone} attendra : {Message}", declaration.Name, device.Name, e.Message);
            core.Sessions.RecordError(device.Id, album.Id, ImportChannel.Wifi, "disk_full", e.Message);
            return Error(StatusCodes.Status507InsufficientStorage, "disk_full", e.Message);
        }
        catch (Exception e)
        {
            // One file the server cannot store (unexpected metadata, I/O error…) must not hold back the others. The bytes stay
            // in the upload: a retry, maybe after an update of the server, imports them without sending them again.
            log.LogError(e, "Import impossible de {File} ({Size} octets) envoyé par {Phone}", declaration.Name, declaration.Size, device.Name);
            if (!File.Exists(core.Uploads.DataPath(album, upload)))
            {
                core.Uploads.Delete(album, upload);
            }
            var message = $"Rangement impossible : {e.Message}";
            core.Sessions.RecordFileError(device.Id, album.Id, ImportChannel.Wifi, declaration.Name, message);
            // 422 rather than 500: phones up to 0.1.13 mark the file in error and go on, where a 5xx made them retry the whole
            // run, stuck on this file.
            return Error(StatusCodes.Status422UnprocessableEntity, SessionErrors.ImportFailed, message);
        }
    }

    private static IResult Guard(Album album, Func<IResult> action)
    {
        try
        {
            return action();
        }
        catch (DestinationUnavailableException e)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "destination_unavailable", e.Message);
        }
    }

    private static Album AlbumOf(CoreServices core, Device device) =>
        core.Albums.Get(device.AlbumId) ?? throw new InvalidOperationException($"Album {device.AlbumId} introuvable.");

    internal static IResult Error(int status, string code, string message) =>
        Results.Json(new ErrorBody(new ErrorDetail(code, message)), statusCode: status);

    /// <summary>The APK hash is computed once per file version.</summary>
    private static class ApkInfoCache
    {
        private static (ApkFile File, ApkInfo Info)? _cached;

        public static ApkInfo? Get(ApkFile? apk)
        {
            if (apk is null || !File.Exists(apk.Path))
            {
                return null;
            }
            if (_cached is { } cached && cached.File == apk)
            {
                return cached.Info;
            }
            using var stream = File.OpenRead(apk.Path);
            var info = new ApkInfo(apk.VersionCode, apk.VersionName, Convert.ToHexStringLower(SHA256.HashData(stream)), stream.Length);
            _cached = (apk, info);
            return info;
        }
    }
}
