/**
 * Spotify Web Playback SDK Bridge for C# WebView2 integration
 * Provides exact message contract as specified in requirements
 */

(function() {
    'use strict';
    
    let player = null;
    let deviceId = null;
    let accessToken = null;
    let isReady = false;
    let isSDKReady = false; // Track if SDK is ready
    let lastStateSent = null; // Track last state sent to avoid spam
    let stateThrottleTimeout = null; // Throttle state messages
    let currentVolume = 1.0; // Cache volume explicitly; SDK state doesn't expose volume reliably
    
    // Ensure window.chrome.webview is available for WebView2 communication
    if (!window.chrome || !window.chrome.webview) {
        console.error('WebView2 environment not detected');
        return;
    }

    const playerBridge = {
        // Initialize with access token from C#
        initialize: function(token) {
            accessToken = token;
            console.log('PlayerBridge initialized with token:', accessToken.substring(0, 20) + '...');
            console.log('isSDKReady:', isSDKReady); // Debug log
            updateStatus('Token received, checking SDK...');
            
            // If SDK is already ready, create the player now
            if (isSDKReady) {
                console.log('SDK already ready, creating player now...');
                this.createPlayer();
            } else {
                console.log('SDK not ready yet, waiting...');
                updateStatus('Token received, waiting for SDK...');
            }
        },

        // Called when Spotify SDK is ready
        onSDKReady: function() {
            isSDKReady = true;
            console.log('Spotify SDK is ready, isSDKReady set to true');
            console.log('window.Spotify:', typeof window.Spotify);
            console.log('window.Spotify.Player:', typeof window.Spotify?.Player);
            
            // If we already have the token, create the player now
            if (accessToken) {
                console.log('Token already available, creating player now...');
                this.createPlayer();
            } else {
                console.log('SDK ready, waiting for access token...');
                updateStatus('SDK ready, waiting for token...');
            }
        },

        // New method to create the player when both SDK and token are ready
        createPlayer: function() {
            console.log('createPlayer called - accessToken:', !!accessToken, 'isSDKReady:', isSDKReady);
            
            if (!accessToken) {
                console.error('Cannot create player: No access token available');
                updateStatus('Error: No access token');
                return;
            }
            
            if (!isSDKReady) {
                console.error('Cannot create player: SDK not ready');
                updateStatus('Error: SDK not ready');
                return;
            }

            console.log('Creating Spotify player...');
            updateStatus('Creating player...');

            // Create Spotify player instance
            player = new Spotify.Player({
                name: 'SpotifyWPF Web Player',
                getOAuthToken: cb => { cb(accessToken); },
                volume: 1.0  // Set volume to maximum
            });

            // Error handling
            player.addListener('initialization_error', ({ message }) => {
                console.error('Initialization Error:', message);
                updateStatus('Initialization Error: ' + message);
            });

            player.addListener('authentication_error', ({ message }) => {
                console.error('🚨 Authentication Error:', message);
                console.error('📋 COMMON CAUSES:');
                console.error('1. Account is not Spotify Premium (Web Playback SDK requires Premium)');
                console.error('2. Token expired or invalid');
                console.error('3. Client ID not registered for Web Playback SDK');
                console.error('4. User-Agent not supported by Spotify');
                updateStatus('Authentication Error: ' + message);
            });

            player.addListener('account_error', ({ message }) => {
                console.error('Account Error:', message);
                updateStatus('Account Error: ' + message);
            });

            player.addListener('playback_error', ({ message }) => {
                console.error('Playback Error:', message);
                updateStatus('Playback Error: ' + message);
            });

            // Ready event - device ID available
            player.addListener('ready', ({ device_id }) => {
                console.log('Player ready with device ID:', device_id);
                deviceId = device_id;
                isReady = true;
                updateStatus('Player ready');
                
                // Post ready message to C# with exact contract
                postMessage({
                    type: 'ready',
                    device_id: device_id
                });
            });

            // Not ready
            player.addListener('not_ready', ({ device_id }) => {
                console.log('Player not ready:', device_id);
                isReady = false;
                updateStatus('Player not ready');
            });

                        // Player state changed - CRITICAL for external control
            player.addListener('player_state_changed', (state) => {
                if (!state) {
                    console.log('Player state: no state');
                    postMessage({
                        type: 'state',
                        state: null
                    });
                    return;
                }

                // Throttle state messages to avoid spam (max once per 500ms)
                if (stateThrottleTimeout) {
                    clearTimeout(stateThrottleTimeout);
                }
                
                stateThrottleTimeout = setTimeout(() => {
                    this.sendPlayerStateToCSharp(state);
                }, 500);
            });

            updateStatus('Player created, connecting...');
            
            // Connect the player immediately
            player.connect().then(success => {
                if (success) {
                    console.log('Player connected successfully');
                    updateStatus('Connected and ready');
                    
                    // Initialize volume cache from SDK; do not force a value
                    if (typeof player.getVolume === 'function') {
                        player.getVolume().then(v => {
                            if (typeof v === 'number') {
                                currentVolume = v;
                                console.log('Initial player volume:', currentVolume);
                            }
                        }).catch(() => { /* ignore */ });
                    }
                    // Do not force volume here; defer to UI/host app
                    
                    // 🔥 CRITICAL: Start polling for external control detection
                    this.startStatePolling();
                    
                    // Initialize audio context for WebView2
                    try {
                        const AudioContext = window.AudioContext || window.webkitAudioContext;
                        if (!window.audioContextInitialized && AudioContext) {
                            const audioContext = new AudioContext();
                            if (audioContext.state === 'suspended') {
                                audioContext.resume().then(() => {
                                    console.log('Audio context resumed on connect');
                                });
                            }
                            window.audioContextInitialized = true;
                            console.log('Audio context initialized on connect');
                        }
                    } catch (e) {
                        console.log('Could not initialize audio context on connect:', e);
                    }
                } else {
                    console.error('Failed to connect player');
                    updateStatus('Connection failed');
                }
            }).catch(error => {
                console.error('Error connecting player:', error);
                updateStatus('Connection error: ' + error);
            });
        },

        // Connect player (called from C#)
        connect: function() {
            if (!player) {
                console.error('Player not initialized');
                return Promise.reject('Player not initialized');
            }

            return player.connect().then(success => {
                if (success) {
                    console.log('Player connected successfully');
                    updateStatus('Connected');
                } else {
                    console.error('Failed to connect player');
                    updateStatus('Connection failed');
                }
                return success;
            });
        },

        // Play tracks (called from C#)
        play: function(uris) {
            console.log('=== PLAY METHOD CALLED ===');
            console.log('isReady:', isReady);
            console.log('deviceId:', deviceId);
            console.log('accessToken:', accessToken ? 'present' : 'missing');
            
            if (!isReady || !deviceId) {
                console.error('Player not ready - isReady:', isReady, 'deviceId:', deviceId);
                return Promise.reject('Player not ready');
            }

            console.log('Playing tracks:', uris);
            
            // CRITICAL: Force user interaction gesture for audio
            this.enableAudioContext();
            
            // Force audio context activation first
            if (player && typeof player.activateElement === 'function') {
                try {
                    player.activateElement();
                    console.log('Audio element activated');
                } catch (e) {
                    console.log('Could not activate audio element:', e);
                }
            }
            
            // Use Spotify Web API to start playbook
            const apiUrl = 'https://api.spotify.com/v1/me/player/play?device_id=' + deviceId;
            const requestBody = JSON.stringify({ uris: uris });
            
            console.log('=== API CALL DEBUG ===');
            console.log('API URL:', apiUrl);
            console.log('Request body:', requestBody);
            console.log('Authorization header:', `Bearer ${accessToken.substring(0, 10)}...`);
            
            return fetch(apiUrl, {
                method: 'PUT',
                body: requestBody,
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': `Bearer ${accessToken}`
                },
            }).then(response => {
                console.log('=== API RESPONSE ===');
                console.log('Status:', response.status);
                console.log('OK:', response.ok);
                
                if (response.ok) {
                    console.log('Playback started successfully');
                    
                    // CRITICAL: Check if player is ready for audio playback
                    if (player) {
                        console.log('=== PLAYER AUDIO DEBUG ===');
                        console.log('Player object exists:', !!player);
                        console.log('Player connected:', player._isReady || 'unknown');
                        
                        // Get current state to verify playback
                        player.getCurrentState().then(state => {
                            if (state) {
                                console.log('Player state after API call:');
                                console.log('- Track:', state.track_window.current_track.name);
                                console.log('- Paused:', state.paused);
                                console.log('- Position:', state.position);
                                console.log('- Volume (cached):', currentVolume);
                                console.log('- Context:', state.context);
                            } else {
                                console.error('❌ Player state is NULL - player not active!');
                                console.log('🔄 Attempting to reconnect player...');
                                
                                // Try to reconnect the player
                                player.connect().then(success => {
                                    if (success) {
                                        console.log('✅ Player reconnected successfully');
                                        // Try to get state again after reconnection
                                        setTimeout(() => {
                                            player.getCurrentState().then(newState => {
                                                if (newState) {
                                                    console.log('✅ Player state available after reconnection');
                                                } else {
                                                    console.log('⚠️ Player state still NULL after reconnection');
                                                }
                                            });
                                        }, 1000);
                                    } else {
                                        console.error('❌ Player reconnection failed');
                                    }
                                }).catch(error => {
                                    console.error('❌ Error reconnecting player:', error);
                                });
                            }
                        }).catch(error => {
                            console.error('❌ Error getting player state:', error);
                        });
                        
                        // Force resume if paused
                        player.resume().then(() => {
                            console.log('✅ Player.resume() called successfully');
                            
                            // CRITICAL: Force audio context activation after resume
                            if (typeof window.AudioContext !== 'undefined' || typeof window.webkitAudioContext !== 'undefined') {
                                try {
                                    const AudioContext = window.AudioContext || window.webkitAudioContext;
                                    if (!window.spotifyAudioContext) {
                                        window.spotifyAudioContext = new AudioContext();
                                        console.log('🔊 Audio context created for Spotify playback');
                                    }
                                    
                                    if (window.spotifyAudioContext.state === 'suspended') {
                                        window.spotifyAudioContext.resume().then(() => {
                                            console.log('🔊 Audio context resumed successfully');
                                        });
                                    }
                                } catch (e) {
                                    console.error('❌ Audio context activation failed:', e);
                                }
                            }
                        }).catch(error => {
                            console.error('❌ Player.resume() failed:', error);
                        });
                    } else {
                        console.error('❌ Player object is NULL!');
                    }
                    
                    // Do not force volume after playback; respect user setting
                    
                    return { success: true };
                } else {
                    console.error('=== API ERROR ===');
                    console.error('Failed to start playback. Status:', response.status);
                    
                    // Try to get error details
                    return response.text().then(errorText => {
                        console.error('Error response body:', errorText);
                        return { success: false, error: `HTTP ${response.status}: ${response.statusText}`, details: errorText };
                    }).catch(() => {
                        return { success: false, error: `HTTP ${response.status}: ${response.statusText}` };
                    });
                }
            }).catch(error => {
                console.error('=== NETWORK ERROR ===');
                console.error('Error starting playback:', error);
                return { success: false, error: error.message };
            });
        },

        // Enable audio context - CRITICAL for modern browsers
        enableAudioContext: function() {
            try {
                if (!window.audioContextInitialized) {
                    const AudioContext = window.AudioContext || window.webkitAudioContext;
                    if (AudioContext) {
                        const audioContext = new AudioContext();
                        if (audioContext.state === 'suspended') {
                            audioContext.resume().then(() => {
                                console.log('Audio context resumed successfully');
                            });
                        }
                        window.audioContextInitialized = true;
                        console.log('Audio context enabled by user interaction');
                    }
                }
                
                // Additional web audio activation for Chrome/Edge
                if (player && typeof player.activateElement === 'function') {
                    player.activateElement();
                }
            } catch (e) {
                console.log('Audio context activation failed:', e);
            }
        },

        // Pause playback
        pause: function() {
            if (!player) {
                console.error('Player not initialized');
                return Promise.reject('Player not initialized');
            }

            console.log('=== PAUSE CALLED ===');
            // Enable audio context on any user interaction
            this.enableAudioContext();
            return player.pause().then(() => {
                console.log('✅ Player paused successfully');
                return { success: true };
            }).catch(error => {
                console.error('❌ Error pausing:', error);
                return { success: false, error: error.message };
            });
        },

        // Resume playbook
        resume: function() {
            if (!player) {
                console.error('Player not initialized');
                return Promise.reject('Player not initialized');
            }

            console.log('=== RESUME CALLED ===');
            // Enable audio context on any user interaction
            this.enableAudioContext();

            return player.resume().then(() => {
                console.log('✅ Playback resumed successfully');
                return { success: true };
            }).catch(error => {
                console.error('❌ Error resuming:', error);
                return { success: false, error: error.message };
            });
        },

        // Seek to position
        seek: function(positionMs) {
            if (!player) {
                console.error('Player not initialized');
                return Promise.reject('Player not initialized');
            }

            return player.seek(positionMs).then(() => {
                console.log('Seeked to position:', positionMs);
                return { success: true };
            }).catch(error => {
                console.error('Error seeking:', error);
                return { success: false, error: error.message };
            });
        },

        // Set volume
        setVolume: function(volume) {
            if (!player) {
                console.error('Player not initialized');
                return Promise.reject('Player not initialized');
            }

            // Normalize/clamp volume to [0,1] and snap tiny values to 0
            let v = typeof volume === 'number' ? volume : parseFloat(volume);
            if (!isFinite(v)) v = 0;
            v = Math.max(0, Math.min(1, v));
            if (v > 0 && v < 0.005) v = 0;

            console.log('🔊 Setting volume to:', v);
            // Ensure audio context is active so volume changes take effect reliably
            try { this.enableAudioContext(); } catch {}
            
            return player.setVolume(v).then(() => {
                console.log('✅ Volume set successfully to:', volume);
                currentVolume = v; // Update cached volume
                return { success: true };
            }).catch(error => {
                console.error('❌ Error setting volume:', error);
                return { success: false, error: error.message };
            });
        },

        // Get current state
        getState: function() {
            if (!player) {
                console.error('Player not initialized');
                return Promise.reject('Player not initialized');
            }

            return player.getCurrentState().then(state => {
                if (!state) {
                    console.log('No current state available');
                    return null;
                }

                const currentTrack = state.track_window.current_track;
                return {
                    trackId: currentTrack ? currentTrack.id : null,
                    position_ms: state.position,
                    paused: state.paused,
                    duration_ms: currentTrack ? currentTrack.duration_ms : 0,
                    volume: currentVolume
                };
            }).catch(error => {
                console.error('Error getting state:', error);
                return null;
            });
        },

        // Send player state to C# with throttling and deduplication
        sendPlayerStateToCSharp: function(state) {
            if (!state) return;
            
            console.log('🎵 EVENT: sendPlayerStateToCSharp called');
            
            // Check if this state is significantly different from the last one sent
            const currentTrack = state.track_window.current_track;
            const stateKey = `${currentTrack ? currentTrack.id : 'null'}_${state.paused}_${Math.floor(state.position / 1000)}`;
            
            // Only send if state has meaningfully changed
            if (lastStateSent === stateKey) {
                console.log('🎵 EVENT: State unchanged, skipping send');
                return;
            }
            
            console.log('🎵 EVENT: EXTERNAL CONTROL - Player state changed:', state);
            
            // Respect currentVolume; do not override user's choice here
            
            const previousTrack = state.track_window.previous_tracks[0];
            const nextTrack = state.track_window.next_tracks[0];
            
            const stateData = {
                trackId: currentTrack ? currentTrack.id : null,
                trackName: currentTrack ? currentTrack.name : null,
                artists: currentTrack ? currentTrack.artists.map(a => a.name).join(', ') : null,
                album: currentTrack ? currentTrack.album.name : null,
                imageUrl: currentTrack && currentTrack.album.images.length > 0 ? currentTrack.album.images[0].url : null,
                position_ms: state.position,
                paused: state.paused,
                duration_ms: currentTrack ? currentTrack.duration_ms : 0,
                volume: currentVolume,
                shuffled: state.shuffle,
                repeat_mode: state.repeat_mode,
                isPlaying: !state.paused,
                hasNextTrack: !!nextTrack,
                hasPreviousTrack: !!previousTrack,
                timestamp: Date.now()
            };

            console.log('📡 EVENT: Sending comprehensive state to C#:', stateData);

            // Post state change to C# with exact contract + enhanced data
            postMessage({
                type: 'state',
                state: stateData
            });

            // Update last sent state
            lastStateSent = stateKey;

            // If playback just started from external control, ensure audio context
            if (!state.paused && state.position > 0) {
                console.log('🎵 EVENT: External playback detected, ensuring audio context...');
                this.enableAudioContext();
            }
        },

        // 🔥 CRITICAL: State polling for external control detection
        startStatePolling: function() {
            console.log('🔄 Starting state polling for external control detection...');
            
            let lastState = null;
            let lastPosition = 0;
            let lastTrackId = null;
            let lastPaused = null;
            
            const pollInterval = setInterval(() => {
                if (!player || !isReady) {
                    console.log('🔄 State polling: player not ready, skipping...');
                    return;
                }
                
                player.getCurrentState().then(state => {
                    if (!state) {
                        console.log('🔄 State polling: no active playback state');
                        return; // No playback active
                    }
                    
                    const currentTrack = state.track_window.current_track;
                    const currentTrackId = currentTrack ? currentTrack.id : null;
                    const currentPaused = state.paused;
                    const currentPosition = state.position;
                    
                    console.log(`🔄 State polling: Track=${currentTrackId}, Paused=${currentPaused}, Position=${currentPosition}`);
                    
                    // Check for significant changes that indicate external control
                    const trackChanged = lastTrackId && currentTrackId !== lastTrackId;
                    const pausedChanged = lastPaused !== null && currentPaused !== lastPaused;
                    const positionJump = lastPosition > 0 && Math.abs(currentPosition - lastPosition) > 5000; // >5 second jump
                    
                    if (trackChanged || pausedChanged || positionJump) {
                        console.log('🎵 EXTERNAL CONTROL DETECTED:', {
                            trackChanged,
                            pausedChanged,
                            positionJump,
                            oldTrack: lastTrackId,
                            newTrack: currentTrackId,
                            oldPaused: lastPaused,
                            newPaused: currentPaused,
                            oldPosition: lastPosition,
                            newPosition: currentPosition
                        });
                        
                        // Force state update to C#
                        this.sendCurrentStateToCSharp(state);
                        
                        // Enable audio context if playback started
                        if (lastPaused && !currentPaused) {
                            console.log('🔊 External play detected, enabling audio...');
                            this.enableAudioContext();
                        }
                    }
                    
                    // Update tracking variables
                    lastState = state;
                    lastTrackId = currentTrackId;
                    lastPaused = currentPaused;
                    lastPosition = currentPosition;
                    
                }).catch(error => {
                    console.error('Error in state polling:', error);
                });
                
            }, 1000); // Poll every 1 second
            
            console.log('✅ State polling started (1 second interval)');
            
            // Store interval ID for cleanup
            window.statePollingInterval = pollInterval;
        },

        // Helper method to send current state to C#
        sendCurrentStateToCSharp: function(state) {
            if (!state) return;
            
            console.log('🔄 POLLING: sendCurrentStateToCSharp called');
            
            const currentTrack = state.track_window.current_track;
            const previousTrack = state.track_window.previous_tracks[0];
            const nextTrack = state.track_window.next_tracks[0];
            
            const stateData = {
                trackId: currentTrack ? currentTrack.id : null,
                trackName: currentTrack ? currentTrack.name : null,
                artists: currentTrack ? currentTrack.artists.map(a => a.name).join(', ') : null,
                album: currentTrack ? currentTrack.album.name : null,
                imageUrl: currentTrack && currentTrack.album.images.length > 0 ? currentTrack.album.images[0].url : null,
                position_ms: state.position,
                paused: state.paused,
                duration_ms: currentTrack ? currentTrack.duration_ms : 0,
                volume: currentVolume,
                shuffled: state.shuffle,
                repeat_mode: state.repeat_mode,
                isPlaying: !state.paused,
                hasNextTrack: !!nextTrack,
                hasPreviousTrack: !!previousTrack,
                timestamp: Date.now()
            };

            console.log('📡 POLLING: Sending comprehensive state to C#:', stateData);

            postMessage({
                type: 'state',
                state: stateData
            });
        }
    };

    // Helper function to post messages to C#
    function postMessage(message) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(JSON.stringify(message));
        }
    }

    // Helper function to update status
    function updateStatus(status) {
        console.log('Status:', status);
        // Try to update status div if it exists
        const statusElement = document.getElementById('status');
        if (statusElement) {
            statusElement.textContent = status;
        }
    }

    // Make playerBridge available globally
    window.playerBridge = playerBridge;
    window.spotifyBridge = playerBridge; // Alias for C# compatibility
    
    // Debug: verify the bridge is properly exposed
    console.log('=== BRIDGE EXPOSED ===');
    console.log('window.spotifyBridge:', typeof window.spotifyBridge);
    console.log('window.spotifyBridge.play:', typeof window.spotifyBridge.play);

    // Global function to enable audio (can be called from UI interactions)
    window.enableSpotifyAudio = function() {
        if (playerBridge && typeof playerBridge.enableAudioContext === 'function') {
            playerBridge.enableAudioContext();
            console.log('Audio context enabled via global function');
            return true;
        }
        return false;
    };

    console.log('Spotify Player Bridge loaded');
    updateStatus('Bridge loaded, waiting for initialization...');
})();
