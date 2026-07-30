using System;
using System.Runtime.InteropServices;
using Aivsp.Application.Contracts;
using Aivsp.Domain.ValueObjects;
using Aivsp.Infrastructure.Windows.Interop;

namespace Aivsp.Infrastructure.Windows.Services;

public sealed class Win32ActiveWindowContextService : IActiveWindowContextService
{
    public ActiveWindowIdentity GetActiveWindowIdentity()
    {
        var windowHandle = NativeMethods.GetForegroundWindow();
        return ReadIdentity(windowHandle);
    }

    internal static ActiveWindowIdentity ReadIdentity(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return default;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        var guiInfo = new GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<GUITHREADINFO>()
        };

        var focusedHandle = threadId != 0 && NativeMethods.GetGUIThreadInfo(threadId, ref guiInfo)
            ? guiInfo.hwndFocus
            : IntPtr.Zero;

        return new ActiveWindowIdentity(
            windowHandle.ToInt64(),
            checked((int)processId),
            focusedHandle.ToInt64());
    }
}
