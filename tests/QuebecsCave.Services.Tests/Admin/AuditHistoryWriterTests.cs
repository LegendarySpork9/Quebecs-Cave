using System.Text.Json;
using FluentAssertions;
using Moq;
using QuebecsCave.Core.Audit;
using QuebecsCave.Services.Admin;
using QuebecsCave.Services.Tests.TestUtilities;

namespace QuebecsCave.Services.Tests.Admin;

[TestClass]
public sealed class AuditHistoryWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 30, 12, 0, 0, TimeSpan.Zero);

    private Mock<IAuditHistoryRepository> _audit = null!;
    private Mock<IDeletionRepository> _deletions = null!;
    private FakeClock _clock = null!;
    private AuditHistoryWriter _sut = null!;

    [TestInitialize]
    public void SetUp()
    {
        _audit = new Mock<IAuditHistoryRepository>();
        _deletions = new Mock<IDeletionRepository>();
        _clock = new FakeClock(Now);
        _sut = new AuditHistoryWriter(_audit.Object, _deletions.Object, _clock);
    }

    [TestMethod]
    public async Task RecordCreate_WritesAfterOnlyDiff()
    {
        string? capturedDiff = null;
        _audit.Setup(a => a.InsertAsync(
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<int?, string, int, string, string?, DateTimeOffset, CancellationToken>(
                (_, _, _, _, diff, _, _) => capturedDiff = diff)
            .ReturnsAsync(1);

        await _sut.RecordCreateAsync("Stream", 7, new { Title = "Hi" }, userId: 42, CancellationToken.None);

        _audit.Verify(a => a.InsertAsync(42, "Stream", 7, AuditAction.Create, It.IsAny<string?>(), Now, It.IsAny<CancellationToken>()), Times.Once);
        capturedDiff.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedDiff!);
        doc.RootElement.TryGetProperty("after", out var after).Should().BeTrue();
        after.GetProperty("Title").GetString().Should().Be("Hi");
        doc.RootElement.TryGetProperty("before", out _).Should().BeFalse();
    }

    [TestMethod]
    public async Task RecordUpdate_WritesBeforeAndAfter()
    {
        string? capturedDiff = null;
        _audit.Setup(a => a.InsertAsync(
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<int?, string, int, string, string?, DateTimeOffset, CancellationToken>(
                (_, _, _, _, diff, _, _) => capturedDiff = diff)
            .ReturnsAsync(1);

        await _sut.RecordUpdateAsync(
            "Game", 3,
            before: new { Name = "Old" },
            after: new { Name = "New" },
            userId: 42,
            CancellationToken.None);

        _audit.Verify(a => a.InsertAsync(42, "Game", 3, AuditAction.Update, It.IsAny<string?>(), Now, It.IsAny<CancellationToken>()), Times.Once);
        using var doc = JsonDocument.Parse(capturedDiff!);
        doc.RootElement.GetProperty("before").GetProperty("Name").GetString().Should().Be("Old");
        doc.RootElement.GetProperty("after").GetProperty("Name").GetString().Should().Be("New");
    }

    [TestMethod]
    public async Task RecordDelete_WritesAuditWithNullDiffAndDeletionRow()
    {
        _audit.Setup(a => a.InsertAsync(
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _deletions.Setup(d => d.InsertAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _sut.RecordDeleteAsync("Stream", 5, userId: 42, CancellationToken.None);

        _audit.Verify(a => a.InsertAsync(42, "Stream", 5, AuditAction.Delete, null, Now, It.IsAny<CancellationToken>()), Times.Once);
        _deletions.Verify(d => d.InsertAsync("Stream", 5, 42, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RecordCreate_AnonymousUser_PassesNullUserId()
    {
        _audit.Setup(a => a.InsertAsync(
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _sut.RecordCreateAsync("Stream", 7, new { Title = "Hi" }, userId: null, CancellationToken.None);

        _audit.Verify(a => a.InsertAsync(null, "Stream", 7, AuditAction.Create, It.IsAny<string?>(), Now, It.IsAny<CancellationToken>()), Times.Once);
    }
}
