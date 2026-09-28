using System.Net.Http.Headers;
using System.Text.Json;
using Killright.Shared.Sde;

namespace Killright.Integration.Sde;

public sealed class SdeClient : ISdeClient
{
    private readonly HttpClient _http;
    private readonly SdeClientOptions _options;

    public SdeClient(HttpClient http, SdeClientOptions? options = null)
    {
        _http = http;
        _options = options ?? new SdeClientOptions();

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);

        if (!_http.DefaultRequestHeaders.AcceptEncoding.Any(x => x.Value.Equals("gzip", StringComparison.OrdinalIgnoreCase)))
            _http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
    }

    public async Task<SdeManifestResult> GetManifestAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(_options.ManifestUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new SdeManifestResult(SdeManifestOutcome.Failure, null);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            string? line;

            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var buildNumber = TryReadSdeBuildNumber(line);

                if (buildNumber is not null)
                    return new SdeManifestResult(SdeManifestOutcome.Success, buildNumber);
            }

            return new SdeManifestResult(SdeManifestOutcome.Failure, null);
        }
        catch
        {
            return new SdeManifestResult(SdeManifestOutcome.Failure, null);
        }
    }

    public async Task<SdeDatasetDownloadResult> DownloadDatasetZipAsync(
        string destinationZipPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(
                _options.DatasetZipUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new SdeDatasetDownloadResult(SdeDatasetDownloadOutcome.Failure);

            var totalBytes = response.Content.Headers.ContentLength;

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = File.Create(destinationZipPath);

            var buffer = new byte[81920];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await responseStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalBytesRead += bytesRead;

                if (progress is not null && totalBytes is > 0)
                    progress.Report((double)totalBytesRead / totalBytes.Value);
            }

            return new SdeDatasetDownloadResult(SdeDatasetDownloadOutcome.Success);
        }
        catch
        {
            return new SdeDatasetDownloadResult(SdeDatasetDownloadOutcome.Failure);
        }
    }

    private static long? TryReadSdeBuildNumber(string jsonLine)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonLine);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!root.TryGetProperty("_key", out var keyProperty) || keyProperty.ValueKind != JsonValueKind.String)
                return null;

            if (keyProperty.GetString() != "sde")
                return null;

            if (!root.TryGetProperty("buildNumber", out var buildNumberProperty))
                return null;

            return buildNumberProperty.GetInt64();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
