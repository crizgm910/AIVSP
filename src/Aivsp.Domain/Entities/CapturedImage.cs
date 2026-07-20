using System;
using System.Buffers;
using System.Threading;
using Aivsp.Domain.ValueObjects;

namespace Aivsp.Domain.Entities;

public sealed class CapturedImage : IDisposable
{
    private byte[]? _buffer;
    private int _isDisposed;

    public ImageMetadata Metadata { get; }
    public CaptureContext Context { get; }
    public int Length { get; }

    public ReadOnlyMemory<byte> Data
    {
        get
        {
            if (_isDisposed == 1)
                throw new ObjectDisposedException(nameof(CapturedImage));
            return new ReadOnlyMemory<byte>(_buffer, 0, Length);
        }
    }

    public CapturedImage(byte[] buffer, int length, ImageMetadata metadata, CaptureContext context)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        Length = length;
        Metadata = metadata;
        Context = context;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
                _buffer = null;
            }
        }
    }
}
