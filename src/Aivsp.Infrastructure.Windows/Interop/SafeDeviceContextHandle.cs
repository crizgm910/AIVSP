using System;
using Microsoft.Win32.SafeHandles;

namespace Aivsp.Infrastructure.Windows.Interop;

internal class SafeDeviceContextHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isCreated = false;

    private SafeDeviceContextHandle() : base(true) { }

    public SafeDeviceContextHandle(IntPtr preexistingHandle, bool ownsHandle, IntPtr hwnd = default, bool isCreated = false) 
        : base(ownsHandle)
    {
        SetHandle(preexistingHandle);
        _hwnd = hwnd;
        _isCreated = isCreated;
    }

    protected override bool ReleaseHandle()
    {
        if (_isCreated)
        {
            return NativeMethods.DeleteDC(handle);
        }
        else
        {
            return NativeMethods.ReleaseDC(_hwnd, handle) == 1;
        }
    }
}
