using System.Windows;
using System.Windows.Controls;

namespace SpotifyWPF.View.Extension
{
    public static class TextBoxExtensions
    {
        public static readonly DependencyProperty AutoScrollToEndProperty =
            DependencyProperty.RegisterAttached(
                "AutoScrollToEnd",
                typeof(bool),
                typeof(TextBoxExtensions),
                new PropertyMetadata(false, OnAutoScrollToEndChanged));

        public static bool GetAutoScrollToEnd(DependencyObject obj)
        {
            return (bool)obj.GetValue(AutoScrollToEndProperty);
        }

        public static void SetAutoScrollToEnd(DependencyObject obj, bool value)
        {
            obj.SetValue(AutoScrollToEndProperty, value);
        }

        private static void OnAutoScrollToEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tb = d as TextBox;
            if (tb == null) return;

            if ((bool)e.NewValue)
            {
                tb.TextChanged += TextBoxOnTextChanged;
                // Allinea subito in fondo al valore iniziale
                tb.CaretIndex = tb.Text?.Length ?? 0;
                tb.ScrollToEnd();
            }
            else
            {
                tb.TextChanged -= TextBoxOnTextChanged;
            }
        }

        private static void TextBoxOnTextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;

            // Mantiene il caret alla fine e scrolla in fondo a ogni nuova riga
            tb.CaretIndex = tb.Text?.Length ?? 0;
            tb.ScrollToEnd();
        }
    }
}
