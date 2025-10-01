using System.Windows;
using System.Windows.Controls;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;

namespace SpotifyWPF.View
{
    public partial class ConfirmationDialog : UserControl
    {
        public ConfirmationDialog()
        {
            InitializeComponent();
        }
    }

    public class ConfirmationDialogViewModel : ViewModelBase
    {
        private string _title = string.Empty;
        private string _message = string.Empty;
        private string _confirmButtonText = "Yes";
        private string _cancelButtonText = "Cancel";
        private Visibility _cancelButtonVisibility = Visibility.Visible;

        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                RaisePropertyChanged();
            }
        }

        public string Message
        {
            get => _message;
            set
            {
                _message = value;
                RaisePropertyChanged();
            }
        }

        public string ConfirmButtonText
        {
            get => _confirmButtonText;
            set
            {
                _confirmButtonText = value;
                RaisePropertyChanged();
            }
        }

        public string CancelButtonText
        {
            get => _cancelButtonText;
            set
            {
                _cancelButtonText = value;
                RaisePropertyChanged();
            }
        }

        public Visibility CancelButtonVisibility
        {
            get => _cancelButtonVisibility;
            set
            {
                _cancelButtonVisibility = value;
                RaisePropertyChanged();
            }
        }

        public RelayCommand ConfirmCommand { get; }
        public RelayCommand CancelCommand { get; }

        public Action? CloseAction { get; set; }

        public ConfirmationDialogViewModel(Action? closeAction = null)
        {
            CloseAction = closeAction;
            ConfirmCommand = new RelayCommand(() => 
            {
                Result = true;
                CloseAction?.Invoke();
            });
            CancelCommand = new RelayCommand(() => 
            {
                Result = false;
                CloseAction?.Invoke();
            });
        }

        public bool? Result { get; private set; }
    }
}