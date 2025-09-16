// Spofyfy Player - Web Playback SDK Integration
class SpotifyPlayer {
    constructor() {
        this.player = null;
        this.deviceId = null;
        this.currentTrack = null;
        this.isPlaying = false;
        this.volume = 0.5;
        this.progressUpdateInterval = null;

        this.init();
    }

    init() {
        // Initialize Spotify Web Playback SDK
        window.onSpotifyWebPlaybackSDKReady = () => {
            this.initializePlayer();
        };

        // Load Spotify SDK if not already loaded
        if (!window.Spotify) {
            const script = document.createElement('script');
            script.src = 'https://sdk.scdn.co/spotify-player.js';
            document.head.appendChild(script);
        } else {
            this.initializePlayer();
        }
    }

    initializePlayer() {
        const token = localStorage.getItem('spotify_access_token');

        if (!token) {
            console.warn('No access token available for player initialization');
            return;
        }

        this.player = new Spotify.Player({
            name: 'Spofyfy Web Player',
            getOAuthToken: cb => { cb(token); },
            volume: this.volume
        });

        // Error handling
        this.player.addListener('initialization_error', ({ message }) => {
            console.error('Failed to initialize:', message);
        });

        this.player.addListener('authentication_error', ({ message }) => {
            console.error('Failed to authenticate:', message);
            // Clear invalid token
            localStorage.removeItem('spotify_access_token');
            window.location.reload();
        });

        this.player.addListener('account_error', ({ message }) => {
            console.error('Failed to validate Spotify account:', message);
        });

        this.player.addListener('playback_error', ({ message }) => {
            console.error('Failed to perform playback:', message);
        });

        // Playback status updates
        this.player.addListener('player_state_changed', async (state) => {
            if (!state) return;

            this.currentTrack = state.track_window.current_track;
            this.isPlaying = !state.paused;

            await this.updateUI(state);

            // Start/stop progress updates based on playback state
            if (!state.paused && !this.progressUpdateInterval) {
                this.startProgressUpdates();
            } else if (state.paused && this.progressUpdateInterval) {
                this.stopProgressUpdates();
            }
        });

        // Ready
        this.player.addListener('ready', ({ device_id }) => {
            console.log('Ready with Device ID', device_id);
            this.deviceId = device_id;
            this.transferPlayback();
        });

        // Not Ready
        this.player.addListener('not_ready', ({ device_id }) => {
            console.log('Device ID has gone offline', device_id);
            this.deviceId = null;
        });

        // Connect to the player
        this.player.connect().then(success => {
            if (success) {
                console.log('Successfully connected to Spotify!');
            } else {
                console.error('Failed to connect to Spotify');
            }
        });
    }

