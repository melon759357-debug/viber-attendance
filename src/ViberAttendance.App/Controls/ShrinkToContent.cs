using System.Windows;
using System.Windows.Controls;

namespace ViberAttendance.App.Controls;

/// <summary>
/// 子内容比可用高度矮时，只占内容高度；超出时限制在可用高度内并让子内容自己滚动。
/// </summary>
public sealed class ShrinkToContent : Decorator
{
    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null)
        {
            return new Size(0, 0);
        }

        var width = double.IsInfinity(constraint.Width) ? 0 : constraint.Width;
        Child.Measure(new Size(width, double.PositiveInfinity));
        var contentHeight = Child.DesiredSize.Height;
        if (double.IsInfinity(constraint.Height) || contentHeight <= constraint.Height)
        {
            return new Size(width, contentHeight);
        }

        Child.Measure(new Size(width, constraint.Height));
        return new Size(width, constraint.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is not null)
        {
            var height = Math.Min(Child.DesiredSize.Height, arrangeSize.Height);
            Child.Arrange(new Rect(0, 0, arrangeSize.Width, height));
        }

        return arrangeSize;
    }
}
