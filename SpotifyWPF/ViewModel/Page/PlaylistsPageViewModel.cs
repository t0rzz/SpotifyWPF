using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AutoMapper;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using SpotifyAPI.Web;
using SpotifyWPF.Model;
using SpotifyWPF.Model.Dto;
using SpotifyWPF.Service;
using SpotifyWPF.Service.MessageBoxes;
using Newtonsoft.Json.Linq;
using MessageBoxButton = SpotifyWPF.Service.MessageBoxes.MessageBoxButton;
using MessageBoxResult = SpotifyWPF.Service.MessageBoxes.MessageBoxResult;
// ReSharper disable AsyncVoidLambda

namespace SpotifyWPF.ViewModel.Page
{
    public class PlaylistsPageViewModel : ViewModelBase
    {
        private readonly IMapper _mapper;

        private readonly IMessageBoxService _messageBoxService;
        private readonly ISpotify _spotify;

        private Visibility _progressVisibility = Visibility.Hidden;

        private string _status = "Ready";

        private bool _isLoadingPlaylists;

        public PlaylistsPageViewModel(ISpotify spotify, IMapper mapper, IMessageBoxService messageBoxService)
        {
            _spotify = spotify;
            _mapper = mapper;
            _messageBoxService = messageBoxService;

            LoadPlaylistsCommand = new RelayCommand(
                async () => await LoadPlaylistsAsync(),
                () => _busyCount == 0 && !_isLoadingPlaylists
            );
            LoadTracksCommand = new RelayCommand<PlaylistDto>(async playlist => await LoadTracksAsync(playlist));
            DeletePlaylistsCommand = new RelayCommand<IList>(
                async playlists =>
                {
                    if (_isDeletingPlaylists) return;
                    _cancelRequested = false;
                    _isDeletingPlaylists = true;
                    UpdateLoadingUiState();
                    try
                    {
                        await DeletePlaylistsAsync(playlists);
                    }
                    finally
                    {
                        _isDeletingPlaylists = false;
                        UpdateLoadingUiState();
                    }
                },
                playlists => playlists != null && playlists.Count > 0
            );

            StartLoadPlaylistsCommand = new RelayCommand(
                async () =>
                {
                    if (_isLoadingPlaylists) return;
                    _loadPlaylistsCts = new CancellationTokenSource();
                    UpdateLoadingUiState();
                    try
                    {
                        await LoadPlaylistsAsync(_loadPlaylistsCts.Token);
                    }
                    finally
                    {
                        _loadPlaylistsCts?.Dispose();
                        _loadPlaylistsCts = null;
                        UpdateLoadingUiState();
                    }
                },
                () => CanStart
            );

            StopLoadPlaylistsCommand = new RelayCommand(
                () =>
                {
                    _cancelRequested = true; // cancel deletions
                    _loadPlaylistsCts?.Cancel(); // cancel loading
                    UpdateLoadingUiState();
                },
                () => CanStop
            );

            // Context menu commands
            OpenInSpotifyCommand = new RelayCommand<PlaylistDto>(
                p =>
                {
                    var url = BuildPlaylistWebUrl(p);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        TryOpenUrl(url);
                    }
                },
                p => p != null && !string.IsNullOrWhiteSpace(p.Id) && !IsBusyAny
            );

            CopyPlaylistLinkCommand = new RelayCommand<PlaylistDto>(
                p =>
                {
                    var url = BuildPlaylistWebUrl(p);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        TryCopyToClipboard(url);
                    }
                },
                p => p != null && !string.IsNullOrWhiteSpace(p.Id)
            );

            UnfollowPlaylistCommand = new RelayCommand<PlaylistDto>(
                async p =>
                {
                    if (p == null) return;
                    await DeletePlaylistsAsync(new[] { p });
                },
                p => p != null && !string.IsNullOrWhiteSpace(p.Id) && !_isDeletingPlaylists
            );