    async transferPlayback() {
        if (!this.deviceId) return;

        try {
            const token = localStorage.getItem('spotify_access_token');
            const response = await fetch('https://api.spotify.com/v1/me/player', {
                method: 'PUT',
                headers: {
                    'Authorization': `Bearer ${token}`,
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    device_ids: [this.deviceId],
                    play: false
                })
            });

            if (response.ok) {
                console.log('Playback transferred to this device');
            }
        } catch (error) {
            console.error('Failed to transfer playback:', error);
        }
    }

    async updateUI(state) {
        console.log('Player state update received:', {
            hasState: !!state,
            isPaused: state ? state.paused : 'N/A',
            position: state ? state.position : 'N/A',
            duration: state ? state.duration : 'N/A',
            currentTrack: state ? state.track_window?.current_track?.name : 'N/A'
        });

        // Log the full state object for debugging
        console.log('Full player state object:', JSON.stringify(state, null, 2));

        if (!state) return;

        const track = state.track_window.current_track;

        // Update track info
        document.getElementById('current-track-name').textContent = track.name;
        document.getElementById('current-track-artist').textContent =
            track.artists.map(artist => artist.name).join(', ');

        // Check for duration in track object
        console.log('Track duration check:', {
            trackDuration: track.duration_ms,
            stateDuration: state.duration,
            statePosition: state.position
        });

        // Update album art
        const albumArt = document.getElementById('current-track-art');
        if (track.album.images && track.album.images.length > 0) {
            albumArt.src = track.album.images[0].url;
            albumArt.style.display = 'block';
        } else {
            albumArt.style.display = 'none';
        }

        // Update play/pause button
        const playPauseBtn = document.getElementById('play-pause-btn');
        const icon = playPauseBtn.querySelector('i');

        if (state.paused) {
            icon.className = 'fas fa-play';
            playPauseBtn.classList.add('play-btn');
            playPauseBtn.classList.remove('pause-btn');
        } else {
            icon.className = 'fas fa-pause';
            playPauseBtn.classList.add('pause-btn');
            playPauseBtn.classList.remove('play-btn');
        }

        // Update all track buttons in playlist modal based on current playing track
        this.updateTrackButtonsState(track, state.paused);

        // If position or duration are missing, try to get current state
        let currentState = state;
        if ((!state.position || !state.duration) && this.player) {
            try {
                console.log('Position/duration missing, fetching current state...');
                const freshState = await this.player.getCurrentState();
                if (freshState) {
                    console.log('Got fresh state:', {
                        position: freshState.position,
                        duration: freshState.duration
                    });
                    currentState = freshState;
                }
            } catch (error) {
                console.error('Failed to get fresh state:', error);
            }
        }

        // Validate position and duration values
        const positionMs = (typeof currentState.position === 'number' && !isNaN(currentState.position)) ? currentState.position : 0;
        const durationMs = (typeof currentState.duration === 'number' && !isNaN(currentState.duration)) ? currentState.duration :
                          (typeof track.duration_ms === 'number' && !isNaN(track.duration_ms)) ? track.duration_ms : 0;

        console.log('Final position/duration values:', { positionMs, durationMs });

        // Update progress bar only if we have valid duration
        if (durationMs > 0) {
            const progress = (positionMs / durationMs) * 100;
            console.log('Updating progress bar:', { positionMs, durationMs, progress: progress + '%' });
            document.getElementById('progress-fill').style.width = `${Math.min(progress, 100)}%`;

            // Update progress dragger position
            const dragger = document.getElementById('progress-dragger');
            if (dragger) {
                dragger.style.left = `${Math.min(progress, 100)}%`;
            }
        } else {
            console.log('Invalid duration, hiding progress bar');
            // Hide progress bar if duration is invalid
            document.getElementById('progress-fill').style.width = '0%';
            const dragger = document.getElementById('progress-dragger');
            if (dragger) {
                dragger.style.left = '0%';
            }
        }

        // Update time displays with validation
        const currentTimeText = this.formatTime(positionMs);
        const totalTimeText = this.formatTime(durationMs);
        console.log('Updating time displays:', { currentTimeText, totalTimeText });
        document.getElementById('current-time').textContent = currentTimeText;
        document.getElementById('total-time').textContent = totalTimeText;

        // Enable/disable controls
        document.getElementById('prev-btn').disabled = !state.track_window.previous_tracks.length;
        document.getElementById('next-btn').disabled = !state.track_window.next_tracks.length;
    }

    formatTime(ms) {
        // Handle invalid inputs
        if (typeof ms !== 'number' || isNaN(ms) || ms < 0) {
            return '0:00';
        }

        const totalSeconds = Math.floor(ms / 1000);
        const minutes = Math.floor(totalSeconds / 60);
        const seconds = totalSeconds % 60;
        return `${minutes}:${seconds.toString().padStart(2, '0')}`;
    }

    // Public methods for controlling playback
    async playTrack(trackUri) {
        if (!this.player) return;

        try {
            await this.player.resume();
            // In a real implementation, you'd queue the specific track
            console.log('Playing track:', trackUri);
        } catch (error) {
            console.error('Failed to play track:', error);
        }
    }

    async togglePlayPause() {
        if (!this.player) return;

        try {
            if (this.isPlaying) {
                await this.player.pause();
            } else {
                await this.player.resume();
            }
        } catch (error) {
            console.error('Failed to toggle playback:', error);
        }
    }

    async nextTrack() {
        if (!this.player) return;

        try {
            await this.player.nextTrack();
        } catch (error) {
            console.error('Failed to skip to next track:', error);
        }
    }

    async previousTrack() {
        if (!this.player) return;

        try {
            await this.player.previousTrack();
        } catch (error) {
            console.error('Failed to go to previous track:', error);
        }
    }

    async setVolume(volume) {
        if (!this.player) {
            console.warn('No player instance available for setVolume');
            return;
        }

        try {
            this.volume = volume / 100;
            console.log('Setting player volume to:', this.volume);
            await this.player.setVolume(this.volume);

            // Update the volume slider UI to reflect the change
            const volumeSlider = document.getElementById('volume-slider');
            if (volumeSlider) {
                volumeSlider.value = volume;
                // Set CSS custom property for volume percentage
                volumeSlider.style.setProperty('--volume-percent', volume + '%');
                console.log('Updated volume slider UI to:', volume);
            }
        } catch (error) {
            console.error('Failed to set volume:', error);
        }
    }

    async seek(position) {
        if (!this.player) {
            console.warn('No player instance available for seek');
            return;
        }

        try {
            const state = await this.player.getCurrentState();
            if (state) {
                // Try to get duration from state, fallback to track duration
                const durationMs = state.duration || (state.track_window?.current_track?.duration_ms);
                if (durationMs) {
                    const seekMs = (position / 100) * durationMs;
                    console.log('Seeking to position:', seekMs, 'ms (', position, '% of', durationMs, 'ms)');
                    await this.player.seek(seekMs);
                } else {
                    console.warn('Cannot seek: no duration available');
                }
            } else {
                console.warn('Cannot seek: no player state available');
            }
        } catch (error) {
            console.error('Failed to seek:', error);
        }
    }

    // Update track buttons in playlist modal based on current playing track
    updateTrackButtonsState(currentTrack, isPaused) {
        if (!currentTrack) return;

        // Find all track play buttons in the playlist modal
        const trackButtons = document.querySelectorAll('.track-play-btn[data-track-uri]');

        trackButtons.forEach(button => {
            const buttonTrackUri = button.getAttribute('data-track-uri');
            const icon = button.querySelector('i');

            if (!icon) return;

            // Check if this button corresponds to the currently playing track
            if (buttonTrackUri === currentTrack.uri) {
                if (isPaused) {
                    // Track is paused - show play button
                    this.updateButtonState(button, 'play');
                } else {
                    // Track is playing - show pause button
                    this.updateButtonState(button, 'pause');
                }
            } else {
                // This is not the current track - show play button
                this.updateButtonState(button, 'play');
            }
        });
    }

    // Helper method to update individual button state
    updateButtonState(buttonElement, state) {
        const icon = buttonElement.querySelector('i');
        if (!icon) return;

        // Remove all state classes
        buttonElement.classList.remove('loading', 'playing', 'paused');
        icon.className = '';

        switch (state) {
            case 'loading':
                buttonElement.classList.add('loading');
                icon.className = 'fas fa-spinner fa-spin';
                buttonElement.title = 'Loading...';
                break;
            case 'play':
                icon.className = 'fas fa-play';
                buttonElement.title = 'Play';
                // Restore original onclick handler
                const trackUri = buttonElement.getAttribute('data-track-uri');
                if (trackUri) {
                    buttonElement.onclick = () => window.spotifyApp.playTrack(trackUri, buttonElement);
                }
                break;
            case 'pause':
                buttonElement.classList.add('playing');
                icon.className = 'fas fa-pause';
                buttonElement.title = 'Pause';
                // Update onclick to pause functionality
                buttonElement.onclick = () => window.spotifyApp.pauseTrack(buttonElement);
                break;
            default:
                icon.className = 'fas fa-play';
                buttonElement.title = 'Play';
        }
    }

    // Cleanup
    disconnect() {
        if (this.player) {
            this.player.disconnect();
        }
        this.stopProgressUpdates();
    }

    // Start periodic progress updates
    startProgressUpdates() {
        console.log('Starting progress updates');
        this.progressUpdateInterval = setInterval(async () => {
            if (this.player && !this.isPlaying) return; // Only update when playing

            try {
                const state = await this.player.getCurrentState();
                if (state) {
                    await this.updateProgressOnly(state);
                }
            } catch (error) {
                console.error('Failed to update progress:', error);
            }
        }, 1000); // Update every second
    }

    // Stop periodic progress updates
    stopProgressUpdates() {
        if (this.progressUpdateInterval) {
            console.log('Stopping progress updates');
            clearInterval(this.progressUpdateInterval);
            this.progressUpdateInterval = null;
        }
    }

    // Update only progress (for periodic updates)
    async updateProgressOnly(state) {
        if (!state) return;

        const track = state.track_window?.current_track;
        if (!track) return;

        // Validate position and duration values
        const positionMs = (typeof state.position === 'number' && !isNaN(state.position)) ? state.position : 0;
        const durationMs = (typeof state.duration === 'number' && !isNaN(state.duration)) ? state.duration :
                          (typeof track.duration_ms === 'number' && !isNaN(track.duration_ms)) ? track.duration_ms : 0;

        // Update progress bar only if we have valid duration
        if (durationMs > 0) {
            const progress = (positionMs / durationMs) * 100;
            document.getElementById('progress-fill').style.width = `${Math.min(progress, 100)}%`;

            // Update progress dragger position
            const dragger = document.getElementById('progress-dragger');
            if (dragger) {
                dragger.style.left = `${Math.min(progress, 100)}%`;
            }
        } else {
            // Hide progress bar if duration is invalid
            document.getElementById('progress-fill').style.width = '0%';
            const dragger = document.getElementById('progress-dragger');
            if (dragger) {
                dragger.style.left = '0%';
            }
        }

        // Update time displays
        document.getElementById('current-time').textContent = this.formatTime(positionMs);
        document.getElementById('total-time').textContent = this.formatTime(durationMs);
    }

    // Re-bind event listeners (call this after player is created)
    bindEventListeners() {
        console.log('Binding player event listeners...');
        const player = window.spotifyApp?.player;

        if (player) {
            console.log('Player found, setting up event listeners');

            // Play/Pause button
            document.getElementById('play-pause-btn').addEventListener('click', () => {
                console.log('Play/pause button clicked');
                player.togglePlayPause();
            });

            // Next/Previous buttons
            document.getElementById('next-btn').addEventListener('click', () => {
                console.log('Next button clicked');
                player.nextTrack();
            });

            document.getElementById('prev-btn').addEventListener('click', () => {
                console.log('Previous button clicked');
                player.previousTrack();
            });

            // Volume slider
            document.getElementById('volume-slider').addEventListener('input', (e) => {
                console.log('Volume slider changed:', e.target.value);
                player.setVolume(e.target.value);
            });

            // Progress bar
            document.querySelector('.progress-bar').addEventListener('click', (e) => {
                console.log('Progress bar clicked');
                const rect = e.currentTarget.getBoundingClientRect();
                const position = ((e.clientX - rect.left) / rect.width) * 100;
                console.log('Seeking to position:', position);
                player.seek(position);
            });

            // Progress dragger drag functionality
            const progressDragger = document.getElementById('progress-dragger');
            if (progressDragger) {
                let isDragging = false;

                progressDragger.addEventListener('mousedown', (e) => {
                    isDragging = true;
                    e.preventDefault();
                    console.log('Started dragging progress dragger');
                });

                document.addEventListener('mousemove', (e) => {
                    if (!isDragging) return;

                    const progressBar = document.querySelector('.progress-bar');
                    const rect = progressBar.getBoundingClientRect();
                    const position = Math.max(0, Math.min(100, ((e.clientX - rect.left) / rect.width) * 100));

                    // Update visual position immediately
                    progressDragger.style.left = `${position}%`;
                    document.getElementById('progress-fill').style.width = `${position}%`;
                });

                document.addEventListener('mouseup', (e) => {
                    if (!isDragging) return;

                    isDragging = false;
                    console.log('Finished dragging progress dragger');

                    const progressBar = document.querySelector('.progress-bar');
                    const rect = progressBar.getBoundingClientRect();
                    const position = Math.max(0, Math.min(100, ((e.clientX - rect.left) / rect.width) * 100));

                    console.log('Seeking to dragged position:', position);
                    player.seek(position);
                });
            }

            console.log('Event listeners bound successfully');
        } else {
            console.warn('Player not found when binding event listeners');
        }
    }
}

// Bind player controls to UI
document.addEventListener('DOMContentLoaded', () => {
    const player = window.spotifyApp?.player;

    if (player) {
        // Play/Pause button
        document.getElementById('play-pause-btn').addEventListener('click', () => {
            player.togglePlayPause();
        });

        // Next/Previous buttons
        document.getElementById('next-btn').addEventListener('click', () => {
            player.nextTrack();
        });

        document.getElementById('prev-btn').addEventListener('click', () => {
            player.previousTrack();
        });

        // Volume slider
        document.getElementById('volume-slider').addEventListener('input', (e) => {
            console.log('Volume slider changed:', e.target.value);
            player.setVolume(e.target.value);
        });

        // Progress bar
        document.querySelector('.progress-bar').addEventListener('click', (e) => {
            console.log('Progress bar clicked');
            const rect = e.currentTarget.getBoundingClientRect();
            const position = ((e.clientX - rect.left) / rect.width) * 100;
            console.log('Seeking to position:', position);
            player.seek(position);
        });
    }
});