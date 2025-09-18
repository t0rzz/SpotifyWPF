//
//  AppDelegate.swift
//  SpotifyWPF
//
//  Created by GitHub Copilot on 2025-09-15.
//  Simple macOS app that displays HTML content in WKWebView
//

import Cocoa
import WebKit

class AppDelegate: NSObject, NSApplicationDelegate {
    
    var window: NSWindow!
    var webView: WKWebView!
    
    func applicationDidFinishLaunching(_ aNotification: Notification) {
        print("🚀 AppDelegate: Application starting...")
        
        // Create the main window
        window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 1200, height: 800),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        
        window.center()
        window.title = "Spofify"
        window.minSize = NSSize(width: 800, height: 600)
        
        print("📱 Creating WKWebView...")
        
        // Create WKWebView configuration
        let configuration = WKWebViewConfiguration()
        configuration.preferences.setValue(true, forKey: "allowFileAccessFromFileURLs")
        
        // Create WKWebView with proper frame (fill the entire window content area)
        webView = WKWebView(frame: window.contentView!.bounds, configuration: configuration)
        webView.autoresizingMask = [.width, .height]
        
        // Add web view to window
        window.contentView?.addSubview(webView)
        
        print("🌐 Loading HTML content...")
        
        // Load the HTML file
        loadWebApp()
        
        // Show the window
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        
        print("✅ Window should now be visible with web content")
    }
    
    private func loadWebApp() {
        // Try to find and load the HTML file
        if let htmlPath = Bundle.main.path(forResource: "index", ofType: "html", inDirectory: "WebApp") {
            let htmlURL = URL(fileURLWithPath: htmlPath)
            print("📁 Found HTML file at: \(htmlPath)")
            
            webView.loadFileURL(htmlURL, allowingReadAccessTo: htmlURL.deletingLastPathComponent())
            print("🔄 Loading HTML from: \(htmlURL)")
        } else {
            print("❌ Could not find index.html in WebApp directory")
            
            // Try fallback location
            let webAppURL = Bundle.main.bundleURL.appendingPathComponent("Contents/Resources/WebApp/index.html")
            if FileManager.default.fileExists(atPath: webAppURL.path) {
                print("📁 Found HTML file at fallback location: \(webAppURL.path)")
                webView.loadFileURL(webAppURL, allowingReadAccessTo: webAppURL.deletingLastPathComponent())
            } else {
                print("❌ HTML file not found at any location")
                loadFallbackHTML()
            }
        }
    }
    
    private func loadFallbackHTML() {
        let fallbackHTML = """
        <!DOCTYPE html>
        <html>
        <head>
            <title>Spotify WPF - Error</title>
            <style>
                body {
                    font-family: -apple-system, BlinkMacSystemFont, sans-serif;
                    text-align: center;
                    padding: 50px;
                    background: linear-gradient(135deg, #1DB954, #191414);
                    color: white;
                    margin: 0;
                    height: 100vh;
                    display: flex;
                    flex-direction: column;
                    justify-content: center;
                    align-items: center;
                }
                h1 { color: #1DB954; margin-bottom: 20px; }
                .error-box {
                    background: rgba(255, 255, 255, 0.1);
                    padding: 30px;
                    border-radius: 10px;
                    max-width: 500px;
                }
                .status { color: #ff6b6b; margin: 10px 0; }
            </style>
        </head>
        <body>
            <div class="error-box">
                <h1>🎵 Spotify WPF</h1>
                <p class="status">❌ HTML file not found</p>
                <p>Check that index.html exists in the WebApp directory</p>
                <p><small>Debug: WebView is working, but content couldn't be loaded</small></p>
            </div>
        </body>
        </html>
        """
        webView.loadHTMLString(fallbackHTML, baseURL: nil)
        print("🔄 Loaded fallback HTML")
    }
    
    func applicationWillTerminate(_ aNotification: Notification) {
        print("👋 Application terminating")
    }
    
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return true
    }
}