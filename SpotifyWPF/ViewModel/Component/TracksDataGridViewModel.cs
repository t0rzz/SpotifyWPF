using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using SpotifyWPF.Model.Dto;
using SpotifyWPF.Service;
using SpotifyWPF.Service.MessageBoxes;
using SpotifyWPF.ViewModel;
using GalaSoft.MvvmLight.Command;
using SpotifyAPI.Web;

namespace SpotifyWPF.ViewModel.Component
{
    public class TracksDataGridViewModel : DataGridViewModelBaseDto<TrackDto>
    {
        private readonly ISpotify _spotify;
        private readonly ISubscriptionDialogService _subscriptionDialogService;
        private Action<string>? _logAction;
        
        public ObservableCollection<Device> Devices { get; } = new ObservableCollection<Device>();
        public RelayCommand<string> PlayToDeviceCommand { get; }
        public RelayCommand RefreshDevicesCommand { get; }

        public TracksDataGridViewModel(ISpotify spotify, ISubscriptionDialogService subscriptionDialogService, Action<string>? logAction = null)
        {
            _spotify = spotify;
            _subscriptionDialogService = subscriptionDialogService;
            _logAction = logAction;
            PlayToDeviceCommand = new RelayCommand<string>(async param => 
            {
                var subscriptionType = await _spotify.GetUserSubscriptionTypeAsync().ConfigureAwait(false);
                if (subscriptionType?.ToLower() != "premium")
                {
                    System.Diagnostics.Debug.WriteLine($"[TracksDataGrid] PlayToDeviceCommand: User does not have premium subscription (subscriptionType={subscriptionType}), showing modal and blocking access");
                    _subscriptionDialogService.ShowSubscriptionDialog("Premium Feature Required", "Playing music on specific devices requires a Spotify Premium subscription. Upgrade to access this feature.", "Play to Device", "Transfer playback to any of your connected devices");
                    return;
                }
                await PlayToAsync(param);
            });
            RefreshDevicesCommand = new RelayCommand(async () =>
            {
                var subscriptionType = await _spotify.GetUserSubscriptionTypeAsync().ConfigureAwait(false);
                if (subscriptionType?.ToLower() == "premium")
                {
                    await RefreshDevicesAsync();
                }
                else
                {
                    // For free users, clear devices or show message
                    await App.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Devices.Clear();
                        LogMessage($"[TracksDataGrid] Not refreshing devices - user does not have premium subscription");
                    });
                }
            });
            // Removed preload - devices will be loaded on submenu open only for premium users
        }

        public void SetLogAction(Action<string>? logAction)
        {
            _logAction = logAction;
        }

        private protected override async Task<PagingDto<TrackDto>> FetchPageInternalAsync()
        {
            return await _spotify.SearchTracksPageAsync(Query, Items.Count, 20);
        }

        private void LogMessage(string message)
        {
            _logAction?.Invoke(message);
        }

        public async Task RefreshDevicesAsync()
        {
            try
            {
                var list = await _spotify.GetDevicesAsync().ConfigureAwait(false);
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    Devices.Clear();
                    foreach (var d in list)
                    {
                        if (d != null && !string.IsNullOrWhiteSpace(d.Id)) 
                        {
                            Devices.Add(d);
                            LogMessage($"[TracksDataGrid] Device: {d.Name} (ID: {d.Id}) - Active: {d.IsActive}");
                        }
                    }
                    LogMessage($"[TracksDataGrid] Refreshed {Devices.Count} devices in Play To submenu");
                });
            }
            catch (Exception ex)
            {
                LogMessage($"[TracksDataGrid] Error refreshing devices: {ex.Message}");
            }
        }

        private async Task PlayToAsync(string? param)
        {
            LogMessage($"[TracksDataGrid] PlayToAsync called with param: {param ?? "NULL"}");
            
            if (string.IsNullOrWhiteSpace(param)) 
            {
                LogMessage($"[TracksDataGrid] PlayToAsync: param is null or empty, returning");
                return;
            }
            
            var parts = param.Split('|');
            if (parts.Length != 2) 
            {
                LogMessage($"[TracksDataGrid] PlayToAsync: param does not contain exactly 2 parts, found {parts.Length}");
                return;
            }
            
            var deviceId = parts[0];
            var trackId = parts[1];
            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(trackId)) 
            {
                LogMessage($"[TracksDataGrid] PlayToAsync: deviceId or trackId is null/empty. Device: '{deviceId}', Track: '{trackId}'");
                return;
            }
            
            LogMessage($"[TracksDataGrid] Playing track {trackId} on device {deviceId}");
            // Ensure the device is active: transfer then attempt to play (helps devices that require transfer)
            try
            {
                await _spotify.TransferPlaybackAsync(new[] { deviceId }, true);
                await Task.Delay(400);
            }
            catch { }
            await _spotify.PlayTrackOnDeviceAsync(deviceId, trackId);
            
            // Wait a moment for Spotify to update the active device state
            LogMessage($"[TracksDataGrid] Waiting for Spotify to update device state...");
            await Task.Delay(1000);
            
            // Refresh devices list to update active device checkmarks
            LogMessage($"[TracksDataGrid] Refreshing devices list after Play To command");
            await RefreshDevicesAsync();
        }
    }
}
