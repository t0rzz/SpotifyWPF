using AutoMapper;
using CommonServiceLocator;
using GalaSoft.MvvmLight.Ioc;
using SpotifyWPF.Model;
using SpotifyWPF.Service;
using SpotifyWPF.Service.MessageBoxes;
using SpotifyWPF.ViewModel.Page;

namespace SpotifyWPF.ViewModel
{
    public class ViewModelLocator
    {
        public ViewModelLocator()
        {
            SimpleIoc.Default.Reset();

            ServiceLocator.SetLocatorProvider(() => SimpleIoc.Default);

            SimpleIoc.Default.Register<ILoggingService, LoggingService>();
            SimpleIoc.Default.Register<IConfigurationService, ConfigurationService>();
            SimpleIoc.Default.Register(() => AutoMapperConfiguration.Configure().CreateMapper());
            SimpleIoc.Default.Register<ISettingsProvider, SettingsProvider>();
            SimpleIoc.Default.Register<ISpotify, Spotify>();
            SimpleIoc.Default.Register<IMessageBoxService, MessageBoxService>();
            SimpleIoc.Default.Register<IConfirmationDialogService, ConfirmationDialogService>();

            SimpleIoc.Default.Register<MainViewModel>();
            SimpleIoc.Default.Register<LoginPageViewModel>();
            SimpleIoc.Default.Register<PlaylistsPageViewModel>();
            SimpleIoc.Default.Register<SearchPageViewModel>();
            SimpleIoc.Default.Register<AlbumsPageViewModel>();
            SimpleIoc.Default.Register(() => new StatusBarViewModel(SimpleIoc.Default.GetInstance<MainViewModel>()));
        }

        public MainViewModel Main => ServiceLocator.Current.GetInstance<MainViewModel>();

        public PlaylistsPageViewModel PlaylistsPage => ServiceLocator.Current.GetInstance<PlaylistsPageViewModel>();

        public SearchPageViewModel Search => ServiceLocator.Current.GetInstance<SearchPageViewModel>();

        public AlbumsPageViewModel AlbumsPage => ServiceLocator.Current.GetInstance<AlbumsPageViewModel>();

        public StatusBarViewModel StatusBar => ServiceLocator.Current.GetInstance<StatusBarViewModel>();

        public static void Cleanup()
        {
        }
    }
}