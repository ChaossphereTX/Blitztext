using System.Runtime.InteropServices;
using System.Windows;

namespace Blitztext.App.Platform;

/// <summary>
/// Writes text to the clipboard and pastes it into the previously focused window via
/// synthetic Ctrl+V. Windows counterpart to the macOS auto-paste path (NSPasteboard +
/// CGEvent Cmd+V + frontmost-app restore). The text intentionally stays on the clipboard
/// as a fallback if pasting is blocked.
/// </summary>
public static class AutoPasteService
{
    /// <summary>Capture the current foreground window so we can paste back into it later.</summary>
    public static IntPtr CaptureForegroundWindow()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            Log.Write("capture: foreground = 0");
            return IntPtr.Zero;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == (uint)Environment.ProcessId)
        {
            Log.Write("capture: foreground is own process -> ignored");
            return IntPtr.Zero;
        }

        Log.Write($"capture: target hwnd={hwnd} pid={pid}");
        return hwnd;
    }

    public static bool WriteToClipboard(string text)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                Log.Write($"clipboard: set ok ({text.Length} chars)");
                return true;
            }
            catch (Exception ex)
            {
                Log.Write($"clipboard: attempt {attempt} failed: {ex.Message}");
                Thread.Sleep(40);
            }
        }

        return false;
    }

    /// <summary>Restore focus to the target window and paste (Ctrl+V), with one retry.</summary>
    public static void PasteInto(IntPtr target)
    {
        if (target == IntPtr.Zero || !NativeMethods.IsWindow(target))
        {
            Log.Write($"paste: invalid target {target} -> skipped (clipboard fallback only)");
            return;
        }

        IntPtr fg = NativeMethods.GetForegroundWindow();
        Log.Write($"paste: target={target} foreground={fg}");

        // Restore focus to the target window only if it isn't already in front, then settle
        // and paste exactly once (a second Ctrl+V would duplicate the text).
        if (fg != target)
        {
            BringToForeground(target);
            Thread.Sleep(60);
        }

        Thread.Sleep(40);
        SendCtrlV();
        Log.Write($"paste: Ctrl+V sent, foreground={NativeMethods.GetForegroundWindow()}");
    }

    private static void BringToForeground(IntPtr target)
    {
        uint targetThread = NativeMethods.GetWindowThreadProcessId(target, out _);
        uint currentThread = NativeMethods.GetCurrentThreadId();

        bool attached = false;
        if (targetThread != currentThread)
        {
            attached = NativeMethods.AttachThreadInput(currentThread, targetThread, true);
        }

        try
        {
            NativeMethods.SetForegroundWindow(target);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }
        }
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            Key(NativeMethods.VK_CONTROL, false),
            Key(NativeMethods.VK_V, false),
            Key(NativeMethods.VK_V, true),
            Key(NativeMethods.VK_CONTROL, true),
        };

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            Log.Write($"paste: SendInput injected {sent}/{inputs.Length}, err={Marshal.GetLastWin32Error()}");
        }
    }

    private static NativeMethods.INPUT Key(ushort vk, bool keyUp)
    {
        ushort scan = (ushort)NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC);
        uint flags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0;

        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags,
                },
            },
        };
    }
}
