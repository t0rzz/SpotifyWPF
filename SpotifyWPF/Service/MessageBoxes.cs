namespace SpotifyWPF.Service.MessageBoxes
{
    using System.Windows;
    using SpotifyWPF.View;
    public enum MessageBoxButton
    {
        OK = 0,
        OkCancel = 1,
        YesNoCancel = 3,
        YesNo = 4
    }

    public enum MessageBoxResult
    {
        None = 0,
        Ok = 1,
        Cancel = 2,
        Yes = 6,
        No = 7
    }

    public enum MessageBoxIcon
    {
        None = 0,
        Error = 16,
        Hand = 16,
        Stop = 16,
        Question = 32,
        Exclamation = 48,
        Warning = 48,
        Information = 64,
        Asterisk = 64
    }

    public interface IMessageBoxService
    {
        MessageBoxResult ShowMessageBox(string message, string caption, MessageBoxButton buttons, MessageBoxIcon icon);
    }

    public class MessageBoxService : IMessageBoxService
    {
        public MessageBoxResult ShowMessageBox(string message, string caption, MessageBoxButton buttons, MessageBoxIcon icon)
        {
            return (MessageBoxResult)System.Windows.MessageBox.Show(message, caption,
                (System.Windows.MessageBoxButton)buttons, (System.Windows.MessageBoxImage)icon);
        }
    }

    public interface IConfirmationDialogService
    {
        bool? ShowConfirmation(string title, string message, string confirmButtonText = "Yes", string cancelButtonText = "Cancel", bool showCancel = true);
    }

    public class ConfirmationDialogService : IConfirmationDialogService
    {
        public bool? ShowConfirmation(string title, string message, string confirmButtonText = "Yes", string cancelButtonText = "Cancel", bool showCancel = true)
        {
            var dialog = new System.Windows.Window
            {
                Title = title,
                Content = new ConfirmationDialog(),
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true
            };

            // Set owner to main window for proper centering
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null)
            {
                dialog.Owner = mainWindow;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            var viewModel = new ConfirmationDialogViewModel
            {
                Title = title,
                Message = message,
                ConfirmButtonText = confirmButtonText,
                CancelButtonText = cancelButtonText,
                CancelButtonVisibility = showCancel ? Visibility.Visible : Visibility.Collapsed
            };

            var dialogControl = (ConfirmationDialog)dialog.Content;
            dialogControl.DataContext = viewModel;

            // Set close action
            viewModel.CloseAction = () => dialog.Close();

            dialog.ShowDialog();

            return viewModel.Result;
        }
    }

    public interface IEditPlaylistDialogService
    {
        (bool? result, string name, bool isPublic) ShowEditPlaylistDialog(string currentName, bool currentIsPublic);
    }

    public class EditPlaylistDialogService : IEditPlaylistDialogService
    {
        public (bool? result, string name, bool isPublic) ShowEditPlaylistDialog(string currentName, bool currentIsPublic)
        {
            var dialog = new System.Windows.Window
            {
                Title = "Edit Playlist",
                Content = new EditPlaylistDialog(),
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true
            };

            // Set owner to main window for proper centering
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null)
            {
                dialog.Owner = mainWindow;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            var viewModel = new EditPlaylistDialogViewModel
            {
                PlaylistName = currentName,
                IsPublic = currentIsPublic
            };

            var dialogControl = (EditPlaylistDialog)dialog.Content;
            dialogControl.DataContext = viewModel;

            // Set close action
            viewModel.CloseAction = () => dialog.Close();

            dialog.ShowDialog();

            return (viewModel.Result, viewModel.PlaylistName, viewModel.IsPublic);
        }
    }
}
