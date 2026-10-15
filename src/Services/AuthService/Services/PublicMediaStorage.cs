using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using AuthService.Models;
using SkiaSharp;

namespace AuthService.Services;

public interface IPublicMediaStorage
{
    string PublicUrl(string key);
    Task<string> SignAsync(PublicMediaIntent intent, CancellationToken ct);
    Task<string> VerifyAsync(PublicMediaIntent intent, CancellationToken ct);
}

public sealed class PublicMediaStorage(IConfiguration configuration) : IPublicMediaStorage
{
    private AmazonS3Client Client()
    {
        var endpoint = configuration["Media:R2:Endpoint"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !uri.Host.EndsWith(".r2.cloudflarestorage.com", StringComparison.Ordinal) || uri.AbsolutePath != "/"
            || string.IsNullOrWhiteSpace(configuration["Media:R2:AccessKeyId"]) || string.IsNullOrWhiteSpace(configuration["Media:R2:SecretAccessKey"])
            || string.IsNullOrWhiteSpace(configuration["Media:R2:Bucket"])) throw new HttpRequestException("R2 media storage is unconfigured.");
        return new AmazonS3Client(new BasicAWSCredentials(configuration["Media:R2:AccessKeyId"], configuration["Media:R2:SecretAccessKey"]),
            new AmazonS3Config { ServiceURL = endpoint, ForcePathStyle = true, AuthenticationRegion = "auto" });
    }
    public string PublicUrl(string key)
    {
        var origin = configuration["Media:R2:PublicOrigin"];
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query))
            throw new HttpRequestException("Media public origin is unconfigured.");
        return origin!.TrimEnd('/') + "/" + key;
    }
    public async Task<string> SignAsync(PublicMediaIntent intent, CancellationToken ct)
    {
        using var client = Client();
        return await client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = configuration["Media:R2:Bucket"],
            Key = intent.Key,
            Verb = HttpVerb.PUT,
            ContentType = intent.ContentType,
            Expires = intent.ExpiresAt.UtcDateTime
        });
    }
    public async Task<string> VerifyAsync(PublicMediaIntent intent, CancellationToken ct)
    {
        using var client = Client();
        using var obj = await client.GetObjectAsync(new GetObjectRequest { BucketName = configuration["Media:R2:Bucket"], Key = intent.Key }, ct);
        if (obj.ContentLength != intent.SizeBytes || obj.Headers.ContentType != intent.ContentType) throw new InvalidDataException("Uploaded object does not match its intent.");
        using var memory = new MemoryStream(); var buffer = new byte[81920];
        int length;
        while ((length = await obj.ResponseStream.ReadAsync(buffer, ct)) > 0)
        {
            if (memory.Length + length > 5_242_880) throw new InvalidDataException("Image exceeds the size limit.");
            memory.Write(buffer, 0, length);
        }
        var bytes = memory.ToArray(); Validate(bytes, intent.ContentType);
        // The final key is never granted to a browser PUT. Replacing a staging
        // object after validation cannot alter an attached public image.
        var finalKey = "verified/" + Guid.NewGuid().ToString("N") + "." + Extension(intent.ContentType);
        using var body = new MemoryStream(bytes);
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = configuration["Media:R2:Bucket"],
            Key = finalKey,
            InputStream = body,
            ContentType = intent.ContentType,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        }, ct);
        return PublicUrl(finalKey);
    }
    public static string Extension(string contentType) => contentType switch { "image/jpeg" => "jpg", "image/png" => "png", "image/webp" => "webp", _ => throw new InvalidDataException("Unsupported image type.") };
    public static void Validate(byte[] bytes, string contentType)
    {
        using var data = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width is <= 0 or > 8192 || codec.Info.Height is <= 0 or > 8192
            || (long)codec.Info.Width * codec.Info.Height > 16_777_216 || codec.FrameCount > 1)
            throw new InvalidDataException("Invalid image dimensions or animated image.");
        var format = contentType switch { "image/jpeg" => SKEncodedImageFormat.Jpeg, "image/png" => SKEncodedImageFormat.Png, "image/webp" => SKEncodedImageFormat.Webp, _ => (SKEncodedImageFormat)(-1) };
        if (codec.EncodedFormat != format) throw new InvalidDataException("Declared image type does not match decoded contents.");
        using var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException("Image cannot be fully decoded.");
    }
}
