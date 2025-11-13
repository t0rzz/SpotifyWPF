using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SpotifyWPF.Model;
using SpotifyWPF.Service;

namespace SpotifyWPF.ViewModel.Component
{
    /// <summary>
    /// Component responsible for managing Spotify device discovery, selection, and transfer operations.
    /// Encapsulates all device-related logic to reduce complexity in PlayerViewModel.
    /// </summary>
    public class DeviceManager : ObservableObject, IDisposable
    {
        private readonly ISpotify _spotify;
        private readonly IWebPlaybackBridge _webPlaybackBridge;
        private readonly ILoggingService _loggingService;

        // Device state
        private DeviceModel? _selectedDevice;
        private string _webPlaybackDeviceId = string.Empty;
        private TaskCompletionSource<string>? _webDeviceReadyTcs;

        // Device collection
        public ObservableCollection<DeviceModel> Devices { get; } = new ObservableCollection<DeviceModel>();

        /// <summary>
        /// Currently selected device
        /// </summary>
        public DeviceModel? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (_selectedDevice != value)
                {
                    _selectedDevice = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Web Playback SDK device ID
        /// </summary>
        public string WebPlaybackDeviceId
        {
            get => _webPlaybackDeviceId;
            set
            {
                if (_webPlaybackDeviceId != value)
                {
                    var oldValue = _webPlaybackDeviceId;
                    _webPlaybackDeviceId = value;
                    OnPropertyChanged();
                    _loggingService.LogDebug($"[WEB_DEVICE] Property setter: WebPlaybackDeviceId changed from '{oldValue}' to '{value}'");
                }
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public DeviceManager(ISpotify spotify, IWebPlaybackBridge webPlaybackBridge, ILoggingService loggingService)
        {
            _spotify = spotify ?? throw new ArgumentNullException(nameof(spotify));
            _webPlaybackBridge = webPlaybackBridge ?? throw new ArgumentNullException(nameof(webPlaybackBridge));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            // Subscribe to Web Playback SDK events
            _webPlaybackBridge.OnReadyDeviceId += OnReadyDeviceId;
        }

        /// <summary>
        /// Refresh available devices from Spotify API
        /// </summary>
        public async Task RefreshDevicesAsync()
        {
            try
            {
                var devices = await _spotify.GetDevicesAsync();
                
                Devices.Clear();
                foreach (var device in devices)
                {
                    Devices.Add(new DeviceModel
                    {
                        Id = device.Id,
                        Name = device.Name,
                        Type = device.Type ?? string.Empty,
                        IsActive = device.IsActive
                    });
                }

                // Ensure Web Playback device appears in the list for bindings, even if API doesn't report it yet
                if (!string.IsNullOrEmpty(WebPlaybackDeviceId) && !Devices.Any(d => d.Id == WebPlaybackDeviceId))
                {
                    Devices.Add(new DeviceModel
                    {
                        Id = WebPlaybackDeviceId,
                        Name = "Web Player",
                        Type = "computer", // Web Player is a computer-type device
                        IsActive = false
                    });
                }

                // Set selected device to active one
                SelectedDevice = Devices.FirstOrDefault(d => d.IsActive);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshDevices error: {ex.Message}");
            }
        }

        /// <summary>
        /// Select a device and transfer playback to it
        /// </summary>
        public async Task SelectDeviceAsync(DeviceModel device)
        {
            try
            {
                if (device == null) return;

                SelectedDevice = device;
                
                // Transfer playback to selected device (false = keep current playback state)
                await _spotify.TransferPlaybackAsync(new[] { device.Id }, false);

                // Give Spotify a brief moment to apply the transfer, then resync devices and state
                await Task.Delay(300);
                await RefreshDevicesAsync();

                // If the selected device became active, keep it selected; otherwise, switch to whatever is active
                var active = Devices.FirstOrDefault(d => d.IsActive);
                if (active != null)
                {
                    SelectedDevice = active;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SelectDevice error: {ex.Message}");
                
                // Retry once after 250ms for 404 errors, then resync state
                if (ex.Message.Contains("404"))
                {
                    await Task.Delay(250);
                    try
                    {
                        await _spotify.TransferPlaybackAsync(new[] { device.Id }, false);
                        await Task.Delay(300);
                        await RefreshDevicesAsync();
                        var active = Devices.FirstOrDefault(d => d.IsActive);
                        if (active != null) SelectedDevice = active;
                    }
                    catch (Exception retryEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"SelectDevice retry error: {retryEx.Message}");
                    }
                }
            }
        }        /// <summary>
        /// Ensure there is an active device; if none, transfer playback to the Web Playback SDK device (this app)
        /// </summary>
        public async Task<bool> EnsureAppDeviceActiveAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🎵 EnsureAppDeviceActiveAsync: Starting device check");

                // Refresh devices to get current active status
                await RefreshDevicesAsync();

                // Fast-path: if there is already an active device, honor it
                var activeDevice = Devices.FirstOrDefault(d => d.IsActive);
                if (activeDevice != null)
                {
                    System.Diagnostics.Debug.WriteLine($"🎵 Active device found: {activeDevice.Name} ({activeDevice.Id})");
                    SelectedDevice = activeDevice;
                    return true;
                }

                // If WebPlaybackDeviceId not yet known, wait briefly for the Web Playback SDK to report it
                if (string.IsNullOrEmpty(WebPlaybackDeviceId))
                {
                    var waitedId = await WaitForWebDeviceIdAsync(TimeSpan.FromSeconds(4));
                    if (!string.IsNullOrEmpty(waitedId))
                    {
                        WebPlaybackDeviceId = waitedId;
                        System.Diagnostics.Debug.WriteLine($"🎵 Web device became ready: {WebPlaybackDeviceId}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"🎵 No active device found. WebPlaybackDeviceId: {WebPlaybackDeviceId}");
                System.Diagnostics.Debug.WriteLine($"🎵 Available devices: {string.Join(", ", Devices.Select(d => $"{d.Name}({d.Id}, Active:{d.IsActive})"))}");

                // If WebPlaybackDeviceId is empty, try to find our web player device in the devices list
                if (string.IsNullOrEmpty(WebPlaybackDeviceId))
                {
                    var webDevice = Devices.FirstOrDefault(d => d.Name == "Web Player" || d.Name.Contains("SpotifyWPF"));
                    if (webDevice != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"🎵 Found Web Player device in list: {webDevice.Id}");
                        WebPlaybackDeviceId = webDevice.Id;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ WebPlaybackDeviceId is empty and no Web Player found in devices list");
                        System.Diagnostics.Debug.WriteLine("⚠️ Web Playback SDK may not be ready yet. Trying to reconnect...");

                        // Try to reconnect the Web Playback bridge
                        try
                        {
                            await _webPlaybackBridge.ConnectAsync();
                            await Task.Delay(1000); // Wait for connection and ready event
                            await RefreshDevicesAsync();

                            // Try to find the device again
                            var webDeviceRetry = Devices.FirstOrDefault(d => d.Name == "Web Player" || d.Name.Contains("SpotifyWPF"));
                            if (webDeviceRetry != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"🎵 Found Web Player device after reconnect: {webDeviceRetry.Id}");
                                WebPlaybackDeviceId = webDeviceRetry.Id;
                            }
                        }
                        catch (Exception reconnectEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ Error reconnecting Web Playback: {reconnectEx.Message}");
                        }
                    }
                }

                // No active device – try to promote our Web Playback device
                if (!string.IsNullOrEmpty(WebPlaybackDeviceId))
                {
                    System.Diagnostics.Debug.WriteLine($"🎵 Transferring playback to Web Playback device: {WebPlaybackDeviceId} (with play=true to activate) ");
                    await _spotify.TransferPlaybackAsync(new[] { WebPlaybackDeviceId }, true);
                    System.Diagnostics.Debug.WriteLine("🎵 TransferPlayback call completed (play=true), waiting for device switch...");

                    // Poll until device becomes active (up to ~4.5s)
                    var attempts = 0;
                    while (attempts++ < 15)
                    {
                        await Task.Delay(300);
                        await RefreshDevicesAsync();
                        var webDevice = Devices.FirstOrDefault(d => d.Id == WebPlaybackDeviceId);
                        if (webDevice != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"🎵 Web device polled: {webDevice.Name}, IsActive: {webDevice.IsActive}");
                            if (webDevice.IsActive)
                            {
                                SelectedDevice = webDevice;
                                return true;
                            }
                        }
                    }
                    System.Diagnostics.Debug.WriteLine("⚠️ Web device did not become active within timeout");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ WebPlaybackDeviceId is still empty after all attempts, cannot transfer playback");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ EnsureAppDeviceActiveAsync error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
            }
            return false;
        }

        /// <summary>
        /// Wait for the Web Playback device ID to be available
        /// </summary>
        public async Task<string?> WaitForWebDeviceIdAsync(TimeSpan timeout)
        {
            if (!string.IsNullOrEmpty(WebPlaybackDeviceId))
            {
                _loggingService.LogDebug($"[WEB_DEVICE] WaitForWebDeviceIdAsync: Already have device ID: {WebPlaybackDeviceId}");
                return WebPlaybackDeviceId;
            }
            try
            {
                _loggingService.LogDebug($"[WEB_DEVICE] WaitForWebDeviceIdAsync: Waiting for device ID with timeout {timeout.TotalSeconds}s");
                var tcs = _webDeviceReadyTcs;
                if (tcs == null)
                {
                    _loggingService.LogDebug($"[WEB_DEVICE] WaitForWebDeviceIdAsync: Creating new TaskCompletionSource");
                    _webDeviceReadyTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    tcs = _webDeviceReadyTcs;
                }
                else
                {
                    _loggingService.LogDebug($"[WEB_DEVICE] WaitForWebDeviceIdAsync: Using existing TaskCompletionSource");
                }
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
                if (completed == tcs.Task)
                {
                    var result = tcs.Task.Result;
                    _loggingService.LogDebug($"[WEB_DEVICE] WaitForWebDeviceIdAsync: Got device ID: {result}");
                    return result;
                }
                else
                {
                    _loggingService.LogDebug($"[WEB_DEVICE] WaitForWebDeviceIdAsync: Timeout waiting for device ID");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[WEB_DEVICE] WaitForWebDeviceIdAsync: Error: {ex.Message}", ex);
            }
            return null;
        }

        /// <summary>
        /// Handle Web Playback SDK ready event
        /// </summary>
        public void OnReadyDeviceId(string deviceId)
        {
            try
            {
                _loggingService.LogDebug($"[WEB_DEVICE] OnReadyDeviceId called with: {deviceId}");
                System.Diagnostics.Debug.WriteLine($"DeviceManager.OnReadyDeviceId called with: {deviceId}");
                WebPlaybackDeviceId = deviceId;
                _loggingService.LogDebug($"[WEB_DEVICE] Set WebPlaybackDeviceId to: {WebPlaybackDeviceId}");
                _webDeviceReadyTcs?.TrySetResult(deviceId);
                _loggingService.LogDebug($"[WEB_DEVICE] Completed TaskCompletionSource with: {deviceId}");

                // Add to devices collection if not already present
                if (!Devices.Any(d => d.Id == deviceId))
                {
                    var webDevice = new DeviceModel
                    {
                        Id = deviceId,
                        Name = "Web Player",
                        IsActive = false
                    };
                    Devices.Add(webDevice);
                    _loggingService.LogDebug($"[WEB_DEVICE] Added Web Player device with ID: {deviceId}");
                    System.Diagnostics.Debug.WriteLine($"Added Web Player device with ID: {deviceId}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"[WEB_DEVICE] OnReadyDeviceId error: {ex.Message}", ex);
                System.Diagnostics.Debug.WriteLine($"OnReadyDeviceId error: {ex.Message}");
            }
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            // Unsubscribe from events
            _webPlaybackBridge.OnReadyDeviceId -= OnReadyDeviceId;

            // Cancel any pending TaskCompletionSource
            if (_webDeviceReadyTcs != null && !_webDeviceReadyTcs.Task.IsCompleted)
            {
                _webDeviceReadyTcs.TrySetCanceled();
            }
        }
    }
}