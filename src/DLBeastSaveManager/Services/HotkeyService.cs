using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DLBeastSaveManager.Models;

namespace DLBeastSaveManager.Services;

public enum HotkeyAction
{
    BackupNow,
    PinLatest
}

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [Flags]
    private enum NativeModifiers : uint
    {
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
        NoRepeat = 0x4000
    }

    private readonly Dictionary<int, HotkeyAction> _registered = new();
    private HwndSource? _source;
    private IntPtr _handle = IntPtr.Zero;
    private int _nextId = 0xB001;

    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public void Attach(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _handle = helper.Handle != IntPtr.Zero ? helper.Handle : helper.EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
    }

    public IReadOnlyList<string> Apply(AppSettings settings)
    {
        UnregisterAll();
        var problems = new List<string>();

        if (!settings.HotkeysEnabled || _handle == IntPtr.Zero) return problems;

        TryRegister(HotkeyAction.BackupNow, settings.BackupNowHotkey, "Backup now", problems);
        TryRegister(HotkeyAction.PinLatest, settings.PinLatestHotkey, "Pin latest", problems);

        return problems;
    }

    private void TryRegister(HotkeyAction action, HotkeyBinding binding, string name, List<string> problems)
    {
        if (!binding.IsBound) return;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(binding.Key);
        if (vk == 0)
        {
            problems.Add($"{name}: {binding.Describe()} is not a usable key.");
            return;
        }

        var id = _nextId++;
        if (RegisterHotKey(_handle, id, ToNative(binding.Modifiers), vk))
        {
            _registered[id] = action;
            return;
        }

        var error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
        problems.Add($"{name}: could not bind {binding.Describe()} - {error}");
    }

    private static uint ToNative(ModifierKeys modifiers)
    {
        var native = NativeModifiers.NoRepeat;
        if (modifiers.HasFlag(ModifierKeys.Alt)) native |= NativeModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Control)) native |= NativeModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Shift)) native |= NativeModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) native |= NativeModifiers.Win;
        return (uint)native;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey) return IntPtr.Zero;
        if (!_registered.TryGetValue(wParam.ToInt32(), out var action)) return IntPtr.Zero;

        handled = true;
        HotkeyPressed?.Invoke(this, action);
        return IntPtr.Zero;
    }

    private void UnregisterAll()
    {
        if (_handle == IntPtr.Zero) return;
        foreach (var id in _registered.Keys) UnregisterHotKey(_handle, id);
        _registered.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
