using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MonAlbumPc.Core.Media;

namespace MonAlbumPc.App.Photos;

/// <summary>Decodes thumbnails off the UI thread: photos with WPF at the needed size, videos through the Windows shell.</summary>
internal static class Thumbnails
{
    private static readonly SemaphoreSlim Gate = new(4);

    public static async Task<ImageSource?> LoadAsync(string path, bool isVideo, int pixels)
    {
        await Gate.WaitAsync();
        try
        {
            return await Task.Run(() => isVideo ? FromShell(path, pixels) : FromFile(path, pixels));
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or COMException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Full-size image for the viewer, upright.</summary>
    public static ImageSource? LoadFull(string path, int maxPixels)
    {
        try
        {
            return FromFile(path, maxPixels);
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ImageSource FromFile(string path, int pixels)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.UriSource = new Uri(path);
        image.DecodePixelWidth = pixels;
        image.EndInit();
        image.Freeze();

        // WPF ignores the EXIF orientation: apply it so phone photos are upright.
        var degrees = ExifOrientation.Degrees(ExifOrientation.Read(path));
        if (degrees == 0)
        {
            return image;
        }
        var rotated = new TransformedBitmap(image, new RotateTransform(degrees));
        rotated.Freeze();
        return rotated;
    }

    private static ImageSource? FromShell(string path, int pixels)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
        try
        {
            if (factory.GetImage(new NativeSize(pixels, pixels), BiggerSizeOk, out var bitmap) != 0)
            {
                return null;
            }
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(bitmap);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    private const int BiggerSizeOk = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeSize(int Width, int Height);

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
