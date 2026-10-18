using System.Net;
using System.Net.Http.Json;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Tests;

namespace MonAlbumPc.Server.Tests;

public sealed class ApiTests
{
    [Fact]
    public async Task Phone_pairs_then_uploads_a_photo_in_two_parts()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var (api, pairing) = await fixture.PairAsync();
        Assert.Equal("PC-SALON", pairing.PcName);
        Assert.Equal("Marie", pairing.AlbumName);
        Assert.Equal(fixture.Core.CertificateFingerprint, pairing.CertSha256);

        var status = await api.GetFromJsonAsync<StatusResponse>("status");
        Assert.True(status!.DestinationAvailable);
        Assert.Null(status.Busy);

        var photo = TestImages.Jpeg("2024:05:12 10:30:15");
        var sha = Hashing.Sha256(photo);
        var check = await (await api.PostAsJsonAsync("check", new CheckRequest([new CheckItem(sha, photo.Length)])))
            .Content.ReadFromJsonAsync<CheckResponse>();
        Assert.Empty(check!.Have);

        var declared = await Declare(api, sha, photo.Length, "IMG_1.jpg");
        Assert.Equal("upload", declared.Status);
        Assert.Equal(0, declared.Offset);

        var half = photo.Length / 2;
        var first = await Patch(api, declared.UploadId!, 0, photo[..half]);
        Assert.Equal(half, (await first.Content.ReadFromJsonAsync<OffsetResponse>())!.Offset);

