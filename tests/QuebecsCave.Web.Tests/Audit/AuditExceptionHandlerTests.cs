using FluentAssertions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QuebecsCave.Core.Audit;
using QuebecsCave.Core.Domain;
using QuebecsCave.Core.Time;
using QuebecsCave.Web.Audit;

namespace QuebecsCave.Web.Tests.Audit;

[TestClass]
public sealed class AuditExceptionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeClock : IClock { public DateTimeOffset Now { get; init; } }

    private sealed class FakePathFeature : IExceptionHandlerPathFeature
    {
        public Exception Error { get; set; } = null!;
        public string Path { get; set; } = "";
        public string? RouteValues => null;
        public Endpoint? Endpoint => null;
    }

    private static AuditExceptionHandler MakeHandler(
        Mock<IErrorLogger>? errorLogger = null,
        Mock<IErrorStatusLookup>? statusLookup = null)
    {
        errorLogger ??= new Mock<IErrorLogger>();
        if (statusLookup is null)
        {
            statusLookup = new Mock<IErrorStatusLookup>();
            statusLookup.Setup(s => s.GetIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(7);
        }
        return new AuditExceptionHandler(
            errorLogger.Object,
            statusLookup.Object,
            new FakeClock { Now = Now },
            NullLogger<AuditExceptionHandler>.Instance);
    }

    private static HttpContext MakeContext(string requestPath, string? exceptionPath = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = requestPath;
        if (exceptionPath is not null)
        {
            ctx.Features.Set<IExceptionHandlerPathFeature>(new FakePathFeature
            {
                Error = new InvalidOperationException("boom"),
                Path = exceptionPath,
            });
        }
        return ctx;
    }

    [TestMethod]
    public async Task TryHandle_AlwaysReturnsFalseSoDefaultHandlerRuns()
    {
        var handler = MakeHandler();
        var ctx = MakeContext("/api/streams");

        var handled = await handler.TryHandleAsync(ctx, new Exception("x"), CancellationToken.None);

        handled.Should().BeFalse();
    }

    [TestMethod]
    public async Task TryHandle_ApiPath_TaggedAsApiSource()
    {
        var logger = new Mock<IErrorLogger>();
        var handler = MakeHandler(logger);
        var ctx = MakeContext("/api/streams", exceptionPath: "/api/streams");

        await handler.TryHandleAsync(ctx, new Exception("oops"), CancellationToken.None);

        logger.Verify(l => l.Enqueue(It.Is<ErrorLogEntry>(e => e.Source == AuditSource.Api)), Times.Once);
    }

    [TestMethod]
    public async Task TryHandle_NonApiPath_TaggedAsWebsiteSource()
    {
        var logger = new Mock<IErrorLogger>();
        var handler = MakeHandler(logger);
        var ctx = MakeContext("/streams/1", exceptionPath: "/streams/1");

        await handler.TryHandleAsync(ctx, new Exception("oops"), CancellationToken.None);

        logger.Verify(l => l.Enqueue(It.Is<ErrorLogEntry>(e => e.Source == AuditSource.Website)), Times.Once);
    }

    [TestMethod]
    public async Task TryHandle_PathRewrittenToError_StillReadsOriginalApiPathFromFeature()
    {
        // Regression for the Phase 4 bug: UseExceptionHandler rewrites Request.Path
        // to "/Error" before TryHandleAsync runs. The original /api/* path lives
        // on IExceptionHandlerPathFeature.Path — not on Request.Path. If the
        // handler regresses and reads Request.Path, this test fails because
        // "/Error" doesn't start with "/api/" and the source becomes Website.
        var logger = new Mock<IErrorLogger>();
        var handler = MakeHandler(logger);
        var ctx = MakeContext(requestPath: "/Error", exceptionPath: "/api/streams");

        await handler.TryHandleAsync(ctx, new Exception("oops"), CancellationToken.None);

        logger.Verify(l => l.Enqueue(It.Is<ErrorLogEntry>(e => e.Source == AuditSource.Api)), Times.Once);
    }

    [TestMethod]
    public async Task TryHandle_NoFeature_FallsBackToRequestPath()
    {
        var logger = new Mock<IErrorLogger>();
        var handler = MakeHandler(logger);
        var ctx = MakeContext("/api/games"); // no feature

        await handler.TryHandleAsync(ctx, new Exception("oops"), CancellationToken.None);

        logger.Verify(l => l.Enqueue(It.Is<ErrorLogEntry>(e => e.Source == AuditSource.Api)), Times.Once);
    }

    [TestMethod]
    public async Task TryHandle_PopulatesLogEntryWithStatusAndClock()
    {
        var logger = new Mock<IErrorLogger>();
        var statusLookup = new Mock<IErrorStatusLookup>();
        statusLookup.Setup(s => s.GetIdAsync(ErrorStatusName.Open, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(99);

        var handler = MakeHandler(logger, statusLookup);
        var ctx = MakeContext("/api/streams", exceptionPath: "/api/streams");
        var ex = new InvalidOperationException("boom");

        await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        logger.Verify(l => l.Enqueue(It.Is<ErrorLogEntry>(e =>
            e.StatusId == 99 &&
            e.OccurredAt == Now &&
            e.ExceptionType == typeof(InvalidOperationException).FullName &&
            e.Message == "boom")), Times.Once);
    }

    [TestMethod]
    public async Task TryHandle_LongMessage_TruncatedTo2000Chars()
    {
        var logger = new Mock<IErrorLogger>();
        var handler = MakeHandler(logger);
        var ctx = MakeContext("/api/streams", exceptionPath: "/api/streams");
        var ex = new Exception(new string('x', 5000));

        await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        logger.Verify(l => l.Enqueue(It.Is<ErrorLogEntry>(e => e.Message.Length == 2000)), Times.Once);
    }

    [TestMethod]
    public async Task TryHandle_LoggerThrows_ReturnsFalseAndSwallowsException()
    {
        var logger = new Mock<IErrorLogger>();
        logger.Setup(l => l.Enqueue(It.IsAny<ErrorLogEntry>())).Throws<InvalidOperationException>();
        var handler = MakeHandler(logger);
        var ctx = MakeContext("/api/streams");

        var handled = await handler.TryHandleAsync(ctx, new Exception("x"), CancellationToken.None);

        handled.Should().BeFalse();
    }
}
