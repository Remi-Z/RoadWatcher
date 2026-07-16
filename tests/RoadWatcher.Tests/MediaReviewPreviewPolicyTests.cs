using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class MediaReviewPreviewPolicyTests
{
    [Fact]
    public void GoPro_gx_and_gl_files_pair_when_duration_is_compatible()
    {
        var mediaId = Guid.NewGuid();

        var matches = MediaReviewPreviewPolicy.MatchSuppliedLrvs(
        [
            new MediaReviewVideoCandidate(mediaId, @"D:\ride\GX010123.MP4", TimeSpan.FromSeconds(61))
        ],
        [
            new MediaReviewPreviewCandidate(@"D:\ride\GL010123.LRV", TimeSpan.FromSeconds(60.5))
        ]);

        var match = Assert.Single(matches);
        Assert.Equal(mediaId, match.MediaSourceId);
        Assert.EndsWith("GL010123.LRV", match.PreviewPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Same_stem_preview_pairs_without_camera_specific_naming()
    {
        var mediaId = Guid.NewGuid();

        var matches = MediaReviewPreviewPolicy.MatchSuppliedLrvs(
        [
            new MediaReviewVideoCandidate(mediaId, "ride-front.mp4", TimeSpan.FromMinutes(2))
        ],
        [
            new MediaReviewPreviewCandidate("ride-front.lrv", TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(1)))
        ]);

        Assert.Equal(mediaId, Assert.Single(matches).MediaSourceId);
    }

    [Fact]
    public void Incompatible_duration_is_not_paired()
    {
        var matches = MediaReviewPreviewPolicy.MatchSuppliedLrvs(
        [
            new MediaReviewVideoCandidate(Guid.NewGuid(), "GX010123.mp4", TimeSpan.FromSeconds(60))
        ],
        [
            new MediaReviewPreviewCandidate("GL010123.lrv", TimeSpan.FromSeconds(68))
        ]);

        Assert.Empty(matches);
    }

    [Fact]
    public void Ambiguous_preview_is_not_guessed()
    {
        var matches = MediaReviewPreviewPolicy.MatchSuppliedLrvs(
        [
            new MediaReviewVideoCandidate(Guid.NewGuid(), "GX010123.mp4", TimeSpan.FromSeconds(60)),
            new MediaReviewVideoCandidate(Guid.NewGuid(), "GX010123.mov", TimeSpan.FromSeconds(60))
        ],
        [
            new MediaReviewPreviewCandidate("GL010123.lrv", TimeSpan.FromSeconds(60))
        ]);

        Assert.Empty(matches);
    }

    [Fact]
    public void Supplied_lrv_takes_precedence_over_generated_proxy()
    {
        var selection = MediaReviewPreviewPolicy.SelectPlaybackSource(
            "source.mp4",
            "source.lrv",
            "cache-proxy.mp4");

        Assert.Equal("source.lrv", selection.Path);
        Assert.Equal(MediaReviewPlaybackKind.SuppliedLrv, selection.Kind);
        Assert.True(selection.IsPreview);
    }

    [Fact]
    public void Proxy_then_original_are_used_when_no_lrv_is_available()
    {
        var proxy = MediaReviewPreviewPolicy.SelectPlaybackSource(
            "source.mp4",
            suppliedLrvPath: null,
            generatedProxyPath: "cache-proxy.mp4");
        var original = MediaReviewPreviewPolicy.SelectPlaybackSource(
            "source.mp4",
            suppliedLrvPath: null,
            generatedProxyPath: null);

        Assert.Equal(MediaReviewPlaybackKind.GeneratedProxy, proxy.Kind);
        Assert.Equal(MediaReviewPlaybackKind.Original, original.Kind);
        Assert.False(original.IsPreview);
    }

    [Theory]
    [InlineData("preview.lrv")]
    [InlineData("PREVIEW.LRV")]
    [InlineData("C:/ride/preview.LrV")]
    public void Lrv_extension_is_recognized_case_insensitively(string path) =>
        Assert.True(MediaReviewPreviewPolicy.IsSuppliedLrv(path));
}
