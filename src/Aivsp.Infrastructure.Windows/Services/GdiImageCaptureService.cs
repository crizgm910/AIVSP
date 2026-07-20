using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.Contracts;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Aivsp.Domain.ValueObjects;
using Aivsp.Infrastructure.Windows.Interop;

namespace Aivsp.Infrastructure.Windows.Services;

public class GdiImageCaptureService : IImageCaptureService
{
    public ValueTask<CapturedImage> CaptureActiveWindowAsync(CancellationToken cancellationToken)
    {
        return new ValueTask<CapturedImage>(CaptureInternal());
    }

    private CapturedImage CaptureInternal()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            throw new Exception("No active window found.");

        if (!NativeMethods.GetWindowRect(hwnd, out RECT rect))
            throw new Exception("Could not get window rect.");

        int width = rect.right - rect.left;
        int height = rect.bottom - rect.top;

        if (width <= 0 || height <= 0)
            throw new Exception("Invalid window dimensions.");

        // Using the desktop DC for BitBlt to get what's on the screen in that area
        IntPtr hDesktop = NativeMethods.GetDesktopWindow();
        using var desktopDc = new SafeDeviceContextHandle(NativeMethods.GetWindowDC(hDesktop), true, hDesktop, false);
        if (desktopDc.IsInvalid)
            throw new Exception("Could not get desktop DC.");

        using var memoryDc = new SafeDeviceContextHandle(NativeMethods.CreateCompatibleDC(desktopDc.DangerousGetHandle()), true, IntPtr.Zero, true);
        if (memoryDc.IsInvalid)
            throw new Exception("Could not create compatible DC.");

        using var bitmap = new SafeGdiObjectHandle(NativeMethods.CreateCompatibleBitmap(desktopDc.DangerousGetHandle(), width, height), true);
        if (bitmap.IsInvalid)
            throw new Exception("Could not create compatible bitmap.");

        IntPtr oldBitmap = NativeMethods.SelectObject(memoryDc.DangerousGetHandle(), bitmap.DangerousGetHandle());
        try
        {
            bool success = NativeMethods.BitBlt(
                memoryDc.DangerousGetHandle(), 0, 0, width, height,
                desktopDc.DangerousGetHandle(), rect.left, rect.top, NativeMethods.SRCCOPY);

            if (!success)
                throw new Exception("BitBlt failed.");

            // Configure DIB
            var bmi = new BITMAPINFO();
            bmi.bmiHeader.Init();
            bmi.bmiHeader.biWidth = width;
            // Negative height for top-down DIB
            bmi.bmiHeader.biHeight = -height;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0; // BI_RGB

            // Calculate stride
            int stride = ((width * 32 + 31) & ~31) >> 3;
            int size = stride * height;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(size);

            int scanLines = NativeMethods.GetDIBits(
                memoryDc.DangerousGetHandle(),
                bitmap.DangerousGetHandle(),
                0,
                (uint)height,
                ref MemoryMarshal.GetReference<byte>(buffer),
                ref bmi,
                NativeMethods.DIB_RGB_COLORS);

            if (scanLines == 0)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw new Exception("GetDIBits failed.");
            }

            var metadata = new ImageMetadata(width, height, stride, PixelFormat.Bgra32, 96, 96);
            
            // Context
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            string processName = "";
            try {
                using var p = Process.GetProcessById((int)pid);
                processName = p.ProcessName;
            } catch { }

            var sb = new StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            string windowTitle = sb.ToString();

            var context = new CaptureContext(processName, windowTitle, DateTime.Now, rect.left, rect.top);

            return new CapturedImage(buffer, size, metadata, context);
        }
        finally
        {
            NativeMethods.SelectObject(memoryDc.DangerousGetHandle(), oldBitmap);
        }
    }
}
