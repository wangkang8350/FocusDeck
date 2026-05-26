namespace FocusDeck;

public static class HotkeyParser
{
    public static (int Modifiers, int VirtualKey)? Parse(string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return null;
        }

        var modifiers = 0;
        int? key = null;
        foreach (var part in shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt":
                    modifiers |= 0x0001;
                    break;
                case "ctrl":
                case "control":
                    modifiers |= 0x0002;
                    break;
                case "shift":
                    modifiers |= 0x0004;
                    break;
                case "win":
                case "windows":
                    modifiers |= 0x0008;
                    break;
                default:
                    key = KeyToVirtualKey(part);
                    break;
            }
        }

        return key is null ? null : (modifiers, key.Value);
    }

    private static int? KeyToVirtualKey(string key)
    {
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                return c;
            }
        }

        if (key.StartsWith('F') && int.TryParse(key[1..], out var number) && number is >= 1 and <= 24)
        {
            return 0x70 + number - 1;
        }

        return key.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "enter" => 0x0D,
            "tab" => 0x09,
            "esc" or "escape" => 0x1B,
            _ => null
        };
    }
}
