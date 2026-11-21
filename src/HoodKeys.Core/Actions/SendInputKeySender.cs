using System.ComponentModel;
using System.Runtime.InteropServices;
using HoodKeys.Core.Keys;

namespace HoodKeys.Core.Actions;

/// <summary>Sends keys with the Win32 SendInput call. Input goes to the foreground window.</summary>
public sealed class SendInputKeySender : IKeySender
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;

    public void Send(KeyCombo combo)
    {
        var steps = KeyInputPlan.Build(combo);
        var inputs = new Input[steps.Count];
        for (var i = 0; i < inputs.Length; i++)
        {
            var flags = (steps[i].Up ? KeyEventKeyUp : 0) | (IsExtended(steps[i].VirtualKey) ? KeyEventExtendedKey : 0);
            inputs[i] = new Input
            {
                Type = InputKeyboard,
                Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = steps[i].VirtualKey, Flags = flags } },
            };
        }

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SendInput sent {sent} of {inputs.Length} events");
        }
    }

    // Navigation keys must be flagged extended, or the numpad variants are sent instead.
    private static bool IsExtended(ushort virtualKey) =>
        virtualKey is (>= 0x21 and <= 0x28) or 0x2D or 0x2E or KeyNames.Win;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
}
