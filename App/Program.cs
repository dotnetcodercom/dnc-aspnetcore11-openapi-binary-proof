using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

var verify = args.Contains("--verify");
var port = DemoHost.Port(args, 5129);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
builder.Logging.ClearProviders();
builder.WebHost.UseUrls($"http://127.0.0.1:{(verify ? 0 : port)}");
builder.Services.AddControllers();
// Pin the document dialect so this proof checks string/binary consistently.
builder.Services.AddOpenApi(options => options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0);
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapOpenApi();
app.MapControllers();
app.MapGet("/files/content", () => TypedResults.File(Payload.Bytes(), Payload.MediaType, Payload.FileName))
    .Produces<FileContentHttpResult>(StatusCodes.Status200OK, Payload.MediaType);
app.MapGet("/files/stream", () => TypedResults.File(new MemoryStream(Payload.Bytes()),
        Payload.MediaType, Payload.FileName, enableRangeProcessing: true))
    .Produces<FileStreamHttpResult>(StatusCodes.Status200OK, Payload.MediaType);
// Deliberately inaccurate metadata: the actual response is still binary.
app.MapGet("/files/wrong-metadata", IResult () => Results.File(Payload.Bytes(), Payload.MediaType, Payload.FileName))
    .Produces<DownloadMetadata>(StatusCodes.Status200OK, "application/json");
var runner = new BinaryProof();
DemoHost.Map(app, runner.RunAsync, "ASP.NET Core 11 binary OpenAPI responses", new
{
    package = "Microsoft.AspNetCore.OpenApi 11.0.0-rc.1.26425.128 + Microsoft.OpenApi 3.10.0", documentVersion = "3.0.4",
    candidate = "DNC-CYCLE-20261003-C03",
    scope = "Generated OpenAPI plus real HTTP bytes. No third-party generated SDK is tested."
});
await app.StartAsync();
runner.BaseUrl = app.Urls.Single();
await DemoHost.FinishAsync(app, verify, runner.RunAsync);

static class Payload
{
    public const string MediaType = "application/octet-stream";
    public const string FileName = "dnc-proof.bin";
    public static byte[] Bytes() => [0, 1, 2, 3, 127, 128, 254, 255, 68, 78, 67, 10];
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
sealed record DownloadMetadata(string FileName, long Length);

[ApiController]
public sealed class FilesController : ControllerBase
{
    [HttpGet("/files/mvc-content")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, Payload.MediaType)]
    public IActionResult ContentFile() => File(Payload.Bytes(), Payload.MediaType, Payload.FileName);

    [HttpGet("/files/mvc-stream")]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK, Payload.MediaType)]
    public IActionResult StreamFile() => File(new MemoryStream(Payload.Bytes()), Payload.MediaType,
        Payload.FileName, enableRangeProcessing: true);
}

