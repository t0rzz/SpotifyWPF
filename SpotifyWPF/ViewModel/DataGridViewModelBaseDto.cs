using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using Nito.AsyncEx;
using SpotifyWPF.Model.Dto;
using SpotifyWPF.View.Extension;

namespace SpotifyWPF.ViewModel
{
    public abstract class DataGridViewModelBaseDto<T> : ViewModelBase, IDataGridViewModel
    {
        private readonly AsyncLock _mutex = new AsyncLock();

        private volatile bool _loadedInitially;
        private volatile bool _loading;

        protected DataGridViewModelBaseDto()
        {
            UpdateVisibilityCommand = new RelayCommand<bool>(async isVisible =>
            {
                if (isVisible) await MaybeLoadUntilScrollable();
                else if (!_loadedInitially) _loadedInitially = true;
            });

            UpdateScrollCommand = new RelayCommand<ScrollInfo>(async scrollInfo =>
            {
                if (!_loadedInitially && scrollInfo.IsScrollable) _loadedInitially = true;
                else if (_loadedInitially && scrollInfo.ScrollPercentage >= 0.85) await FetchAndLoadPageAsync();
            });
        }

        protected string Query { get; private set; } = string.Empty;

        public ObservableCollection<T> Items { get; } = new ObservableCollection<T>();

        public RelayCommand<bool> UpdateVisibilityCommand { get; }
        public RelayCommand<ScrollInfo> UpdateScrollCommand { get; }

        public int? Total { get; private set; }

        public bool Loading
        {
            get => _loading;
            set
            {
                _loading = value;
                RaisePropertyChanged();
            }
        }

        public async Task MaybeLoadUntilScrollable()
        {
            if (string.IsNullOrWhiteSpace(Query)) return;

            await Task.Run(async () =>
            {
                while (!_loadedInitially)
                {
                    if (Total.HasValue && Items.Count >= Total.Value) break;
                    await FetchAndLoadPageAsync();
                }
            });

            _loadedInitially = true;
        }

        public async Task InitializeAsync(string query, PagingDto<T> paging)
        {
            using (await _mutex.LockAsync())
            {
                Loading = false;
                _loadedInitially = false;
                Items.Clear();
                Query = query;
                Total = 0;

                await LoadPageAsync(paging);
            }
        }

        private async Task LoadPageAsync(PagingDto<T> paging)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                await dispatcher.BeginInvoke((Action)(() =>
                {
                    if (paging?.Items != null)
                    {
                        foreach (var item in paging.Items) Items.Add(item);
                    }
                    if (paging != null) Total = paging.Total;
                    PageLoaded?.Invoke(this, EventArgs.Empty);
                }));
            }
            else
            {
                if (paging?.Items != null)
                {
                    foreach (var item in paging.Items) Items.Add(item);
                }
                if (paging != null) Total = paging.Total;
                PageLoaded?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task FetchAndLoadPageAsync()
        {
            using (await _mutex.LockAsync())
            {
                Loading = true;

                if (Total.HasValue && Items.Count >= Total.Value)
                {
                    Loading = false;
                    return;
                }

                var page = await FetchPageInternalAsync();
                await LoadPageAsync(page);

                Loading = false;
            }
        }

        private protected abstract Task<PagingDto<T>> FetchPageInternalAsync();

        public event EventHandler? PageLoaded;
    }
}
