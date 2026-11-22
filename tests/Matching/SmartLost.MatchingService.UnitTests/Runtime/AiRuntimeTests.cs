using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Exceptions;
using SmartLost.MatchingService.Application.Services;
using SmartLost.MatchingService.Domain.ValueObjects;
using SmartLost.MatchingService.Infrastructure.Ai;
using Xunit;

namespace SmartLost.MatchingService.UnitTests.Runtime;

public sealed class AiRuntimeTests
{
    private const string Source = "10000000-0000-0000-0000-000000000001";
    private const string Photo = "10000000-f0f0-0000-0000-000000000001";

    [Fact]
    public async Task HttpModeSendsPhotoRevisionAndKeyThroughExistingEndpoints()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory();
        try
        {
            byte[] bytes = [1, 2, 3];
            await File.WriteAllBytesAsync(Path.Combine(directory.FullName, $"{Source}_{Photo}_1.png"), bytes);
            var calls = new List<string>();
            using var factory = new MatchingApiFactory(overrides: new Dictionary<string, string?>
            {
                ["AiRuntime:BaseUrl"] = "https://runtime.example",
                ["AiRuntime:ApiKey"] = Guid.NewGuid().ToString(),
                ["AiRuntime:PhotoDirectory"] = directory.FullName,
                ["MockAdapters:Enabled"] = "false"
            });
            using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddHttpClient(nameof(AiRuntimeClient)).ConfigurePrimaryHttpMessageHandler(() => new Stub(async request =>
                {
                    Assert.Equal("runtime.example", request.RequestUri!.Host);
                    Assert.Single(request.Headers.GetValues("X-Api-Key"));
                    Assert.Equal("image/png", request.Content!.Headers.ContentType!.MediaType);
                    Assert.Equal(bytes, await request.Content.ReadAsByteArrayAsync());
                    string path = request.RequestUri.AbsolutePath;
                    calls.Add(path);
                    EmbeddingVersion dino = MatchingOptions.Dinov3Version;
                    float[] vector = new float[1024];
                    vector[0] = 1;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = path == "/embeddings"
                            ? JsonContent.Create(new
                            {
                                model = dino.ModelVersion,
                                preprocessingVersion = dino.PreprocessingVersion,
                                dimensions = 1024,
                                embedding = vector
                            })
                            : new StringContent("""
                                {"objects":[{"category":"containers","object_type":"bag","colors":["red"],
                                "material":null,"shape":null,"pattern":null,"visible_text":["A"],
                                "distinctive_features":["zipper"],"tags":["bag"]}],"uncertainties":[]}
                                """)
                    };
                }))));
            using HttpClient client = host.CreateClient();
            using HttpResponseMessage seed = await client.PostAsync("/api/matching/demo/seed", null);
            seed.EnsureSuccessStatusCode();
            using HttpResponseMessage processed = await client.PostAsync($"/api/matching/demo/listings/{Source}/embedding/process", null);
            processed.EnsureSuccessStatusCode();
            using HttpResponseMessage analysis = await client.PostAsJsonAsync("/api/matching/photo-analysis",
                new
                {
                    listingId = Source,
                    photoId = Photo,
                    photoRevision = 1
                });
            analysis.EnsureSuccessStatusCode();
            PhotoAnalysis? result = await analysis.Content.ReadFromJsonAsync<PhotoAnalysis>();
            DetectedObject obj = Assert.Single(result!.Objects);
            Assert.Equal("bag", obj.ObjectType);
            Assert.Equal("A", Assert.Single(obj.VisibleText));
            Assert.Equal("zipper", Assert.Single(obj.DistinctiveFeatures));
            Assert.Collection(calls, path => Assert.Equal("/embeddings", path), path => Assert.Equal("/photo-analysis", path));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData("ftp://runtime.example", "test", "BaseUrl")]
    [InlineData("http://runtime.example", "test", "BaseUrl")]
    [InlineData("https://runtime.example/nested", "test", "BaseUrl")]
    [InlineData("https://runtime.example", "", "ApiKey")]
    public void HttpModeRejectsInvalidConfiguration(string url, string key, string setting)
    {
        using var factory = new MatchingApiFactory(overrides: new Dictionary<string, string?>
        {
            ["AiRuntime:BaseUrl"] = url,
            ["AiRuntime:ApiKey"] = key
        });
        Assert.Contains(setting, Assert.Throws<InvalidOperationException>(() => factory.CreateClient()).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(429, "busy", true)]
    [InlineData(502, "failure", false)]
    [InlineData(200, "{}", true)]
    [InlineData(200, "null", false)]
    [InlineData(200, "{\"objects\":[null],\"uncertainties\":[]}", false)]
    [InlineData(200, "{\"model\":\"dino\",\"preprocessingVersion\":\"v\",\"dimensions\":1,\"embedding\":[1]}", true)]
    public async Task InvalidUpstreamResultsBecomeInferenceFailures(int status, string data, bool embedding)
    {
        using var http = new HttpClient(new Stub(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(data)
        })))
        {
            BaseAddress = new Uri("https://runtime.example")
        };
        var runtime = new AiRuntimeClient(http);
        var photo = new PhotoContent(new byte[] { 1 }, "image/png");
        await Assert.ThrowsAsync<AiInferenceException>(() =>
            embedding ? (Task)runtime.GenerateAsync(photo, default) : runtime.AnalyzeAsync(photo, default));
    }

    [Fact]
    public async Task DirectoryReaderReturnsOnlyAvailableNonemptyRevision()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory();
        var reader = new DirectoryPhotoReader(directory.FullName);
        var photo = new PhotoReference(Guid.Parse(Photo), 1);
        var listing = Guid.Parse(Source);
        try
        {
            Assert.Null(await reader.ReadAsync(listing, photo, default));
            string path = Path.Combine(directory.FullName, $"{Source}_{Photo}_1.jpg");
            await File.WriteAllBytesAsync(path, []);
            Assert.Null(await reader.ReadAsync(listing, photo, default));
            await File.WriteAllBytesAsync(path, [9]);
            Assert.Equal("image/jpeg", (await reader.ReadAsync(listing, photo, default))!.MediaType);
        }
        finally
        {
            directory.Delete(true);
        }
        Assert.Null(await reader.ReadAsync(listing, photo, default));
    }

    private sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return send(request);
        }
    }
}