        using var head = await api.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"uploads/{declared.UploadId}"));
        Assert.Equal(half.ToString(), head.Headers.GetValues("Upload-Offset").Single());

        // Declaring again (e.g. after the app restarted) resumes the same upload.
        var resumed = await Declare(api, sha, photo.Length, "IMG_1.jpg");
        Assert.Equal(declared.UploadId, resumed.UploadId);
        Assert.Equal(half, resumed.Offset);

        var last = await Patch(api, declared.UploadId!, half, photo[half..]);
        var done = (await last.Content.ReadFromJsonAsync<OffsetResponse>())!;
        Assert.Equal("saved", done.Status);
        Assert.True(File.Exists(Path.Combine(fixture.Album.DestinationRoot, "2024", "2024-05", "IMG_1.jpg")));
        Assert.Equal(1, Assert.Single(fixture.Core.Sessions.Active).Photos);

        check = await (await api.PostAsJsonAsync("check", new CheckRequest([new CheckItem(sha, photo.Length)])))
            .Content.ReadFromJsonAsync<CheckResponse>();
        Assert.Equal([sha], check!.Have);
        Assert.Equal("exists", (await Declare(api, sha, photo.Length, "IMG_1.jpg")).Status);
    }

    [Fact]
    public async Task Unknown_or_unpaired_phones_are_rejected()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var stranger = fixture.Api(Guid.NewGuid().ToString(), "nope");
        var response = await stranger.GetAsync("status");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("device_unknown", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Error.Code);

        var (api, _) = await fixture.PairAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync("pairing")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("status")).StatusCode);
    }

    [Fact]
    public async Task Api_is_not_served_over_plain_http()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var http = fixture.Download();
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync("api/v1/pair", new { })).StatusCode);
    }

    [Fact]
    public async Task Expired_or_wrong_pairing_is_reported()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var api = fixture.Api();
        var device = new PairDevice("A35", "Samsung", null, null, null);

        var none = await api.PostAsJsonAsync("pair", new PairRequest(null, "123456", device));
        Assert.Equal(HttpStatusCode.Gone, none.StatusCode);

        var session = fixture.Core.Pairing.Start(fixture.Album.Id);
        var wrong = session.Code == "000000" ? "111111" : "000000";
        var invalid = await api.PostAsJsonAsync("pair", new PairRequest(null, wrong, device));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("pairing_invalid", (await invalid.Content.ReadFromJsonAsync<ErrorBody>())!.Error.Code);

        Assert.Equal(HttpStatusCode.OK, (await api.PostAsJsonAsync("pair", new PairRequest(null, session.Code, device))).StatusCode);
    }

    [Fact]
    public async Task Wrong_offset_tells_where_to_resume()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var (api, _) = await fixture.PairAsync();
        var photo = TestImages.Jpeg();
        var declared = await Declare(api, Hashing.Sha256(photo), photo.Length, "IMG_1.jpg");

        var response = await Patch(api, declared.UploadId!, 10, photo[10..]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("0", response.Headers.GetValues("Upload-Offset").Single());
    }

    [Fact]
    public async Task Corrupted_upload_is_rejected_and_restarts_from_scratch()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var (api, _) = await fixture.PairAsync();
        var photo = TestImages.Jpeg();
        var declared = await Declare(api, Hashing.Sha256(photo), photo.Length, "IMG_1.jpg");
        var corrupted = photo.ToArray();
        corrupted[^1] ^= 0xFF;

        var response = await Patch(api, declared.UploadId!, 0, corrupted);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, (await Declare(api, Hashing.Sha256(photo), photo.Length, "IMG_1.jpg")).Offset);
    }

    [Fact]
    public async Task Missing_destination_asks_the_phone_to_wait()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var (api, _) = await fixture.PairAsync();
        Directory.Delete(fixture.Album.DestinationRoot, recursive: true);

        Assert.False((await api.GetFromJsonAsync<StatusResponse>("status"))!.DestinationAvailable);
        var photo = TestImages.Jpeg();
        var response = await api.PostAsJsonAsync("uploads", Declaration(Hashing.Sha256(photo), photo.Length, "IMG_1.jpg"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("destination_unavailable", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Error.Code);
    }

    [Fact]
    public async Task Usb_import_pauses_wifi_uploads()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var (api, pairing) = await fixture.PairAsync();
        using var usb = fixture.Core.Locks.AcquireUsb(pairing.DeviceId);

        Assert.Equal("usb", (await api.GetFromJsonAsync<StatusResponse>("status"))!.Busy);
        var photo = TestImages.Jpeg();
        var response = await api.PostAsJsonAsync("uploads", Declaration(Hashing.Sha256(photo), photo.Length, "IMG_1.jpg"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Apk_is_announced_and_served_to_paired_phones()
    {
        var apkPath = Path.Combine(Path.GetTempPath(), $"monalbumpc-{Guid.NewGuid():N}.apk");
        await File.WriteAllBytesAsync(apkPath, [1, 2, 3, 4]);
        try
        {
            await using var fixture = await ServerFixture.StartAsync(new ServerOptions
            {
                ApiPort = 0, DownloadPort = 0, Advertise = false, Apk = new ApkFile(apkPath, 3, "1.0.2"),
            });
            var (api, _) = await fixture.PairAsync();

            var apk = (await api.GetFromJsonAsync<StatusResponse>("status"))!.Apk!;
            Assert.Equal(3, apk.VersionCode);
            Assert.Equal(Hashing.Sha256([1, 2, 3, 4]), apk.Sha256);
            Assert.Equal([1, 2, 3, 4], await api.GetByteArrayAsync("apk"));
            using var http = fixture.Download();
            Assert.Equal([1, 2, 3, 4], await http.GetByteArrayAsync("monalbumpc.apk"));
        }
        finally
        {
            File.Delete(apkPath);
        }
    }

    private static UploadRequest Declaration(string sha, long size, string name) =>
        new(sha, size, name, "Appareil photo", "image/jpeg", null, DateTimeOffset.Now);

    private static async Task<UploadResponse> Declare(HttpClient api, string sha, long size, string name)
    {
        var response = await api.PostAsJsonAsync("uploads", Declaration(sha, size, name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadResponse>())!;
    }

    private static Task<HttpResponseMessage> Patch(HttpClient api, string uploadId, long offset, byte[] bytes)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"uploads/{uploadId}") { Content = new ByteArrayContent(bytes) };
        request.Headers.Add("Upload-Offset", offset.ToString());
        return api.SendAsync(request);
    }
}