            // Timer per flush del log in batch
            _logFlushTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(LogFlushIntervalMs)
            };
            _logFlushTimer.Tick += (s, e) => FlushLogTick();
            _logFlushTimer.Start();
        }

        public ObservableCollection<PlaylistDto> Playlists { get; } = new ObservableCollection<PlaylistDto>();

        public ObservableCollection<Track> Tracks { get; } = new ObservableCollection<Track>();

        // Tracciamo gli ID già caricati per evitare duplicati tra tentativi
        private readonly HashSet<string> _playlistIds = new HashSet<string>();

        public string Status
        {
            get => _status;

            set
            {
                _status = value;
                RaisePropertyChanged();
            }
        }

        public Visibility ProgressVisibility
        {
            get => _progressVisibility;

            set
            {
                _progressVisibility = value;
                RaisePropertyChanged();
            }
        }

        private string _outputLog = string.Empty;

        public string OutputLog
        {
            get => _outputLog;
            set
            {
                _outputLog = value ?? string.Empty;
                RaisePropertyChanged();
            }
        }

        // Batching del log per evitare saturazione del thread UI
        private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
        private DispatcherTimer _logFlushTimer;
        private const int LogBatchSize = 200;           // quante righe scaricare per tick
        private const int LogFlushIntervalMs = 100;     // ogni quanti ms flushare
        private const int LogMaxChars = 300000;         // cap per il testo nel box

        public RelayCommand<PlaylistDto> LoadTracksCommand { get; }

        public RelayCommand<IList> DeletePlaylistsCommand { get; }

        public RelayCommand LoadPlaylistsCommand { get; }

        public RelayCommand StartLoadPlaylistsCommand { get; }

        public RelayCommand StopLoadPlaylistsCommand { get; }

        // Context menu commands
        public RelayCommand<PlaylistDto> OpenInSpotifyCommand { get; }
        public RelayCommand<PlaylistDto> CopyPlaylistLinkCommand { get; }
        public RelayCommand<PlaylistDto> UnfollowPlaylistCommand { get; }

        private CancellationTokenSource? _loadPlaylistsCts;
        private volatile bool _cancelRequested;
        private bool _isDeletingPlaylists;

        public bool CanStart => !_isLoadingPlaylists && _busyCount == 0 && !_isDeletingPlaylists;

        public bool CanStop => _isLoadingPlaylists || _isDeletingPlaylists;

        private int _busyCount = 0;

        private bool IsBusyAny => _isLoadingPlaylists || _isDeletingPlaylists || _busyCount > 0;

        private void UpdateLoadingUiState()
        {
            LoadPlaylistsCommand?.RaiseCanExecuteChanged();
            StartLoadPlaylistsCommand?.RaiseCanExecuteChanged();
            StopLoadPlaylistsCommand?.RaiseCanExecuteChanged();
            OpenInSpotifyCommand?.RaiseCanExecuteChanged();
            CopyPlaylistLinkCommand?.RaiseCanExecuteChanged();
            UnfollowPlaylistCommand?.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(CanStart));
            RaisePropertyChanged(nameof(CanStop));
        }

        private void StartBusy(String initialStatus)
        {
            System.Threading.Interlocked.Increment(ref _busyCount);
            Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
            {
                if (!string.IsNullOrEmpty(initialStatus))
                {
                    Status = initialStatus;
                }
                ProgressVisibility = Visibility.Visible;
                // Aggiorna lo stato dei comandi
                UpdateLoadingUiState();
            }));
        }

        private void EndBusy()
        {
            var left = System.Threading.Interlocked.Decrement(ref _busyCount);
            if (left <= 0)
            {
                // Normalizza a zero
                System.Threading.Interlocked.Exchange(ref _busyCount, 0);
                Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
                {
                    Status = "Ready";
                    ProgressVisibility = Visibility.Hidden;
                    // Aggiorna lo stato dei comandi
                    UpdateLoadingUiState();
                }));
            }
            else
            {
                // Aggiorna comunque lo stato del comando in caso vari il conteggio
                Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
                {
                    UpdateLoadingUiState();
                }));
            }
        }

        public async Task DeletePlaylistsAsync(IList items)
        {
            if (items == null) return;

            var playlists = items.Cast<PlaylistDto>().ToList();
            if (!playlists.Any()) return;

            var message = playlists.Count == 1
                ? $"Are you sure you want to delete playlist {playlists.ElementAt(0).Name}?"
                : $"Are you sure you want to delete these {playlists.Count} playlists?";

            var result = _messageBoxService.ShowMessageBox(
                message,
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxIcon.Exclamation
            );

            if (result != MessageBoxResult.Yes) return;

            if (_spotify.Api == null)
            {
                AppendLog("You must be logged in to delete playlists.");
                return;
            }

            const int maxAttempts = 5;
            const int maxConcurrency = 10;

            StartBusy($"Deleting {playlists.Count} playlist(s) with up to {maxConcurrency} workers...");

            var throttler = new System.Threading.SemaphoreSlim(maxConcurrency);
            var tasks = new System.Collections.Generic.List<Task>();

            try
            {
                foreach (var playlist in playlists)
                {
                    if (_cancelRequested) { AppendLog("Deletion cancelled by user."); break; }
                    if (playlist == null || string.IsNullOrEmpty(playlist.Id))
                    {
                        continue;
                    }

                    var t = Task.Run(async () =>
                    {
                        var acquired = false;
                        try
                        {
                            if (_cancelRequested) return;
                            await throttler.WaitAsync();
                            acquired = true;
                            if (_cancelRequested) return;
                            var attempt = 0;
                            var success = false;

                            while (attempt < maxAttempts && !success && !_cancelRequested)
                            {
                                attempt++;
                                try
                                {
                                    await _spotify.EnsureAuthenticatedAsync();
                                    await _spotify.UnfollowPlaylistAsync(playlist.Id);

                                    // Rimozione ottimistica dalla UI per evitare chiamate di verifica aggiuntive
                                    await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                                    {
                                        Playlists.Remove(playlist);
                                        if (!string.IsNullOrEmpty(playlist.Id))
                                        {
                                            _playlistIds.Remove(playlist.Id);
                                        }
                                    }));

                                    success = true;
                                    AppendLog($"Deleted playlist '{playlist.Name}' on attempt {attempt}.");
                                }
                                catch (APIException apiEx)
                                {
                                    var messageText = apiEx.Message ?? apiEx.ToString();
                                    AppendLog($"Attempt {attempt} to delete '{playlist.Name}' failed: {messageText}");

                                    // Token scaduto: prova a rinnovare e riprova subito
                                    if (SpotifyWPF.Service.RateLimitHelper.IsAccessTokenExpiredMessage(messageText))
                                    {
                                        var ok = await _spotify.EnsureAuthenticatedAsync();
                                        if (!ok) AppendLog("Automatic token refresh failed.");
                                    }

                                    // Rate limit: rispetta Retry-After se presente, solo per questo worker
                                    var retryAfterSeconds = SpotifyWPF.Service.RateLimitHelper.TryExtractRetryAfterSeconds(messageText);
                                    if (retryAfterSeconds.HasValue)
                                    {
                                        if (_cancelRequested) break;
                                        await Task.Delay(TimeSpan.FromSeconds(retryAfterSeconds.Value));
                                    }
                                    else if (!string.IsNullOrEmpty(messageText) &&
                                             messageText.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        // Fallback: nessun Retry-After, aggiungi un backoff breve con jitter
                                        var rnd = new Random();
                                        var jitterMs = rnd.Next(100, 400);
                                        if (_cancelRequested) break;
                                        await Task.Delay(TimeSpan.FromMilliseconds(1200 + jitterMs));
                                    }
                                    // Altrimenti nessun delay aggiuntivo
                                }
                                catch (Exception ex)
                                {
                                    AppendLog($"Attempt {attempt} to delete '{playlist.Name}' failed: {ex.Message}");
                                    // Nessun delay extra sui generici per massima velocità
                                }
                            }

                            if (!success)
                            {
                                AppendLog($"Failed to delete playlist '{playlist.Name}' after {attempt} attempt(s).");
                            }
                        }
                        finally
                        {
                            if (acquired) throttler.Release();
                        }
                    });

                    tasks.Add(t);
                }

                await Task.WhenAll(tasks);
            }
            finally
            {
                EndBusy();
            }
        }

        public async Task LoadPlaylistsAsync(CancellationToken cancellationToken = default)
        {
            if (_spotify.Api == null)
            {
                AppendLog("You must be logged in to load playlists.");
                return;
            }

            if (_isLoadingPlaylists)
            {
                AppendLog("A playlists load is already in progress. Skipping.");
                return;
            }
            _isLoadingPlaylists = true;
            UpdateLoadingUiState();

            try
            {
                StartBusy("Loading playlists...");
                AppendLog("Starting playlists load (offset-based, keeping existing items in UI).");

                // Per un nuovo fetch/refresh, svuotiamo UI e set ID per evitare dedup da run precedenti
                await Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    Playlists.Clear();
                    _playlistIds.Clear();
                }));

                const int limit = 50;
                int offset = 0;
                int? expectedTotal = null;

                // 1) Ottieni la prima pagina valida con retry (per ricavare expectedTotal)
                {
                    const int pageMaxAttempts = 20;
                    bool pageLoaded = false;
                    PagingDto<PlaylistDto>? page = null;

                    for (var attempt = 1; attempt <= pageMaxAttempts && !pageLoaded && !cancellationToken.IsCancellationRequested; attempt++)
                    {
                        try
                        {
                            await _spotify.EnsureAuthenticatedAsync();
                            page = await _spotify.GetMyPlaylistsPageAsync(offset, limit);

                            // Log dell'output dell'API (redacted)
                            try
                            {
                                var json = BuildPlaylistsPageApiOutput(page);
                                AppendLog($"Spotify API response (playlists page offset {offset}) [redacted]:");
                                foreach (var line in json.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                                {
                                    AppendLog("  " + line);
                                }
                            }
                            catch { /* ignore logging issues */ }

                            // Validazione coerenza (anche per la prima pagina)
                            ValidatePlaylistsPageConsistency(page, expectedTotal, 0);

                            // Prima pagina valida: fissa il totale atteso
                            if (page != null && page.Total > 0)
                            {
                                expectedTotal = page.Total;
                                AppendLog($"Detected expected total playlists from API: {expectedTotal.Value}");
                            }

                            // Aggiunge elementi (deduplica)
                            var countToAdd = page?.Items?.Count ?? 0;
                            if (countToAdd > 0)
                            {
                                var toAdd = page;
                                var disp = Application.Current?.Dispatcher;
                                if (disp != null)
                                {
                                    await disp.BeginInvoke((Action)(() => { AddPlaylists(toAdd); }));
                                }
                                else
                                {
                                    AddPlaylists(toAdd);
                                }
                            }

                            pageLoaded = true;
                        }
                        catch (APIException apiEx)
                        {
                            AppendLog($"Page fetch attempt {attempt} at offset {offset} failed: {apiEx.Message}");
                            if (apiEx.Message != null && apiEx.Message.IndexOf("access token expired", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var ok = await _spotify.EnsureAuthenticatedAsync();
                                if (!ok) AppendLog("Automatic token refresh failed.");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"Page fetch attempt {attempt} at offset {offset} failed: {ex.Message}");
                        }
                    }

                    if (!pageLoaded)
                    {
                        AppendLog("Stopping load: could not load first page after 20 attempts.");
                        return;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        AppendLog("Load cancelled during first page phase.");
                        return;
                    }
                }

                // Se non abbiamo potuto stabilire un totale atteso, fermiamoci
                if (!expectedTotal.HasValue || expectedTotal.Value <= 0)
                {
                    AppendLog("Expected total could not be determined. Stopping.");
                    return;
                }

                // 2) Strategia di paginazione: solo offset-based in parallelo
                var total = expectedTotal.Value;

                var nextStart = limit; // abbiamo già aggiunto offset 0 (se aveva items)
                var offsets = new System.Collections.Concurrent.ConcurrentQueue<int>();
                for (var off = nextStart; off < total; off += limit)
                {
                    offsets.Enqueue(off);
                }

                // Calcolo dinamico dei worker (conservativo) per ridurre i 429
                var pagesRemaining = (int)Math.Ceiling((total - nextStart) / (double)limit);
                var computedWorkers = Math.Max(1, Math.Min(pagesRemaining, ComputeWorkerCount(total, 2000)));
                AppendLog($"Starting concurrent page fetch with {computedWorkers} worker(s). Total expected={total}, page size={limit}");

                var workers = new List<Task>();
                var allLoaded = false;

                for (var w = 0; w < computedWorkers; w++)
                {
                    workers.Add(Task.Run(async () =>
                    {
                        while (!allLoaded && !cancellationToken.IsCancellationRequested && offsets.TryDequeue(out var off))
                        {
                            // Retry per pagina (senza delay), reset per ogni offset
                            const int pageMaxAttempts = 20;
                            bool pageLoaded = false;

                            for (var attempt = 1; attempt <= pageMaxAttempts && !pageLoaded; attempt++)
                            {
                                try
                                {
                                    await _spotify.EnsureAuthenticatedAsync();
                                    var page = await _spotify.GetMyPlaylistsPageAsync(off, limit);

                                    // Log dell'output (redacted)
                                    try
                                    {
                                        var json = BuildPlaylistsPageApiOutput(page);
                                        AppendLog($"Spotify API response (playlists page offset {off}) [redacted]:");
                                        foreach (var line in json.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                                        {
                                            AppendLog("  " + line);
                                        }
                                    }
                                    catch { }

                                    ValidatePlaylistsPageConsistency(page, total, off / limit);

                                    var countToAdd = page?.Items?.Count ?? 0;
                                    if (countToAdd > 0)
                                    {
                                        var toAdd = page;
                                        var disp = Application.Current?.Dispatcher;
                                        if (disp != null)
                                        {
                                            await disp.BeginInvoke((Action)(() => { AddPlaylists(toAdd); }));
                                        }
                                        else
                                        {
                                            AddPlaylists(toAdd);
                                        }
                                    }

                                    pageLoaded = true;

                                    var uiCount = GetLoadedPlaylistsCount();
                                    if (uiCount >= total)
                                    {
                                        allLoaded = true;
                                        break;
                                    }
                                }
                                catch (APIException apiEx)
                                {
                                    AppendLog($"Page fetch attempt {attempt} at offset {off} failed: {apiEx.Message}");
                                    if (apiEx.Message != null && apiEx.Message.IndexOf("access token expired", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        var ok = await _spotify.EnsureAuthenticatedAsync();
                                        if (!ok) AppendLog("Automatic token refresh failed.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    AppendLog($"Page fetch attempt {attempt} at offset {off} failed: {ex.Message}");
                                }
                            }

                            if (!pageLoaded)
                            {
                                AppendLog($"Skipping offset {off}: failed after 20 attempts.");
                            }

                            // Controllo completamento anche se la pagina è stata saltata
                            if (GetLoadedPlaylistsCount() >= total)
                            {
                                allLoaded = true;
                                break;
                            }
                        }
                    }));
                }

                await Task.WhenAll(workers);

                var finalCount = GetLoadedPlaylistsCount();
                if (finalCount >= total)
                {
                    AppendLog($"All playlist pages loaded. Total loaded in UI: {finalCount}");
                }
                else
                {
                    AppendLog($"Completed concurrent fetchers. Loaded {finalCount}/{total} playlists.");
                }
            }
            finally
            {
                _isLoadingPlaylists = false;
                UpdateLoadingUiState();
                EndBusy();
            }
        }

        private void AppendLog(string line)
        {
            var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
            _logQueue.Enqueue(stamped);
        }

        private void FlushLogTick()
        {
            if (_logQueue.IsEmpty) return;

            var sb = new StringBuilder();
            var drained = 0;

            while (drained < LogBatchSize && _logQueue.TryDequeue(out var entry))
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(entry);
                drained++;
            }

            if (sb.Length == 0) return;

            // Unico update sul thread UI
            var textToAppend = sb.ToString();
            var prefix = string.IsNullOrEmpty(OutputLog) ? "" : Environment.NewLine;
            OutputLog += prefix + textToAppend;

            // Cap della dimensione per evitare lentezza su stringhe enormi
            if (OutputLog != null && OutputLog.Length > LogMaxChars)
            {
                // Mantieni la parte finale (le ultime LogMaxChars)
                OutputLog = OutputLog.Substring(OutputLog.Length - LogMaxChars);
            }
        }

        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, string operationName, int maxAttempts = 5)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (APIException apiEx)
                {
                    AppendLog($"Attempt {attempt} to {operationName} failed: {apiEx.Message}");
                    if (attempt == maxAttempts) throw;

                    var delayMs = (int)(Math.Pow(2, attempt - 1) * 500); // 0.5s,1s,2s,4s...
                    await Task.Delay(delayMs);
                }
                catch (Exception ex)
                {
                    AppendLog($"Attempt {attempt} to {operationName} failed: {ex.Message}");
                    if (attempt == maxAttempts) throw;

                    var delayMs = (int)(Math.Pow(2, attempt - 1) * 500);
                    await Task.Delay(delayMs);
                }
            }

            // Non raggiungibile, ma richiesto dal compilatore
            return default!;
        }

        private string BuildPlaylistsPageApiOutput(PagingDto<PlaylistDto> page)
        {
            if (page == null) return "{ \"nullPage\": true }";

            var jo = new JObject
            {
                ["href"] = page.Href,
                ["limit"] = page.Limit,
                ["offset"] = page.Offset,
                ["total"] = page.Total,
                ["next"] = page.Next,
                ["previous"] = page.Previous,
                // Non includiamo l'array items per evitare i nomi delle playlist
                ["itemsCount"] = page.Items != null ? page.Items.Count : 0
            };

            return jo.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private int GetLoadedPlaylistsCount()
        {
            var count = 0;
            try
            {
                Application.Current?.Dispatcher?.Invoke((Action)(() =>
                {
                    count = Playlists.Count;
                }));
            }
            catch
            {
                // In caso di problemi con il dispatcher, restituiamo la stima basata sul set
                count = _playlistIds.Count;
            }

            return count;
        }

        private int? TryExtractRetryAfterSeconds(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;

            // Cerca "Retry-After" e poi la prima sequenza di cifre (secondi)
            var idx = message.IndexOf("Retry-After", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            // Salta fino alla prima cifra dopo "Retry-After"
            var i = idx;
            while (i < message.Length && !char.IsDigit(message[i])) i++;
            if (i >= message.Length) return null;

            var start = i;
            while (i < message.Length && char.IsDigit(message[i])) i++;

            var numberText = message.Substring(start, i - start);
            int seconds;
            if (int.TryParse(numberText, out seconds))
            {
                return seconds;
            }

            return null;
        }

        // Calcolo dinamico dei worker: 1 worker ogni 'unitSize' elementi (minimo 1)
        private int ComputeWorkerCount(int totalCount, int unitSize)
        {
            if (totalCount <= 0) return 1;
            if (unitSize <= 0) unitSize = 1;
            var workers = (int)Math.Ceiling(totalCount / (double)unitSize);
            return workers < 1 ? 1 : workers;
        }

        private void ValidatePlaylistsPageConsistency(PagingDto<PlaylistDto> page, int? expectedTotal, int pageIndex)
        {
            if (page == null)
            {
                throw new Exception("Null page received from Spotify API.");
            }

            var items = page.Items != null ? page.Items.Count : 0;
            // Converti in int in modo sicuro anche se le proprietà sono nullable
            int limit = Convert.ToInt32(page.Limit);
            int offset = Convert.ToInt32(page.Offset);
            int total = Convert.ToInt32(page.Total);
            var hasNext = !string.IsNullOrWhiteSpace(page.Next);

            // Regole generali
            if (items < 0 || limit < 0 || offset < 0)
                throw new Exception($"Invalid paging values (offset={offset}, limit={limit}, items={items}).");

            if (items > limit)
                throw new Exception($"Items count {items} exceeds limit {limit} at offset {offset}.");

            if (expectedTotal.HasValue && total == 0)
                throw new Exception($"Inconsistent total=0 after expected total={expectedTotal} at offset {offset}.");

            // Se sappiamo che mancano ancora elementi, una pagina vuota è incoerente
            if (expectedTotal.HasValue && (offset + limit) < expectedTotal.Value && items == 0)
                throw new Exception($"Empty page at offset {offset} while expectedTotal={expectedTotal} indicates more items.");

            // Regole specifiche per la prima pagina
            if (pageIndex == 0)
            {
                // Caso problematico: prima pagina completamente vuota e senza next -> transiente, va ritentata
                if (total == 0 && items == 0 && !hasNext)
                    throw new Exception("First page returned empty (total=0, items=0, next=null). Treating as transient failure.");

                // Se total > 0, ci aspettiamo items > 0
                if (total > 0 && items == 0)
                    throw new Exception("First page returned total>0 but items=0.");

                // Se total > limit ma next è null, è incoerente (terminazione anticipata)
                if (total > limit && !hasNext)
                    throw new Exception($"First page indicates more data (total={total}, limit={limit}) but next=null.");
            }
        }

        private void AddPlaylists(PagingDto<PlaylistDto>? playlists)
        {
            if (playlists?.Items == null)
            {
                return;
            }

            foreach (var playlist in playlists.Items)
            {
                if (playlist == null) continue;
                if (string.IsNullOrEmpty(playlist.Id))
                {
                    // A volte item nulli/incompleti: ignora
                    continue;
                }

                if (_playlistIds.Contains(playlist.Id))
                {
                    // Già presente in UI, salta
                    continue;
                }

                Playlists.Add(playlist);
                _playlistIds.Add(playlist.Id);
            }
        }

        public async Task LoadTracksAsync(PlaylistDto playlist)
        {
            StartBusy("Loading tracks...");

            Tracks.Clear();

            var tracks = await GetPlaylistTracksAsync(playlist.Id!, 0);
            var received = 0;

            while (true)
            {
                var itemsCount = tracks.Items?.Count ?? 0;
                received += itemsCount;

                var tracksToLoad = tracks;
                var disp = Application.Current?.Dispatcher;
                if (disp != null)
                {
                    await disp.BeginInvoke((Action)(() => { AddTracks(tracksToLoad); }));
                }
                else
                {
                    AddTracks(tracksToLoad);
                }

                var total = Convert.ToInt32(tracks.Total);
                if (received < total)
                {
                    tracks = await GetPlaylistTracksAsync(playlist.Id!, received);
                }
                else
                {
                    break;
                }
            }

            EndBusy();
        }

        private async Task<Paging<PlaylistTrack<IPlayableItem>>> GetPlaylistTracksAsync(string playlistId, int offset)
        {
            var req = new PlaylistGetItemsRequest()
            {
                Offset = offset,
                Limit = 100
            };

            await _spotify.EnsureAuthenticatedAsync();
            return await _spotify.Api!.Playlists.GetItems(playlistId, req);
        }

        private void AddTracks(IPaginatable<PlaylistTrack<IPlayableItem>> tracks)
        {
            if (tracks.Items == null)
            {
                return;
            }

            foreach (var track in tracks.Items)
            {
                Tracks.Add(_mapper.Map<Track>(track));
            }
        }

        private string BuildPlaylistWebUrl(PlaylistDto? playlist)
        {
            if (playlist == null) return string.Empty;
            var id = playlist.Id;
            if (string.IsNullOrWhiteSpace(id)) return string.Empty;
            return $"https://open.spotify.com/playlist/{id}";
        }

        private void TryOpenUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to open URL: {ex.Message}");
            }
        }

        private void TryCopyToClipboard(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetText(text);
                AppendLog("Link copied to clipboard.");
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to copy to clipboard: {ex.Message}");
            }
        }
    }
}