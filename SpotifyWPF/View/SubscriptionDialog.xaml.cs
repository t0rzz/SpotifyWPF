using System.Windows;
using System.Windows.Controls;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;

namespace SpotifyWPF.View
{
    public partial class SubscriptionDialog : UserControl
    {
        public SubscriptionDialog()
        {
            InitializeComponent();
        }
    }

    public class SubscriptionDialogViewModel : ViewModelBase
    {
        private string _title = "Premium Feature";
        private string _message = string.Empty;
        private string _featureName = string.Empty;
        private string _featureDescription = string.Empty;
        private string _upgradeMessage = "Upgrade to Spotify Premium to unlock this feature!";
        private string _okButtonText = "Got it";

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

        public string FeatureName
        {
            get => _featureName;
            set
            {
                _featureName = value;
                RaisePropertyChanged();
            }
        }

        public string FeatureDescription
        {
            get => _featureDescription;
            set
            {
                _featureDescription = value;
                RaisePropertyChanged();
            }
        }

        public string UpgradeMessage
        {
            get => _upgradeMessage;
            set
            {
                _upgradeMessage = value;
                RaisePropertyChanged();
            }
        }

        public string OkButtonText
        {
            get => _okButtonText;
            set
            {
                _okButtonText = value;
                RaisePropertyChanged();
            }
        }

        public RelayCommand OkCommand { get; }

        public Action? CloseAction { get; set; }

        public SubscriptionDialogViewModel(Action? closeAction = null)
        {
            CloseAction = closeAction;
            OkCommand = new RelayCommand(() =>
            {
                CloseAction?.Invoke();
            });
        }
    }
}