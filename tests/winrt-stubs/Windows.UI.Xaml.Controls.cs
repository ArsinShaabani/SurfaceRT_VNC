using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Windows.UI.Xaml.Controls
{
    public class Control : Windows.UI.Xaml.FrameworkElement
    {
        public static Windows.UI.Xaml.DependencyProperty BackgroundProperty { get { return null; } }
        public Windows.UI.Xaml.Media.Brush Background { get; set; }
        public object Content { get; set; }
        public double FontSize { get; set; }
    }

    public class ContentControl : Control
    {
    }

    public class Panel : Windows.UI.Xaml.FrameworkElement
    {
    }

    public enum Orientation { Horizontal, Vertical }

    public class RowDefinition : Windows.UI.Xaml.DependencyObject
    {
        public Windows.UI.Xaml.GridLength Height { get; set; }
    }

    public class ColumnDefinition : Windows.UI.Xaml.DependencyObject
    {
        public Windows.UI.Xaml.GridLength Width { get; set; }
    }

    public class RowDefinitionCollection : List<RowDefinition>
    {
    }

    public class ColumnDefinitionCollection : List<ColumnDefinition>
    {
    }

    public class Grid : Panel
    {
        public RowDefinitionCollection RowDefinitions { get { return null; } }
        public ColumnDefinitionCollection ColumnDefinitions { get { return null; } }
    }

    public class StackPanel : Panel
    {
        public Orientation Orientation { get; set; }
    }

    public class Canvas : Panel
    {
        public static void SetLeft(Windows.UI.Xaml.UIElement element, double length) { }
        public static void SetTop(Windows.UI.Xaml.UIElement element, double length) { }
    }

    public class Image : Windows.UI.Xaml.FrameworkElement
    {
        public Windows.UI.Xaml.Media.ImageSource Source { get; set; }
        public Windows.UI.Xaml.Media.Stretch Stretch { get; set; }
    }

    public class TextBlock : Windows.UI.Xaml.FrameworkElement
    {
        public string Text { get; set; }
        public Windows.UI.Xaml.TextWrapping TextWrapping { get; set; }
    }

    public enum ScrollBarVisibility { Disabled, Auto, Hidden, Visible }

    public enum ScrollMode { Disabled, Enabled, Auto }

    public class ScrollViewer : ContentControl
    {
        public ScrollBarVisibility HorizontalScrollBarVisibility { get; set; }
        public ScrollBarVisibility VerticalScrollBarVisibility { get; set; }
        public ScrollMode HorizontalScrollMode { get; set; }
        public ScrollMode VerticalScrollMode { get; set; }
    }

    public class Border : Windows.UI.Xaml.FrameworkElement
    {
        public Windows.UI.Xaml.CornerRadius CornerRadius { get; set; }
        public Windows.UI.Xaml.Media.Brush BorderBrush { get; set; }
        public Windows.UI.Xaml.Thickness BorderThickness { get; set; }
        public Windows.UI.Xaml.UIElement Child { get; set; }
    }

    public class Button : Control
    {
        public event Windows.UI.Xaml.RoutedEventHandler Click;
    }

    public class TextChangedEventArgs : Windows.UI.Xaml.RoutedEventArgs
    {
    }

    public class TextBox : Control
    {
        public string Text { get; set; }
        public string PlaceholderText { get; set; }
        public bool AcceptsReturn { get; set; }
        public Windows.UI.Xaml.TextWrapping TextWrapping { get; set; }
        public Windows.UI.Xaml.FocusState FocusState { get { return Windows.UI.Xaml.FocusState.Unfocused; } }
        public bool Focus(Windows.UI.Xaml.FocusState value) { return true; }
        public event EventHandler<TextChangedEventArgs> TextChanged;
    }

    public class PasswordBox : Control
    {
        public string Password { get; set; }
    }

    public class CheckBox : Control
    {
        public bool? IsChecked { get; set; }
    }

    public class Page : Windows.UI.Xaml.FrameworkElement
    {
        public Windows.UI.Xaml.Media.Brush Background { get; set; }
    }

    public enum ContentDialogResult { None, Primary, Secondary }

    public enum ContentDialogButton { None, Primary, Secondary, Close }

    public class ContentDialog : ContentControl
    {
        public object Title { get; set; }
        public string PrimaryButtonText { get; set; }
        public string SecondaryButtonText { get; set; }
        public string CloseButtonText { get; set; }
        public ContentDialogButton DefaultButton { get; set; }
        public Task<ContentDialogResult> ShowAsync() { return null; }
    }
}
