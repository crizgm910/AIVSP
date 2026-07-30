using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Aivsp.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace Aivsp.Presentation.Wpf.Services;

public class Win32InputSimulator : IInputSimulationService
{
    private readonly ILogger<Win32InputSimulator> _logger;

    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInput);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    public Win32InputSimulator(ILogger<Win32InputSimulator> logger)
    {
        _logger = logger;
    }

    public async Task SimulatePasteAsync(string text, double targetX, double targetY, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Simulando teclado para pegar el texto de la respuesta.");

        var clipboardSnapshot = await System.Windows.Application.Current.Dispatcher.InvokeAsync(
            CaptureClipboardSnapshot);

        try
        {
            // Clipboard can be temporarily locked by the browser or another process.
            Exception? clipboardError = null;
            var clipboardReady = false;
            for (var attempt = 0; attempt < 3 && !clipboardReady; attempt++)
            {
                try
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        System.Windows.Clipboard.SetText(text));
                    clipboardReady = true;
                }
                catch (Exception ex)
                {
                    clipboardError = ex;
                    if (attempt < 2) await Task.Delay(75, cancellationToken);
                }
            }

            if (!clipboardReady)
            {
                throw new InvalidOperationException("No se pudo preparar el portapapeles para pegar la respuesta.", clipboardError);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Focus the detected input before pasting. The hotkey is normally pressed
            // while the cursor is still over the question text.
            try
            {
                if (!SetCursorPos((int)Math.Round(targetX), (int)Math.Round(targetY)))
                {
                    _logger.LogWarning("No se pudo mover el cursor al campo de respuesta ({X}, {Y}).", targetX, targetY);
                }

                SendOrThrow(new[]
                {
                    MouseInput(MOUSEEVENTF_LEFTDOWN),
                    MouseInput(MOUSEEVENTF_LEFTUP)
                });

                await Task.Delay(200, cancellationToken);

                SendOrThrow(new[]
                {
                    KeyboardInput(VK_LCONTROL, 0),
                    KeyboardInput(VK_V, 0),
                    KeyboardInput(VK_V, KEYEVENTF_KEYUP),
                    KeyboardInput(VK_LCONTROL, KEYEVENTF_KEYUP)
                });

                // Let the target process consume WM_PASTE before restoring the previous data.
                await Task.Delay(150, CancellationToken.None);
                _logger.LogInformation("Texto pegado automáticamente mediante simulación Ctrl+V.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al simular las teclas Ctrl+V.");
                throw;
            }
        }
        finally
        {
            await RestoreClipboardSnapshotAsync(clipboardSnapshot);
        }
    }

    private static ClipboardSnapshot CaptureClipboardSnapshot()
    {
        var source = System.Windows.Clipboard.GetDataObject();
        if (source == null)
        {
            return new ClipboardSnapshot(Array.Empty<ClipboardDataItem>());
        }

        var items = new List<ClipboardDataItem>();
        foreach (var format in source.GetFormats(autoConvert: false))
        {
            try
            {
                var data = source.GetData(format, autoConvert: false);
                if (data != null)
                {
                    items.Add(new ClipboardDataItem(format, data));
                }
            }
            catch
            {
                // Some owner-rendered formats cannot be copied safely. Other formats,
                // including text, are still preserved when available.
            }
        }

        return new ClipboardSnapshot(items);
    }

    private static async Task RestoreClipboardSnapshotAsync(ClipboardSnapshot snapshot)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (snapshot.Items.Count == 0)
                    {
                        System.Windows.Clipboard.Clear();
                        return;
                    }

                    var restored = new System.Windows.DataObject();
                    foreach (var item in snapshot.Items)
                    {
                        restored.SetData(item.Format, item.Data, autoConvert: false);
                    }

                    System.Windows.Clipboard.SetDataObject(restored, copy: true);
                });
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt < 2)
                {
                    await Task.Delay(75, CancellationToken.None);
                }
            }
        }

        throw new InvalidOperationException("No se pudo restaurar el portapapeles anterior.", lastError);
    }

    private static void SendOrThrow(INPUT[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != (uint)inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SendInput envió {sent} de {inputs.Length} eventos.");
        }
    }

    private static INPUT MouseInput(uint flags) => new()
    {
        Type = INPUT_MOUSE,
        Data = new InputUnion
        {
            Mouse = new MOUSEINPUT { Flags = flags }
        }
    };

    private static INPUT KeyboardInput(ushort key, uint flags) => new()
    {
        Type = INPUT_KEYBOARD,
        Data = new InputUnion
        {
            Keyboard = new KEYBDINPUT { VirtualKey = key, Flags = flags }
        }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    private sealed record ClipboardDataItem(string Format, object Data);
    private sealed record ClipboardSnapshot(IReadOnlyList<ClipboardDataItem> Items);
}
