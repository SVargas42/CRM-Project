using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace HippoCrm.Core;

public static class ImageSanitizer
{
    private const int iMaximumInputBytes = 10 * 1024 * 1024;
    private const int iMaximumOutputBytes = 20 * 1024 * 1024;
    private const int iMaximumDimension = 4096;
    private const long lngMaximumPixels = 16_777_216;

    // Takes ownership of the input, including on rejection. The caller must wipe
    // the returned PNG after its encrypted BLOB write has completed.
    public static byte[] Sanitize(byte[] arrUntrusted)
    {
        ArgumentNullException.ThrowIfNull(arrUntrusted);
        var gchUntrusted = default(GCHandle);
        Image<Rgba32>? imgDecoded = null;
        Image<Rgba32>? imgCanvas = null;
        MemoryStream? msEncoded = null;
        byte[]? arrResult = null;
        var bTransferred = false;
        try
        {
            gchUntrusted = GCHandle.Alloc(arrUntrusted, GCHandleType.Pinned);
            if (arrUntrusted.Length == 0 || arrUntrusted.Length > iMaximumInputBytes || !HasAllowedHeader(arrUntrusted))
                throw new ArgumentException("Choose a supported PNG or JPEG picture.");

            var objOptions = new DecoderOptions { SkipMetadata = true, MaxFrames = 1 };
            var objInfo = Image.Identify(objOptions, arrUntrusted);
            ValidateDimensions(objInfo.Width, objInfo.Height);
            imgDecoded = Image.Load<Rgba32>(objOptions, arrUntrusted);
            ValidateDimensions(imgDecoded.Width, imgDecoded.Height);

            // A new single-frame canvas transfers pixels only. Source EXIF, XMP,
            // IPTC, ICC and format-specific metadata never reach the output.
            imgCanvas = new Image<Rgba32>(imgDecoded.Width, imgDecoded.Height);
            imgCanvas.Mutate(objContext => objContext.DrawImage(imgDecoded, new Point(0, 0), 1f));
            msEncoded = new MemoryStream();
            imgCanvas.Save(msEncoded, new PngEncoder { SkipMetadata = true });
            if (msEncoded.Length > iMaximumOutputBytes) throw new ArgumentException("The picture is too large.");
            arrResult = SensitiveMemory.AllocateBytes(checked((int)msEncoded.Length));
            msEncoded.GetBuffer().AsSpan(0, arrResult.Length).CopyTo(arrResult);
            // Cleanup occurs before ownership can actually reach the caller.
            // If cleanup throws, its outer catch clears this result as well.
            bTransferred = true;
            return arrResult;
        }
        finally
        {
            try
            {
            // Nested finally blocks guarantee input wiping even if disposal fails.
            try { ClearPixels(imgDecoded); }
            finally
            {
                try { ClearPixels(imgCanvas); }
                finally
                {
                    try
                    {
                        if (msEncoded is not null)
                        {
                            try { SensitiveMemory.Clear(msEncoded.GetBuffer()); }
                            finally { msEncoded.Dispose(); }
                        }
                    }
                    finally
                    {
                        if (!bTransferred) SensitiveMemory.Clear(arrResult);
                        try { SensitiveMemory.Clear(arrUntrusted); }
                        finally { if (gchUntrusted.IsAllocated) gchUntrusted.Free(); }
                    }
                }
            }
            }
            catch
            {
                SensitiveMemory.Clear(arrResult);
                throw;
            }
        }
    }

    private static bool HasAllowedHeader(ReadOnlySpan<byte> spnInput) =>
        (spnInput.Length >= 8 && spnInput[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) ||
        (spnInput.Length >= 3 && spnInput[0] == 0xff && spnInput[1] == 0xd8 && spnInput[2] == 0xff);

    private static void ValidateDimensions(int iWidth, int iHeight)
    {
        if (iWidth < 1 || iHeight < 1 || iWidth > iMaximumDimension || iHeight > iMaximumDimension ||
            (long)iWidth * iHeight > lngMaximumPixels)
            throw new ArgumentException("The picture dimensions are too large.");
    }

    private static void ClearPixels(Image<Rgba32>? imgImage)
    {
        if (imgImage is null) return;
        try
        {
            imgImage.ProcessPixelRows(objPixels =>
            {
                for (var iRow = 0; iRow < objPixels.Height; iRow++)
                {
                    var spnBytes = MemoryMarshal.AsBytes(objPixels.GetRowSpan(iRow));
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(spnBytes);
                }
            });
        }
        finally { imgImage.Dispose(); }
    }
}
