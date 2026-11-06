using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HippoCrm.Core;

namespace HippoCrm.Desktop.Views;

// Own and overwrite CPU preview pixels before releasing the native surface.
public sealed class SecureProfileImage : Image
{
    public static readonly StyledProperty<byte[]?> arrImageProperty=AvaloniaProperty.Register<SecureProfileImage,byte[]?>(nameof(arrImage));
    public byte[]? arrImage { get=>GetValue(arrImageProperty); set=>SetValue(arrImageProperty,value); }
    private WriteableBitmap? bmpOwned;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs objArguments)
    { base.OnPropertyChanged(objArguments); if(objArguments.Property==arrImageProperty) Refresh(); }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs objArguments) { base.OnAttachedToVisualTree(objArguments); Refresh(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs objArguments) { Clear(); base.OnDetachedFromVisualTree(objArguments); }
    private void Refresh()
    {
        Clear(); if(arrImage is not { } arrSource) return;
        byte[]? arrPixels=null;
        try
        {
            var objPreview=ImageSanitizer.Preview(arrSource); arrPixels=objPreview.arrPixels;
            bmpOwned=new WriteableBitmap(new PixelSize(objPreview.iWidth,objPreview.iHeight),new Vector(96,96),PixelFormat.Rgba8888,AlphaFormat.Unpremul);
            using(var objSurface=bmpOwned.Lock())
                for(var iRow=0;iRow<objPreview.iHeight;iRow++) Marshal.Copy(arrPixels,iRow*objPreview.iWidth*4,IntPtr.Add(objSurface.Address,iRow*objSurface.RowBytes),objPreview.iWidth*4);
            Source=bmpOwned;
        }
        catch(Exception objException) when(objException is ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        { Clear(); }
        finally { if(arrPixels is not null) CryptographicOperations.ZeroMemory(arrPixels); }
    }
    private void Clear()
    {
        Source=null; var bmpPrevious=bmpOwned; bmpOwned=null; if(bmpPrevious is null) return;
        try
        {
            using var objSurface=bmpPrevious.Lock();
            var arrZeros=new byte[objSurface.RowBytes];
            for(var iRow=0;iRow<objSurface.Size.Height;iRow++) Marshal.Copy(arrZeros,0,IntPtr.Add(objSurface.Address,iRow*objSurface.RowBytes),arrZeros.Length);
        }
        finally { bmpPrevious.Dispose(); }
    }
}
