using SmartInput.Infrastructure.RecoveredRules;

namespace SmartInput.Core.Tests;

public sealed class RecoveredBoundaryPreparationTests
{
    [Theory]
    [InlineData(" ghbdtn ", "ghbdtn", RecoveredBoundaryCallbackBranch.CallbackA)]
    [InlineData("\tghbdtn", "ghbdtn", RecoveredBoundaryCallbackBranch.CallbackB)]
    [InlineData("helo-", "helo-", RecoveredBoundaryCallbackBranch.CallbackA)]
    [InlineData("\r\nруддщ\t", "руддщ", RecoveredBoundaryCallbackBranch.CallbackA)]
    [InlineData("ё", "ё", RecoveredBoundaryCallbackBranch.CallbackB)]
    public void ConfirmedTrimAndCallbackSelection_IsReproduced(
        string source,
        string expectedTrimmed,
        RecoveredBoundaryCallbackBranch expectedBranch)
    {
        var result = RecoveredBoundaryPreparation.Analyze(System.Text.Encoding.UTF8.GetBytes(source));

        Assert.Equal(RecoveredBoundaryPreparationStatus.PreparedKnownWhitespace, result.Status);
        Assert.Equal(expectedBranch, result.CallbackBranch);
        Assert.Equal(
            expectedBranch == RecoveredBoundaryCallbackBranch.CallbackA ? 0x81D538 : 0x81D53E,
            result.DescriptorRva);
        Assert.True(result.FinalTransformKnown);
        Assert.Equal(
            expectedBranch == RecoveredBoundaryCallbackBranch.CallbackA
                ? new byte[] { 0x01, 0x02, 0xC0, 0x01, 0x03, 0x00 }
                : new byte[] { 0x01, 0x02, 0xC0, 0x00 },
            result.CopyDescriptorBytes());
        Assert.Equal(expectedTrimmed, System.Text.Encoding.UTF8.GetString(result.CopyTrimmedBytes()));
        var expectedMatcherBytes = System.Text.Encoding.UTF8.GetBytes(expectedTrimmed);
        expectedMatcherBytes = expectedBranch == RecoveredBoundaryCallbackBranch.CallbackA
            ? [0x02, .. expectedMatcherBytes, 0x03]
            : [0x02, .. expectedMatcherBytes];
        Assert.Equal(expectedMatcherBytes, result.CopyFinalMatcherBytes());
    }

    [Fact]
    public void InvalidUtf8_IsRejectedWithoutProducingMatcherBytes()
    {
        var result = RecoveredBoundaryPreparation.Analyze([0xC3, 0x28]);

        Assert.Equal(RecoveredBoundaryPreparationStatus.InvalidUtf8, result.Status);
        Assert.Empty(result.CopyTrimmedBytes());
    }

    [Fact]
    public void NonZeroMode_DoesNotUseModeZeroBoundaryCallbackRule()
    {
        var result = RecoveredBoundaryPreparation.Analyze(
            System.Text.Encoding.UTF8.GetBytes("word "),
            mode: 1);

        Assert.Equal(RecoveredBoundaryCallbackBranch.CallbackB, result.CallbackBranch);
        Assert.Equal("word", System.Text.Encoding.UTF8.GetString(result.CopyTrimmedBytes()));
    }

    [Fact]
    public void NativeLayoutsExposeOnlyConfirmedOffsets()
    {
        var record = new byte[RecoveredMatchRecordLayout.Size];
        BitConverter.GetBytes(123u).CopyTo(record, RecoveredMatchRecordLayout.TableIndexOffset);

        Assert.True(RecoveredMatchRecordLayout.TryReadTableIndex(record, out var index));
        Assert.Equal(123u, index);
        Assert.False(RecoveredMatchRecordLayout.TryReadTableIndex(new byte[31], out _));
        Assert.Equal(24, RecoveredTransformOutputLayout.Size);
        Assert.Equal(0x10, RecoveredTransformOutputLayout.LengthOffset);
    }
}
