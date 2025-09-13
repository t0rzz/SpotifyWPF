using System.Collections.ObjectModel;
using System.Windows.Input;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;

namespace SpotifyWPF.ViewModel.Component
{
    public class MenuItemViewModel : ViewModelBase
    {
        private string _header;
        private bool _isChecked;
        private string? _id;
        private string? _iconGlyph;

        public MenuItemViewModel(string header) : this(header, null) { }

        public MenuItemViewModel(string header, ICommand? command)
        {
            _header = header;
            Command = command ?? new RelayCommand(() => { });
            MenuItems = new ObservableCollection<MenuItemViewModel>();
        }

        public string Header
        {
            get => _header;

            set
            {
                _header = value;
                RaisePropertyChanged();
            }
        }

        public ObservableCollection<MenuItemViewModel> MenuItems { get; set; }

        public bool IsChecked
        {
            get => _isChecked;

            set
            {
                _isChecked = value;
                RaisePropertyChanged();
            }
        }

        // Optional identifier (e.g., device id)
        public string? Id
        {
            get => _id;
            set
            {
                _id = value;
                RaisePropertyChanged();
            }
        }

        // Optional glyph from Segoe MDL2 Assets for an icon
        public string? IconGlyph
        {
            get => _iconGlyph;
            set
            {
                _iconGlyph = value;
                RaisePropertyChanged();
            }
        }

        public ICommand Command { get; }
    }
}