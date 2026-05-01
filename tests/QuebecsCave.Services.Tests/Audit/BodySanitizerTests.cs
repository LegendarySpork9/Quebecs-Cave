using FluentAssertions;
using QuebecsCave.Services.Audit;

namespace QuebecsCave.Services.Tests.Audit;

[TestClass]
public sealed class BodySanitizerTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void Sanitize_NullOrEmpty_PassesThrough(string? input)
    {
        BodySanitizer.Sanitize(input).Should().Be(input);
    }

    [TestMethod]
    public void Sanitize_PlainBody_IsUnchanged()
    {
        const string body = "{\"title\":\"hello\",\"count\":42}";
        BodySanitizer.Sanitize(body).Should().Be(body);
    }

    [TestMethod]
    public void Sanitize_RedactsJsonField()
    {
        var input = "{\"clientSecret\":\"abc123\",\"title\":\"keep\"}";
        var output = BodySanitizer.Sanitize(input);
        output.Should().Contain("\"clientSecret\":\"***REDACTED***\"");
        output.Should().Contain("\"title\":\"keep\"");
        output.Should().NotContain("abc123");
    }

    [TestMethod]
    [DataRow("clientSecret")]
    [DataRow("client_secret")]
    [DataRow("accessToken")]
    [DataRow("access_token")]
    [DataRow("refreshToken")]
    [DataRow("refresh_token")]
    [DataRow("apiKey")]
    [DataRow("api_key")]
    [DataRow("password")]
    [DataRow("authorization")]
    [DataRow("auth")]
    [DataRow("x-api-key")]
    public void Sanitize_RedactsAllAllowlistedJsonFields(string field)
    {
        var input = $"{{\"{field}\":\"super-secret\"}}";
        var output = BodySanitizer.Sanitize(input);
        output.Should().NotContain("super-secret");
        output.Should().Contain("***REDACTED***");
    }

    [TestMethod]
    public void Sanitize_FieldNameIsCaseInsensitive()
    {
        var output = BodySanitizer.Sanitize("{\"PASSWORD\":\"hunter2\"}");
        output.Should().NotContain("hunter2");
        output.Should().Contain("***REDACTED***");
    }

    [TestMethod]
    public void Sanitize_RedactsEscapedJsonField()
    {
        // A JSON value that itself contains a stringified JSON object.
        var input = "{\"payload\":\"{\\\"clientSecret\\\":\\\"nested-secret\\\"}\"}";
        var output = BodySanitizer.Sanitize(input);
        output.Should().NotContain("nested-secret");
        output.Should().Contain("\\\"clientSecret\\\":\\\"***REDACTED***\\\"");
    }

    [TestMethod]
    public void Sanitize_RedactsQueryStringField()
    {
        var output = BodySanitizer.Sanitize("foo=bar&accessToken=abc123&baz=ok");
        output.Should().NotContain("abc123");
        output.Should().Contain("accessToken=***REDACTED***");
        output.Should().Contain("foo=bar");
        output.Should().Contain("baz=ok");
    }

    [TestMethod]
    public void Sanitize_LeavesUnknownFieldsAlone()
    {
        var input = "{\"sessionId\":\"keep-me\",\"title\":\"hi\"}";
        BodySanitizer.Sanitize(input).Should().Be(input);
    }

    [TestMethod]
    public void Sanitize_TruncatesLongBodies()
    {
        var input = new string('a', 5000);
        var output = BodySanitizer.Sanitize(input);
        output.Should().NotBeNull();
        output!.Should().EndWith("…[truncated]");
        output.Length.Should().Be(4 * 1024 + "…[truncated]".Length);
    }

    [TestMethod]
    public void Sanitize_BodyAtThresholdIsNotTruncated()
    {
        var input = new string('a', 4 * 1024);
        BodySanitizer.Sanitize(input).Should().Be(input);
    }

    [TestMethod]
    public void Sanitize_RedactsMultipleFieldsInSameBody()
    {
        var input = "{\"clientSecret\":\"s1\",\"accessToken\":\"s2\",\"title\":\"t\"}";
        var output = BodySanitizer.Sanitize(input);
        output.Should().NotContain("s1").And.NotContain("s2");
        output.Should().Contain("\"clientSecret\":\"***REDACTED***\"");
        output.Should().Contain("\"accessToken\":\"***REDACTED***\"");
        output.Should().Contain("\"title\":\"t\"");
    }
}
