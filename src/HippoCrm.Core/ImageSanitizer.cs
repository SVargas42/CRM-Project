using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace HippoCrm.Core;

public static class ImageSanitizer
{
    private const int iMaximumInputBytes = 10 * 1024 * 1024;
    private const int iMaximumOutputBytes = 5 * 1024 * 1024;
    private const int iMaximumDimension = 4096;
    private const long lngMaximumPixels = 16_777_216;

    // Ownership transfers in: the original is wiped on every exit. Ownership of
    // the pinned PNG transfers out: its caller must wipe it after the BLOB write.
    public static byte[] Sanitize(byte[] arrUntrusted, CancellationToken ctCancellation = default)
    {
        ArgumentNullException.ThrowIfNull(arrUntrusted);
        var gchUntrusted = default(GCHandle);
        Image<Rgba32>? imgDecoded = null;
        Image<Rgba32>? imgCanvas = null;
        PinnedClearingAllocator? objAllocator = null;
        byte[]? arrEncoded = null;
        byte[]? arrResult = null;
        var bTransferred = false;
        try
        {
            ctCancellation.ThrowIfCancellationRequested();
            gchUntrusted = GCHandle.Alloc(arrUntrusted, GCHandleType.Pinned);
            if (arrUntrusted.Length == 0 || arrUntrusted.Length > iMaximumInputBytes || !HasAllowedHeader(arrUntrusted))
                throw new ArgumentException("Choose a supported PNG or JPEG picture.");

            objAllocator = new PinnedClearingAllocator();
            var objConfiguration = Configuration.Default.Clone();
            objConfiguration.MemoryAllocator = objAllocator;
            objConfiguration.MaxDegreeOfParallelism = 1;
            var objOptions = new DecoderOptions { Configuration = objConfiguration, SkipMetadata = true, MaxFrames = 1 };
            using var stmInput = new MemoryStream(arrUntrusted, writable: false);
            var objInfo = Image.Identify(objOptions, stmInput);
            ValidateDimensions(objInfo.Width, objInfo.Height);
            stmInput.Position = 0;
            imgDecoded = Image.Load<Rgba32>(objOptions, stmInput);
            ValidateDimensions(imgDecoded.Width, imgDecoded.Height);

            ctCancellation.ThrowIfCancellationRequested();
            // Bound stored/preview pixels independently of the source dimensions.
            imgDecoded.Mutate(objContext => objContext.AutoOrient().Resize(new ResizeOptions
                { Size = new Size(1024, 1024), Mode = ResizeMode.Max }));
            // Transfer pixels only, never the source image's metadata profiles.
            imgCanvas = new Image<Rgba32>(objConfiguration, imgDecoded.Width, imgDecoded.Height);
            imgCanvas.Mutate(objContext => objContext.DrawImage(imgDecoded, new Point(0, 0), 1f));

            // A fixed backing array prevents MemoryStream growth from abandoning
            // earlier plaintext image buffers. Overflow rejects the picture.
            arrEncoded = SensitiveMemory.AllocateBytes(iMaximumOutputBytes);
            using var stmEncoded = new MemoryStream(arrEncoded, 0, arrEncoded.Length, writable: true, publiclyVisible: false);
            stmEncoded.SetLength(0);
            try { imgCanvas.Save(stmEncoded, new PngEncoder { SkipMetadata = true }); }
            catch (NotSupportedException objException)
            { throw new ArgumentException("The picture is too large.", objException); }
            arrResult = SensitiveMemory.AllocateBytes(checked((int)stmEncoded.Length));
            arrEncoded.AsSpan(0, arrResult.Length).CopyTo(arrResult);
            ctCancellation.ThrowIfCancellationRequested();
            bTransferred = true;
            return arrResult;
        }
        catch(Exception objException) when(objException is InvalidImageContentException or UnknownImageFormatException)
        { throw new ArgumentException("Choose a valid PNG or JPEG picture."); }
        finally
        {
            try
            {
                try { imgDecoded?.Dispose(); }
                finally
                {
                    try { imgCanvas?.Dispose(); }
                    finally { objAllocator?.Dispose(); }
                }
            }
            catch { SensitiveMemory.Clear(arrResult); throw; }
            finally
            {
                try { SensitiveMemory.Clear(arrEncoded); }
                finally
                {
                    if (!bTransferred) SensitiveMemory.Clear(arrResult);
                    try { SensitiveMemory.Clear(arrUntrusted); }
                    finally { if (gchUntrusted.IsAllocated) gchUntrusted.Free(); }
                }
            }
        }
    }

