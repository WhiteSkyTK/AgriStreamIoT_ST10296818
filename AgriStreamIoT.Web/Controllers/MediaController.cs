using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Shared.Entities;
using AgriStreamIoT.Shared.Imaging;
using AgriStreamIoT.Shared.Messages;
using AgriStreamIoT.Shared.Telemetry;
using AgriStreamIoT.Web.Helpers;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriStreamIoT.Web.Controllers;

/// <summary>
/// Drone imagery and farm media in Azure Blob Storage. Uploading to the drone-images container
/// fires the ProcessDroneImageUpload blob trigger, whose audit entries are shown next to each image.
/// </summary>
[Authorize]
public sealed class MediaController : Controller
{
    private const long MaxUploadBytes = 512L * 1024 * 1024;
    private const long MaxFileBytes = 200L * 1024 * 1024;

    private readonly IFarmMediaService _media;
    private readonly IFieldRepository _fields;
    private readonly ImageAuditStore _audits;
    private readonly ILogger<MediaController> _logger;

    public MediaController(IFarmMediaService media, IFieldRepository fields, ImageAuditStore audits, ILogger<MediaController> logger)
    {
        _media = media;
        _fields = fields;
        _audits = audits;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string? category, string? field, CancellationToken cancellationToken)
    {
        MediaCategory? filter = category switch
        {
            "drone" => MediaCategory.DroneImagery,
            "media" => MediaCategory.FarmMedia,
            _ => null
        };
        var items = await _media.ListAsync(filter, field, 120, cancellationToken);
        var audits = await _audits.GetRecentAsync(StorageNames.DroneImagesContainer, 1000, cancellationToken);

        return View(new MediaGalleryViewModel
        {
            Items = items.ToList(),
            Audits = audits.GroupBy(a => a.BlobName).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
            Fields = (await _fields.GetAllAsync(cancellationToken)).ToList(),
            Category = category,
            Field = field,
            IsAdmin = User.IsInRole(UserRoles.Admin)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Upload(string? field, CancellationToken cancellationToken) =>
        View(new MediaUploadViewModel
        {
            FieldId = field ?? string.Empty,
            Fields = (await _fields.GetAllAsync(cancellationToken)).ToList()
        });

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Upload(MediaCategory category, string fieldId, List<IFormFile> files, CancellationToken cancellationToken)
    {
        var fields = await _fields.GetAllAsync(cancellationToken);
        var wantsJson = Request.Headers.Accept.Any(a => a?.Contains("application/json") == true);

        string? error = null;
        if (fields.All(f => f.FieldId != fieldId)) error = "Choose the field the media belongs to.";
        else if (files.Count == 0) error = "Choose at least one file.";
        else if (files.Any(f => f.Length > MaxFileBytes)) error = $"Each file must be under {Display.Bytes(MaxFileBytes)}.";

        if (error is not null)
        {
            if (wantsJson) return BadRequest(new { ok = false, error });
            TempData["Error"] = error;
            return RedirectToAction(nameof(Upload), new { field = fieldId });
        }

        var uploaded = new List<object>();
        var rejected = new List<string>();
        foreach (var file in files)
        {
            // Drone imagery must really be an image: check the header bytes, not just the extension.
            if (category == MediaCategory.DroneImagery)
            {
                var header = new byte[64];
                int read;
                await using (var probe = file.OpenReadStream())
                    read = await probe.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
                if (!ImageInspector.Inspect(header.AsSpan(0, read)).IsImage)
                {
                    rejected.Add($"{file.FileName} (not a JPEG, PNG, TIFF, WebP, GIF or BMP image)");
                    continue;
                }
            }

            await using var stream = file.OpenReadStream();
            var result = await _media.UploadAsync(stream, file.FileName, file.ContentType, category, fieldId, User.Email(), cancellationToken);
            uploaded.Add(new { result.BlobName, result.FileName, result.SizeBytes, result.Container });
            _logger.LogInformation("Uploaded {File} ({Size}) to {Container}/{Blob} for {Field}.",
                file.FileName, Display.Bytes(result.SizeBytes), result.Container, result.BlobName, fieldId);
        }

        var message = category == MediaCategory.DroneImagery
            ? $"{uploaded.Count} drone image(s) uploaded. The ProcessDroneImageUpload function audits each one within seconds."
            : $"{uploaded.Count} file(s) uploaded to farm media.";
        if (rejected.Count > 0) message += $" Skipped: {string.Join(", ", rejected)}.";

        if (wantsJson) return Json(new { ok = uploaded.Count > 0, message, uploaded, rejected }, FarmJson.Options);

        TempData[uploaded.Count > 0 ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index), new { category = category == MediaCategory.DroneImagery ? "drone" : "media" });
    }

    /// <summary>Latest audit entries, polled by the gallery so statuses update live.</summary>
    [HttpGet]
    public async Task<IActionResult> Audits(CancellationToken cancellationToken)
    {
        var audits = await _audits.GetRecentAsync(StorageNames.DroneImagesContainer, 200, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Json(audits.Select(a => new
        {
            a.BlobName,
            a.Status,
            a.StatusDetail,
            a.Format,
            a.WidthPx,
            a.HeightPx,
            a.Megapixels,
            a.ProcessingMs,
            a.PipelineLatencySeconds,
            processedAt = Display.Time(a.ProcessedAtUtc, "HH:mm:ss"),
            a.ProcessedOn
        }), FarmJson.Options);
    }

    /// <summary>Streams a blob through the app when a SAS link cannot be generated.</summary>
    [HttpGet]
    public async Task<IActionResult> Raw(string container, string name, CancellationToken cancellationToken)
    {
        var blob = await _media.OpenReadAsync(container, name, cancellationToken);
        if (blob is null) return NotFound();
        return File(blob.Value.Content, blob.Value.ContentType, enableRangeProcessing: false);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Delete(string container, string name, CancellationToken cancellationToken)
    {
        var deleted = await _media.DeleteAsync(container, name, cancellationToken);
        if (deleted && container == StorageNames.DroneImagesContainer)
        {
            try { await _audits.DeleteAsync(container, name, cancellationToken); }
            catch (Azure.RequestFailedException) { /* audit row may not exist yet */ }
        }
        TempData[deleted ? "Success" : "Error"] = deleted ? "File deleted." : "File not found.";
        return RedirectToAction(nameof(Index));
    }
}
