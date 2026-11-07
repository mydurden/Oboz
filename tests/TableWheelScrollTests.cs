using System.Windows;
using Xunit;

namespace Obozny.Tests;

public class TableWheelScrollTests
{
    [Fact]
    public void ComputeDelta_ItemMode_HugeGesture_DoesNotJumpHundredsOfRows()
    {
        // Типовий «стрибок у кінець»: піксельна дельта (~сотні) на item-offset.
        var delta = TableWheelScroll.ComputeVerticalScrollDelta(mouseDelta: -4800, itemBasedScrolling: true);
        Assert.InRange(Math.Abs(delta), 0.1, 20); // ≤ MaxNotches(3) × lines(~3) ≈ 9
    }

    [Fact]
    public void ComputeDelta_PixelMode_HugeGesture_CappedNotches()
    {
        var delta = TableWheelScroll.ComputeVerticalScrollDelta(mouseDelta: -4800, itemBasedScrolling: false);
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0) { Assert.Equal(0, delta); return; }
        if (lines < 0) lines = 3;
        // 3 notches × lines × 44
        Assert.Equal(3 * lines * 44, Math.Abs(delta), 0.01);
    }

    [Fact]
    public void ComputeDelta_OneNotch_Down_PositiveOffset()
    {
        var item = TableWheelScroll.ComputeVerticalScrollDelta(mouseDelta: -120, itemBasedScrolling: true);
        var pixel = TableWheelScroll.ComputeVerticalScrollDelta(mouseDelta: -120, itemBasedScrolling: false);
        Assert.True(item > 0);
        Assert.True(pixel > 0);
        Assert.True(pixel > item); // пікселі = рядки × 44
    }

    [Fact]
    public void ComputeDelta_ItemVsPixel_SameNotches_RatioIsLineHeight()
    {
        var item = TableWheelScroll.ComputeVerticalScrollDelta(-120, itemBasedScrolling: true);
        var pixel = TableWheelScroll.ComputeVerticalScrollDelta(-120, itemBasedScrolling: false);
        if (item == 0) return;
        Assert.Equal(44, pixel / item, 0.01);
    }
}
