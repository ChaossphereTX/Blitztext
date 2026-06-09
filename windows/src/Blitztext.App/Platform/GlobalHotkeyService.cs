using Blitztext.Core.Workflows;

namespace Blitztext.App.Platform;

public enum HotkeyEventKind { Down, Up, Cancel }

public sealed record HotkeyEvent(HotkeyEventKind Kind, WorkflowType Type);

/// <summary>
/// Global hotkeys via a WH_KEYBOARD_LL low-level keyboard hook. This is the Windows
/// counterpart to the macOS NSEvent global monitors. The macOS <c>fn</c> modifier does not
/// exist on Windows, so the chords are Ctrl+Shift+&lt;trigger&gt;:
///   Space → Transcription, E → TextImprover, R → DampfAblassen, J → EmojiText, L → Local.
/// Escape raises a cancel event. Down/Up events drive both hold and toggle modes.
/// The hook is installed on the WPF UI thread (which already pumps messages); the callback
/// is lightweight and the real work runs asynchronously, so input is never blocked.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int VkEscape = 0x1B;

    private static readonly Dictionary<uint, WorkflowType> TriggerKeys = new()
    {
        [0x44] = WorkflowType.Transcription,      // D (Diktat) – Space was a poor choice
        [0x45] = WorkflowType.TextImprover,       // E
        [0x52] = WorkflowType.DampfAblassen,      // R
        [0x4A] = WorkflowType.EmojiText,          // J
        [0x4C] = WorkflowType.LocalTranscription, // L
    };

    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hookHandle = IntPtr.Zero;
    private WorkflowType? _activeCombo;
    private uint _activeTriggerKey;

    public event Action<HotkeyEvent>? HotkeyFired;

    public GlobalHotkeyService()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        IntPtr module = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, module, 0);
    }

    public void Stop()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        int msg = (int)wParam;
        var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        uint vk = data.vkCode;

        bool isKeyDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
        bool isKeyUp = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

        if (isKeyDown)
        {
            if (vk == VkEscape)
            {
                _activeCombo = null;
                _activeTriggerKey = 0;
                HotkeyFired?.Invoke(new HotkeyEvent(HotkeyEventKind.Cancel, WorkflowType.Transcription));
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            if (_activeCombo == null && TriggerKeys.TryGetValue(vk, out WorkflowType type) && CtrlShiftHeld())
            {
                _activeCombo = type;
                _activeTriggerKey = vk;
                HotkeyFired?.Invoke(new HotkeyEvent(HotkeyEventKind.Down, type));
                return (IntPtr)1; // swallow so the trigger letter is not typed into the target app
            }

            // Swallow auto-repeat of the active trigger while held.
            if (_activeCombo != null && vk == _activeTriggerKey)
            {
                return (IntPtr)1;
            }
        }
        else if (isKeyUp)
        {
            if (_activeCombo != null && vk == _activeTriggerKey)
            {
                WorkflowType type = _activeCombo.Value;
                _activeCombo = null;
                _activeTriggerKey = 0;
                HotkeyFired?.Invoke(new HotkeyEvent(HotkeyEventKind.Up, type));
                return (IntPtr)1;
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool CtrlShiftHeld()
    {
        bool ctrl = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL_STATE) & 0x8000) != 0;
        bool shift = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
        return ctrl && shift;
    }

    public void Dispose() => Stop();
}
