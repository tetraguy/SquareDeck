using System.IO;
using Windows.Media.Control;

namespace SquareDeck.Services;

public sealed record NowPlaying(
    string Title, string Artist, string App, bool IsPlaying,
    TimeSpan Position, TimeSpan Duration, byte[]? Thumbnail);

/// <summary>Reads and controls whatever is playing on the PC (Spotify, browsers, media players…).</summary>
public static class MediaService
{
    private static GlobalSystemMediaTransportControlsSessionManager? _manager;

    private static async Task<GlobalSystemMediaTransportControlsSession?> SessionAsync()
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return _manager.GetCurrentSession();
    }

    /// <summary>Null when nothing is playing.</summary>
    public static async Task<NowPlaying?> GetAsync(bool includeThumbnail)
    {
        try
        {
            var session = await SessionAsync();
            if (session == null) return null;

            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            var position = timeline.Position;
            if (playing && timeline.LastUpdatedTime > DateTimeOffset.MinValue.AddYears(1900))
                position += DateTimeOffset.Now - timeline.LastUpdatedTime;
            var duration = timeline.EndTime - timeline.StartTime;
            if (duration > TimeSpan.Zero && position > duration) position = duration;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;

            byte[]? thumbnail = null;
            if (includeThumbnail && props.Thumbnail != null)
            {
                try
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    using var input = stream.AsStreamForRead();
                    using var ms = new MemoryStream();
                    await input.CopyToAsync(ms);
                    thumbnail = ms.ToArray();
                }
                catch (Exception) { }
            }

            return new NowPlaying(props.Title ?? "", props.Artist ?? "", session.SourceAppUserModelId ?? "",
                playing, position, duration, thumbnail);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task PlayPauseAsync()
    {
        try { if (await SessionAsync() is { } s) await s.TryTogglePlayPauseAsync(); } catch (Exception) { }
    }

    public static async Task NextAsync()
    {
        try { if (await SessionAsync() is { } s) await s.TrySkipNextAsync(); } catch (Exception) { }
    }

    public static async Task PreviousAsync()
    {
        try { if (await SessionAsync() is { } s) await s.TrySkipPreviousAsync(); } catch (Exception) { }
    }

    /// <summary>Jumps forward or back. Falls back to the player's own rewind / fast-forward if it can't seek.</summary>
    public static async Task SeekAsync(TimeSpan delta)
    {
        try
        {
            var session = await SessionAsync();
            if (session == null) return;

            var now = await GetAsync(includeThumbnail: false);
            if (now != null && now.Duration > TimeSpan.Zero)
            {
                var target = now.Position + delta;
                if (target < TimeSpan.Zero) target = TimeSpan.Zero;
                if (target > now.Duration) target = now.Duration;
                if (await session.TryChangePlaybackPositionAsync(target.Ticks)) return;
            }

            if (delta < TimeSpan.Zero) await session.TryRewindAsync();
            else await session.TryFastForwardAsync();
        }
        catch (Exception) { }
    }
}
