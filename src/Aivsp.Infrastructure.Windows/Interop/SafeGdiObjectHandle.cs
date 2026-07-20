using System;
using Microsoft.Win32.SafeHandles;

namespace Aivsp.Infrastructure.Windows.Interop;

internal class SafeGdiObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeGdiObjectHandle() : base(true) { }

    public SafeGdiObjectHandle(IntPtr preexistingHandle, bool ownsHandle) : base(ownsHandle)
    {
        SetHandle(preexistingHandle);
    }

    protected override bool ReleaseHandle()
    {
        return NativeMethods.DeleteObject(handle);
    }
}
