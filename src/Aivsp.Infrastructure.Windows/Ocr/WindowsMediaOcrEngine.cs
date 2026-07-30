using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Aivsp.Domain.ValueObjects;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using DomainOcrResult = Aivsp.Domain.Entities.OcrResult;

namespace Aivsp.Infrastructure.Windows.Ocr;

[ComImport]
[Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe interface IMemoryBufferByteAccess
{
    void GetBuffer(out byte* buffer, out uint capacity);
}

public class WindowsMediaOcrEngine : IOcrEngine
{
    public async Task<IReadOnlyList<DomainOcrResult>> ExtractTextAsync(CapturedImage image, AssistantOptions options, CancellationToken cancellationToken)
    {
        var language = new global::Windows.Globalization.Language(options.OcrLanguage);
        if (!OcrEngine.IsLanguageSupported(language))
        {
            throw new InvalidOperationException($"OCR Language {options.OcrLanguage} is not supported.");
        }

        var ocrEngine = OcrEngine.TryCreateFromLanguage(language);
        if (ocrEngine == null)
        {
            throw new InvalidOperationException("Failed to create OcrEngine.");
        }

        var format = BitmapPixelFormat.Bgra8;
        if (image.Metadata.PixelFormat != PixelFormat.Bgra32)
        {
            throw new NotSupportedException("Only Bgra32 is supported for OCR currently.");
        }

        var softwareBitmap = new SoftwareBitmap(format, image.Metadata.Width, image.Metadata.Height, BitmapAlphaMode.Ignore);

        CopyDataToBitmap(image, softwareBitmap);

        var result = await ocrEngine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken);

        var ocrResults = new List<DomainOcrResult>();
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0) continue;

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var word in line.Words)
            {
                var rect = word.BoundingRect;
                if (rect.X < minX) minX = rect.X;
                if (rect.Y < minY) minY = rect.Y;
                if (rect.X + rect.Width > maxX) maxX = rect.X + rect.Width;
                if (rect.Y + rect.Height > maxY) maxY = rect.Y + rect.Height;
            }

            var box = new BoundingBox(
                image.Context.OriginX + (int)minX,
                image.Context.OriginY + (int)minY,
                (int)(maxX - minX),
                (int)(maxY - minY));

            ocrResults.Add(new DomainOcrResult(line.Text, box, 1.0, OcrResultLevel.Line));
        }

        return ocrResults;
    }

    private void CopyDataToBitmap(CapturedImage image, SoftwareBitmap softwareBitmap)
    {
        // En .NET 8 con CsWinRT, interactuar directamente con interfaces COM de bajo nivel 
        // como IMemoryBufferByteAccess causa problemas graves debido a restricciones del CLR.
        // La forma 100% segura y nativa de pasar bytes a un SoftwareBitmap es usar CryptographicBuffer.
        var bytes = image.Data.ToArray();
        var buffer = global::Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(bytes);
        softwareBitmap.CopyFromBuffer(buffer);
    }
}