    // Caller owns the pinned RGBA pixels and must clear them after copying to a
    // disposable preview surface. Source BLOB ownership stays with the caller.
    public static (int iWidth,int iHeight,byte[] arrPixels) Preview(byte[] arrPng)
    {
        try { return DecodePreview(arrPng); }
        catch(Exception objException) when(objException is InvalidImageContentException or UnknownImageFormatException)
        { throw new ArgumentException("The stored picture could not be displayed."); }
    }
    private static (int iWidth,int iHeight,byte[] arrPixels) DecodePreview(byte[] arrPng)
    {
        if(arrPng.Length is < 8 or > 20*1024*1024 || !HasAllowedHeader(arrPng)) throw new ArgumentException("The picture cannot be displayed.");
        using var objAllocator=new PinnedClearingAllocator();
        var objConfiguration=Configuration.Default.Clone(); objConfiguration.MemoryAllocator=objAllocator; objConfiguration.MaxDegreeOfParallelism=1;
        var objOptions=new DecoderOptions { Configuration=objConfiguration,SkipMetadata=true,MaxFrames=1 };
        using var stmSource=new MemoryStream(arrPng,false);
        var objInfo=Image.Identify(objOptions,stmSource); ValidateDimensions(objInfo.Width,objInfo.Height); stmSource.Position=0;
        using var imgPreview=Image.Load<Rgba32>(objOptions,stmSource);
        imgPreview.Mutate(objContext=>objContext.Resize(new ResizeOptions { Size=new Size(1024,1024),Mode=ResizeMode.Max }));
        var arrPixels=SensitiveMemory.AllocateBytes(checked(imgPreview.Width*imgPreview.Height*4));
        try { imgPreview.CopyPixelDataTo(arrPixels); return (imgPreview.Width,imgPreview.Height,arrPixels); }
        catch { SensitiveMemory.Clear(arrPixels); throw; }
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

    // ImageSharp receives operation-owned pinned buffers instead of its shared
    // pixel pool. Track every owner so failed decoders cannot leave these buffers
    // uncleared even if they omit an individual owner's Dispose call.
    private sealed class PinnedClearingAllocator : MemoryAllocator, IDisposable
    {
        private readonly object objGate = new();
        private readonly List<IDisposable> lstOwners = [];
        private long lngAllocatedBytes;
        private bool bDisposed;
        protected override int GetBufferCapacityInBytes() => 4 * 1024 * 1024;
        public override IMemoryOwner<T> Allocate<T>(int iLength, AllocationOptions enOptions = AllocationOptions.None)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(iLength);
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) throw new NotSupportedException("Unsupported image buffer.");
            lock (objGate)
            {
                ObjectDisposedException.ThrowIf(bDisposed, this);
                var lngBytes = checked((long)iLength * Unsafe.SizeOf<T>());
                if (lngBytes > 256L * 1024 * 1024 - lngAllocatedBytes)
                    throw new ArgumentException("The picture requires too much memory.");
                var objOwner = new PinnedImageMemory<T>(iLength);
                lstOwners.Add(objOwner);
                lngAllocatedBytes += lngBytes;
                return objOwner;
            }
        }
        public void Dispose()
        {
            lock (objGate)
            {
                if (bDisposed) return;
                bDisposed = true;
                foreach (var objOwner in lstOwners) objOwner.Dispose();
                lstOwners.Clear();
            }
        }
    }

    private sealed class PinnedImageMemory<T>(int iLength) : IMemoryOwner<T> where T : struct
    {
        private T[]? arrBuffer = GC.AllocateArray<T>(iLength, pinned: true);
        public Memory<T> Memory => (arrBuffer ?? throw new ObjectDisposedException(nameof(PinnedImageMemory<T>))).AsMemory();
        public void Dispose()
        {
            var arrClearing = Interlocked.Exchange(ref arrBuffer, null);
            if (arrClearing is null) return;
            try { CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(arrClearing.AsSpan())); }
            finally { Array.Clear(arrClearing, 0, arrClearing.Length); }
        }
    }
}