sealed class BinaryProof
{
    public string BaseUrl { get; set; } = "";
    public async Task<ProofReport> RunAsync()
    {
        var report = new ProofReport("DNC-OpenAPI-Binary-Proof");
        report.Observations["openApiAssembly"] = typeof(Microsoft.AspNetCore.OpenApi.OpenApiOptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(20) };
        string? rawDocument = null;
        try
        {
            rawDocument = await client.GetStringAsync("/openapi/v1.json");
            using var document = JsonDocument.Parse(rawDocument);
            var root = document.RootElement;
            report.Check("The real generated document uses pinned OpenAPI 3.0.4",
                root.GetProperty("openapi").GetString() == "3.0.4", root.GetProperty("openapi").GetString());
            report.Observations["documentSha256"] = Payload.Hash(System.Text.Encoding.UTF8.GetBytes(rawDocument));
            var results = new List<object>();
            foreach (var path in new[] { "/files/content", "/files/stream", "/files/mvc-content", "/files/mvc-stream" })
            {
                var media = root.GetProperty("paths").GetProperty(path).GetProperty("get")
                    .GetProperty("responses").GetProperty("200").GetProperty("content");
                var schema = Resolve(root, media.GetProperty(Payload.MediaType).GetProperty("schema"));
                var isBinary = schema.TryGetProperty("type", out var type) && type.GetString() == "string"
                    && schema.TryGetProperty("format", out var format) && format.GetString() == "binary";
                report.Check(path + ": response schema resolves to string/binary", isBinary,
                    JsonSerializer.Deserialize<object>(schema.GetRawText()));
                using var response = await client.GetAsync(path);
                var bytes = await response.Content.ReadAsByteArrayAsync();
                var name = response.Content.Headers.ContentDisposition?.FileNameStar
                    ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                var observation = new
                {
                    path, status = (int)response.StatusCode,
                    contentType = response.Content.Headers.ContentType?.MediaType,
                    fileName = name, length = bytes.Length, sha256 = Payload.Hash(bytes)
                };
                report.Check(path + ": HTTP status, media type, filename and exact bytes match the contract",
                    response.StatusCode == HttpStatusCode.OK
                    && response.Content.Headers.ContentType?.MediaType == Payload.MediaType
                    && name == Payload.FileName && bytes.SequenceEqual(Payload.Bytes()), observation);
                results.Add(observation);
            }
            report.Observations["downloads"] = results;
            var wrongMedia = root.GetProperty("paths").GetProperty("/files/wrong-metadata")
                .GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content");
            var wrongSchema = Resolve(root, wrongMedia.GetProperty("application/json").GetProperty("schema"));
            report.Check("Negative control advertises a JSON object instead of binary content",
                wrongSchema.TryGetProperty("type", out var wrongType) && wrongType.GetString() == "object"
                && !wrongMedia.TryGetProperty(Payload.MediaType, out _),
                JsonSerializer.Deserialize<object>(wrongMedia.GetRawText()));
            using var wrongResponse = await client.GetAsync("/files/wrong-metadata");
            report.Check("Negative control detects the mismatch with actual binary HTTP bytes",
                wrongResponse.Content.Headers.ContentType?.MediaType == Payload.MediaType
                && (await wrongResponse.Content.ReadAsByteArrayAsync()).SequenceEqual(Payload.Bytes()),
                new { advertised = "application/json", actual = wrongResponse.Content.Headers.ContentType?.MediaType });

            using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "/files/stream");
            rangeRequest.Headers.Range = new RangeHeaderValue(2, 6);
            using var range = await client.SendAsync(rangeRequest);
            var rangeBytes = await range.Content.ReadAsByteArrayAsync();
            report.Check("The streaming HTTP endpoint honors bytes 2-6 with HTTP 206",
                range.StatusCode == HttpStatusCode.PartialContent
                && rangeBytes.SequenceEqual(Payload.Bytes()[2..7])
                && range.Content.Headers.ContentRange?.From == 2
                && range.Content.Headers.ContentRange?.To == 6,
                new { status = (int)range.StatusCode, contentRange = range.Content.Headers.ContentRange?.ToString(), sha256 = Payload.Hash(rangeBytes) });
        }
        catch (Exception ex) { report.Check("Proof completed without an unexpected error", false, ex.ToString()); }
        report.Scope = "Four file-result types, real generated OpenAPI 3.0.4, HTTP binary downloads, deliberately wrong metadata and a range request. No third-party client-generator or cross-version comparison is claimed.";
        await report.SaveAsync();
        if (rawDocument is not null)
            await File.WriteAllTextAsync(Path.Combine(report.EvidenceDirectory, "openapi.json"), rawDocument);
        return report;
    }
    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        for (var i = 0; i < 10; i++)
        {
            if (!schema.TryGetProperty("$ref", out var reference)) return schema;
            var pointer = reference.GetString() ?? throw new InvalidOperationException("Empty schema reference.");
            if (!pointer.StartsWith("#/")) throw new InvalidOperationException("Only local schema references expected.");
            schema = root;
            foreach (var part in pointer[2..].Split('/'))
                schema = schema.GetProperty(part.Replace("~1", "/").Replace("~0", "~"));
        }
        throw new InvalidOperationException("Too many schema references.");
    }
}
