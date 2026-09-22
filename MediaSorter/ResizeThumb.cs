using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MediaSorter;

public sealed class ResizeThumb : Grid
{
    public event EventHandler<DragDeltaEventArgs>? DragDelta;
    public event EventHandler<DragCompletedEventArgs>? DragCompleted;
    public ResizeThumb(InputSystemCursorShape shape)
    {
        ProtectedCursor = InputSystemCursor.Create(shape);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        MinWidth = 0; MinHeight = 0;
        var thumb = new Thumb { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, MinWidth = 0, MinHeight = 0 };
        thumb.Template = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Grid Background='Transparent'><Border Background='#808090' Width='4' Height='4' CornerRadius='2' Opacity='.6'/></Grid></ControlTemplate>");
        thumb.DragDelta += (sender, e) => DragDelta?.Invoke(this, e);
        thumb.DragCompleted += (sender, e) => DragCompleted?.Invoke(this, e);
        Children.Add(thumb);
    }
}
