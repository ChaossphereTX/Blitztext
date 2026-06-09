using Blitztext.Core.Workflows;

namespace Blitztext.App.Platform;

public enum HotkeyEventKind { Down, Up, Cancel }

public sealed record HotkeyEvent(HotkeyEventKind Kind, WorkflowType Type);

/// <summary>
/// Global hotkey for the transcription workflow via a WH_KEYBOARD_LL hook. The trigger is
/// <b>Ctrl+Shift held alone</b> (the Windows analogue of the macOS fn+Shift push-to-talk).
///
/// Because Ctrl+Shift is also used by many normal shortcuts (Ctrl+Shift+Arrow, Ctrl+Shift+Esc, …),
/// two safeguards apply:
///   1. The modifier keys are never swallowed — normal Ctrl+Shift shortcuts keep working.
///   2. Recording only starts when Ctrl+Shift are held <i>alone</i> for ~250 ms with no other key.
///      If any other key is pressed (i.e. it was a real shortcut), the pending/active trigger is
///      cancelled. Escape cancels an active workflow.
///
/// The other workflows (Blitztext+, $%&!, :), Lokal) have no global hotkey — they are started from
/// the tray popover.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int HoldDelayMs = 250;
    private const int VkEscape = 0x1B;

    private static bool IsCtrl(uint vk) => vk is 0x11 or 0xA2 or 0xA3;   // VK_CONTROL / L / R
    private static bool IsShift(uint vk) => vk is 0x10 or 0xA0 or 0xA1;  // VK_SHIFT / L / R

    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hookHandle = IntPtr.Zero;

    private bool _ctrl;
    private bool _shift;
    private bool _otherKeyDown;   // a non-modifier key was pressed during this Ctrl+Shift session
    private bool _active;         // a Down event has been raised (recording in progress)
    private System.Threading.Timer? _pendingTimer;

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

        CancelPendingTimer();
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

        bool isDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
        bool isUp = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

        if (isDown)
        {
            if (vk == VkEscape)
            {
                Disqualify();
                HotkeyFired?.Invoke(new HotkeyEvent(HotkeyEventKind.Cancel, WorkflowType.Transcription));
            }
            else if (IsCtrl(vk))
            {
                _ctrl = true;
                MaybeSchedule();
            }
            else if (IsShift(vk))
            {
                _shift = true;
                MaybeSchedule();
            }
            else
            {
                // Any non-modifier key means this is a normal shortcut, not push-to-talk.
                _otherKeyDown = true;
                Disqualify();
            }
        }
        else if (isUp)
        {
            if (IsCtrl(vk))
            {
                _ctrl = false;
                OnModifierReleased();
            }
            else if (IsShift(vk))
            {
                _shift = false;
                OnModifierReleased();
            }
        }

        // Never swallow keys — Ctrl/Shift must keep working as normal modifiers.
        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void MaybeSchedule()
    {
        if (_ctrl && _shift && !_active && !_otherKeyDown && _pendingTimer == null)
        {
            _pendingTimer = new System.Threading.Timer(_ => FirePending(), null, HoldDelayMs, System.Threading.Timeout.Infinite);
        }
    }

    private void FirePending()
    {
        CancelPendingTimer();
        if (_ctrl && _shift && !_otherKeyDown && !_active)
        {
            _active = true;
            HotkeyFired?.Invoke(new HotkeyEvent(HotkeyEventKind.Down, WorkflowType.Transcription));
        }
    }

    private void OnModifierReleased()
    {
        // Releasing either modifier ends the gesture.
        if (_active)
        {
            _active = false;
            HotkeyFired?.Invoke(new HotkeyEvent(HotkeyEventKind.Up, WorkflowType.Transcription));
        }
        else
        {
            CancelPendingTimer();
        }

        if (!_ctrl && !_shift)
        {
            _otherKeyDown = false;
        }
    }

    private void Disqualify()
    {
        CancelPendingTimer();
        if (_active)
        {
            _active = false;
            HotkeyFired?.Invoke(new HotkeyEvent(HotkeyEventKind.Cancel, WorkflowType.Transcription));
        }
    }

    private void CancelPendingTimer()
    {
        _pendingTimer?.Dispose();
        _pendingTimer = null;
    }

    public void Dispose() => Stop();
}
