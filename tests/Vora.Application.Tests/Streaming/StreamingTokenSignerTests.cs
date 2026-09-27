using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Auth;
using Vora.Application.Streaming;

namespace Vora.Application.Tests.Streaming;

public class StreamingTokenSignerTests
{
    private static StreamingTokenSigner NewSigner(string secret = "test-secret-key-must-be-long-enough-for-hmac")
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test",
            Audience = "test",
            SecretKey = secret
        });
        return new StreamingTokenSigner(options, NullLogger<StreamingTokenSigner>.Instance);
    }

    [Fact]
    public void TryVerify_round_trips_signed_payload()
    {
        var signer = NewSigner();
        var token = signer.Sign("hls", "media-123", TimeSpan.FromMinutes(5));

        var verified = signer.TryVerify(token, "hls", out var payload);

        verified.Should().BeTrue();
        payload.Should().Be("media-123");
    }

    [Fact]
    public void TryVerify_rejects_wrong_scope()
    {
        var signer = NewSigner();
        var token = signer.Sign("hls", "media-123", TimeSpan.FromMinutes(5));

        var verified = signer.TryVerify(token, "dvr", out var payload);

        verified.Should().BeFalse();
        payload.Should().BeEmpty();
    }

    [Fact]
    public void TryVerify_rejects_token_signed_with_different_secret()
    {
        var signerA = NewSigner("secret-A-with-enough-length-for-hmac-key-padding");
        var signerB = NewSigner("secret-B-which-differs-from-the-other-on-purpose");

        var token = signerA.Sign("hls", "media-123", TimeSpan.FromMinutes(5));

        signerB.TryVerify(token, "hls", out _).Should().BeFalse();
    }

    [Fact]
    public void TryVerify_rejects_tampered_payload()
    {
        var signer = NewSigner();
        var token = signer.Sign("hls", "media-123", TimeSpan.FromMinutes(5));

        var parts = token.Split('.');
        var tampered = parts[0] + "X" + "." + parts[1];

        signer.TryVerify(tampered, "hls", out _).Should().BeFalse();
    }

    [Fact]
    public void TryVerify_rejects_tampered_signature()
    {
        var signer = NewSigner();
        var token = signer.Sign("hls", "media-123", TimeSpan.FromMinutes(5));

        // Flip a byte near the START of the signature — the last base64url char
        // can have padding bits that don't affect the decoded value, so flipping
        // it isn't always a real change. The first chars always encode meaningful bits.
        var parts = token.Split('.');
        var firstChar = parts[1][0];
        var swap = firstChar == 'A' ? 'B' : 'A';
        var tampered = parts[0] + "." + swap + parts[1][1..];

        signer.TryVerify(tampered, "hls", out _).Should().BeFalse();
    }

    [Fact]
    public void TryVerify_rejects_expired_token()
    {
        var signer = NewSigner();

        var token = signer.Sign("hls", "media-123", TimeSpan.FromSeconds(-1));

        signer.TryVerify(token, "hls", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not.a.token.too.many.dots")]
    [InlineData("missing-dot")]
    [InlineData("two..dots")]
    public void TryVerify_rejects_malformed_tokens(string token)
    {
        var signer = NewSigner();

        signer.TryVerify(token, "hls", out _).Should().BeFalse();
    }

    [Fact]
    public void TryVerify_handles_payloads_containing_pipe_characters()
    {
        var signer = NewSigner();
        var token = signer.Sign("hls", "media-id|extra|stuff", TimeSpan.FromMinutes(5));

        signer.TryVerify(token, "hls", out var payload).Should().BeTrue();
        payload.Should().Be("media-id|extra|stuff");
    }
}
