using System.Net;
using Vora.Application.Net;

namespace Vora.Application.Tests.Net;

// Adding a poster by URL failed for any host that answers with a redirect or
// declines to name the content type. Redirects were refused outright because
// following them automatically would have skipped the private-address check
// that is the whole SSRF guard; they are now followed by hand, re-checking
// every hop, and the bytes are inspected rather than the header trusted.
public class SafeImageDownloaderTests
{
    private static byte[] WithHeader(params byte[] header)
    {
        var bytes = new byte[32];
        header.CopyTo(bytes, 0);
        return bytes;
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.SeeOther)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public void Every_redirect_status_is_recognised(HttpStatusCode status)
    {
        SafeImageDownloader.IsRedirect(status).Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotModified)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public void A_non_redirect_is_not_followed(HttpStatusCode status)
    {
        SafeImageDownloader.IsRedirect(status).Should().BeFalse();
    }

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("binary/octet-stream")]
    [InlineData("Application/Octet-Stream")]
    public void An_unhelpful_content_type_is_treated_as_unknown_not_as_a_refusal(string contentType)
    {
        SafeImageDownloader.IsAmbiguousContentType(contentType).Should().BeTrue();
    }

    // Something that names a specific non-image type is a real answer and is
    // still refused before the body is read.
    [Theory]
    [InlineData("text/html")]
    [InlineData("application/json")]
    [InlineData("video/mp4")]
    public void A_specific_non_image_type_is_still_a_refusal(string contentType)
    {
        SafeImageDownloader.IsAmbiguousContentType(contentType).Should().BeFalse();
    }

    [Fact]
    public void A_jpeg_is_recognised_by_its_bytes()
    {
        SafeImageDownloader.LooksLikeImage(WithHeader(0xFF, 0xD8, 0xFF, 0xE0)).Should().BeTrue();
    }

    [Fact]
    public void A_png_is_recognised_by_its_bytes()
    {
        SafeImageDownloader.LooksLikeImage(WithHeader(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)).Should().BeTrue();
    }

    [Fact]
    public void A_gif_is_recognised_by_its_bytes()
    {
        SafeImageDownloader.LooksLikeImage(WithHeader(0x47, 0x49, 0x46, 0x38, 0x39, 0x61)).Should().BeTrue();
    }

    [Fact]
    public void A_bmp_is_recognised_by_its_bytes()
    {
        SafeImageDownloader.LooksLikeImage(WithHeader(0x42, 0x4D)).Should().BeTrue();
    }

    [Fact]
    public void A_webp_is_recognised_by_its_riff_container()
    {
        var bytes = WithHeader(0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50);
        SafeImageDownloader.LooksLikeImage(bytes).Should().BeTrue();
    }

    [Fact]
    public void An_avif_is_recognised_by_its_ftyp_box()
    {
        var bytes = WithHeader(0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69, 0x66);
        SafeImageDownloader.LooksLikeImage(bytes).Should().BeTrue();
    }

    // A RIFF that is not a WebP is a WAV, and a host mislabelling one as an
    // image must not get it written into the artwork store.
    [Fact]
    public void A_riff_that_is_not_a_webp_is_rejected()
    {
        var wav = WithHeader(0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45);
        SafeImageDownloader.LooksLikeImage(wav).Should().BeFalse();
    }

    [Fact]
    public void An_html_error_page_labelled_as_an_image_is_rejected()
    {
        var html = System.Text.Encoding.ASCII.GetBytes("<!DOCTYPE html><html><head><title>403</title>");
        SafeImageDownloader.LooksLikeImage(html).Should().BeFalse();
    }

    [Fact]
    public void A_json_error_body_is_rejected()
    {
        var json = System.Text.Encoding.ASCII.GetBytes("{\"error\":\"rate limit exceeded, try again later\"}");
        SafeImageDownloader.LooksLikeImage(json).Should().BeFalse();
    }

    // Short reads must not index past the end of the buffer while sniffing.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(11)]
    public void A_body_too_short_to_identify_is_rejected_without_throwing(int length)
    {
        SafeImageDownloader.LooksLikeImage(new byte[length]).Should().BeFalse();
    }
}
