namespace Lertaro.Plugins.AudioDeviceSelector.CoreAudio;

internal static class AudioDeviceDisplayFormatter
{
    internal static (string Title, string Description) Format(string friendlyName, AudioDeviceDisplayMode mode)
    {
        if (mode == AudioDeviceDisplayMode.FriendlyName) return (friendlyName, string.Empty);
        var (deviceName, deviceDescription) = SplitFriendlyName(friendlyName);
        return mode switch
        {
            AudioDeviceDisplayMode.DeviceName => (deviceName, deviceDescription),
            AudioDeviceDisplayMode.DeviceDescription when deviceDescription.Length > 0 => (deviceDescription, deviceName),
            _ => (friendlyName, string.Empty)
        };
    }

    internal static (string Name, string Description) SplitFriendlyName(string friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
            return (friendlyName, string.Empty);

        var closingParenthesis = friendlyName.Length - 1;
        var closing = friendlyName[closingParenthesis];
        if (closing is not (')' or '）')) return (friendlyName, string.Empty);
        var opening = closing == ')' ? '(' : '（';
        var depth = 1;
        var openingParenthesis = closingParenthesis - 1;
        for (; openingParenthesis > 0; openingParenthesis--)
        {
            var character = friendlyName[openingParenthesis];
            if (character == closing) depth++;
            else if (character == opening && --depth == 0) break;
        }
        if (openingParenthesis <= 0) return (friendlyName, string.Empty);

        var name = friendlyName[..openingParenthesis].TrimEnd();
        var description = friendlyName[(openingParenthesis + 1)..closingParenthesis].Trim();
        return string.IsNullOrEmpty(name) || string.IsNullOrEmpty(description)
            ? (friendlyName, string.Empty)
            : (name, description);
    }
}
