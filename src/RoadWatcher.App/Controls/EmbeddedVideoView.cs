using System.Runtime.InteropServices;
using Avalonia.Platform;
using LibVLCSharp.Avalonia;

namespace RoadWatcher.App.Controls;

/// <summary>
/// Keeps LibVLC attached when Avalonia creates the native child window after the
/// control has already been initialized (for example, after IsVisible changes).
/// Rendering and playback remain provided by LibVLCSharp's mature VideoView.
/// </summary>
public sealed class EmbeddedVideoView : VideoView
{
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        AttachMediaPlayer(handle);
        return handle;
    }

    private void AttachMediaPlayer(IPlatformHandle handle)
    {
        if (MediaPlayer is null)
        {
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            MediaPlayer.Hwnd = handle.Handle;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            MediaPlayer.XWindow = unchecked((uint)handle.Handle.ToInt64());
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            MediaPlayer.NsObject = handle.Handle;
        }
    }
}
