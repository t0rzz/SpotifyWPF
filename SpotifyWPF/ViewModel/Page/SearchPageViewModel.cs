﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using SpotifyAPI.Web;
using SpotifyWPF.Service;
using SpotifyWPF.ViewModel.Component;
// ReSharper disable AsyncVoidLambda

namespace SpotifyWPF.ViewModel.Page
{
    public class SearchPageViewModel : ViewModelBase
    {
        private readonly ISpotify _spotify;

        private Visibility _progressVisibility = Visibility.Hidden;

        private string _searchTerms = string.Empty;

        private string _status = "Ready";

        private string _tracksResultsTitle = "Tracks";

        public string TracksResultsTitle
        {
            get => _tracksResultsTitle;

            set
            {
                _tracksResultsTitle = value;
                RaisePropertyChanged();
            }
        }

        private string _artistsResultsTitle = "Artists";

        public string ArtistsResultsTitle
        {
            get => _artistsResultsTitle;

            set
            {
                _artistsResultsTitle = value;
                RaisePropertyChanged();
            }
        }

        private string _albumsResultsTitle = "Albums";

        public string AlbumsResultsTitle
        {
            get => _albumsResultsTitle;

            set
            {
                _albumsResultsTitle = value;
                RaisePropertyChanged();
            }
        }

        private string _playlistsResultsTitle = "Playlists";

        public string PlaylistsResultsTitle
        {
            get => _playlistsResultsTitle;

            set
            {
                _playlistsResultsTitle = value;
                RaisePropertyChanged();
            }
        }

        public int SelectedTab { get; set; } = 0;

        private readonly IList<IDataGridViewModel> _tabs = new List<IDataGridViewModel>();

        public SearchPageViewModel(ISpotify spotify)
        {
            _spotify = spotify;

            SearchCommand = new RelayCommand(async () => await Search(), () => !string.IsNullOrWhiteSpace(SearchTerms));

            TracksDataGridViewModel = new TracksDataGridViewModel(_spotify);
            TracksDataGridViewModel.PageLoaded += (obj, args) =>
            {
                TracksResultsTitle = $"Tracks ({TracksDataGridViewModel.Items.Count}/{TracksDataGridViewModel.Total})";
            };
            TracksDataGridViewModel.PropertyChanged += TabItemPropertyChanged;
            
            _tabs.Add(TracksDataGridViewModel);

            ArtistsDataGridViewModel = new ArtistsDataGridViewModel(_spotify);
            ArtistsDataGridViewModel.PageLoaded += (obj, args) =>
            {
                ArtistsResultsTitle = $"Artists ({ArtistsDataGridViewModel.Items.Count}/{ArtistsDataGridViewModel.Total})";
            };
            ArtistsDataGridViewModel.PropertyChanged += TabItemPropertyChanged;

            _tabs.Add(ArtistsDataGridViewModel);

            AlbumsDataGridViewModel = new AlbumsDataGridViewModel(_spotify);
            AlbumsDataGridViewModel.PageLoaded += (obj, args) =>
            {
                AlbumsResultsTitle =
                    $"Albums ({AlbumsDataGridViewModel.Items.Count}/{AlbumsDataGridViewModel.Total})";
            };
            AlbumsDataGridViewModel.PropertyChanged += TabItemPropertyChanged;

            _tabs.Add(AlbumsDataGridViewModel);

            PlaylistsDataGridViewModel = new PlaylistsDataGridViewModel(_spotify);
            PlaylistsDataGridViewModel.PageLoaded += (obj, args) =>
            {
                PlaylistsResultsTitle =
                    $"Playlists ({PlaylistsDataGridViewModel.Items.Count}/{PlaylistsDataGridViewModel.Total})";
            };
            PlaylistsDataGridViewModel.PropertyChanged += TabItemPropertyChanged;

            _tabs.Add(PlaylistsDataGridViewModel);
        }

        public TracksDataGridViewModel TracksDataGridViewModel { get; }

        public ArtistsDataGridViewModel ArtistsDataGridViewModel { get; }

        public AlbumsDataGridViewModel AlbumsDataGridViewModel { get; }

        public PlaylistsDataGridViewModel PlaylistsDataGridViewModel { get; }

        public Visibility ProgressVisibility
        {
            get => _progressVisibility;

            set
            {
                _progressVisibility = value;
                RaisePropertyChanged();
            }
        }

        public string Status
        {
            get => _status;

            set
            {
                _status = value;
                RaisePropertyChanged();

                ProgressVisibility = value == "Ready" ? Visibility.Hidden : Visibility.Visible;
            }
        }

        public string SearchTerms
        {
            get => _searchTerms;

            set
            {
                _searchTerms = value;
                RaisePropertyChanged();
                SearchCommand.RaiseCanExecuteChanged();
            }
        }

        public RelayCommand SearchCommand { get; }

        private async Task Search()
        {
            Status = "Searching...";

            // Autenticazione e soppressione nullability su Api
            await _spotify.EnsureAuthenticatedAsync();
            var req = new SearchRequest(SearchRequest.Types.All, SearchTerms);
            var searchItem = await _spotify.Api!.Search.Item(req);

            var tracksDto = await _spotify.SearchTracksPageAsync(SearchTerms, 0, 20);
            await TracksDataGridViewModel.InitializeAsync(SearchTerms, tracksDto);

            var artistsDto = await _spotify.SearchArtistsPageAsync(SearchTerms, 0, 20);
            await ArtistsDataGridViewModel.InitializeAsync(SearchTerms, artistsDto);

            var albumsDto = await _spotify.SearchAlbumsPageAsync(SearchTerms, 0, 20);
            await AlbumsDataGridViewModel.InitializeAsync(SearchTerms, albumsDto);

            var playlistsDto = await _spotify.SearchPlaylistsPageAsync(SearchTerms, 0, 20);
            await PlaylistsDataGridViewModel.InitializeAsync(SearchTerms, playlistsDto);

            await _tabs[SelectedTab].MaybeLoadUntilScrollable();

            await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                Status = "Ready";
            }));
        }

        private void TabItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (sender is not IDataGridViewModel dataGridViewModel) return;

            if (args.PropertyName == "Loading")
            {
                Status = dataGridViewModel.Loading ? "Searching..." : "Ready";
            }
        }
    }
}
