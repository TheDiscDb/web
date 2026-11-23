namespace TheDiscDb.Data.Import;

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fantastic.FileSystem;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

public static class CoverImageConverter
{
    public static async Task Normalize(IFileSystem fileSystem, string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] original = await fileSystem.File.ReadAllBytes(path, cancellationToken);
        byte[]? converted = null;
        try
        {
            using var input = new MemoryStream(original);
            using var image = await Image.LoadAsync(input, cancellationToken);
            if (image.Metadata.DecodedImageFormat != JpegFormat.Instance)
            {
                image.Mutate(context => context.AutoOrient().BackgroundColor(Color.White));
                using var output = new MemoryStream();
                await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 90 }, cancellationToken);
                converted = output.ToArray();
            }
        }
        catch (UnknownImageFormatException exception)
        {
            throw new InvalidDataException(
                $"Cover image '{path}' uses a format ImageSharp cannot decode (including AVIF). Replace it with a valid JPEG, PNG, or WebP image.", exception);
        }
        catch (InvalidImageContentException exception)
        {
            throw new InvalidDataException(
                $"Cover image '{path}' is invalid and could not be converted to JPEG. Replace it with a valid JPEG, PNG, or WebP image.", exception);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (converted != null)
        {
            await fileSystem.File.WriteAllBytes(path, converted, cancellationToken);
        }
    }
}