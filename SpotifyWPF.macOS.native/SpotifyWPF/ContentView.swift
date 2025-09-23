//
//  ContentView.swift
//  SpotifyWPF
//
//  Created by GitHub Copilot on 2025-09-21.
//

import SwiftUI
import WebKit

struct ContentView: View {
    @State private var callbackURL: URL?
    
    var body: some View {
        WebView(callbackURL: callbackURL)
            .frame(minWidth: 1200, minHeight: 800)
            .onOpenURL { url in
                print("📱 ContentView received URL via onOpenURL: \(url)")
                callbackURL = url
            }
            .onReceive(NotificationCenter.default.publisher(for: .spotifyCallback)) { notification in
                if let url = notification.userInfo?["url"] as? URL {
                    print("📱 ContentView received URL via notification: \(url)")
                    callbackURL = url
                }
            }
    }
}

struct WebView: NSViewRepresentable {
    let callbackURL: URL?
    
    init(callbackURL: URL? = nil) {
        self.callbackURL = callbackURL
    }
    
    func makeNSView(context: Context) -> WKWebView {
        let configuration = WKWebViewConfiguration()
        configuration.preferences.setValue(true, forKey: "allowFileAccessFromFileURLs")
        
        let webView = WKWebView(frame: .zero, configuration: configuration)
        webView.navigationDelegate = context.coordinator
        
        // Enable Safari Web Inspector for debugging
        if #available(macOS 13.3, *) {
            webView.isInspectable = true
        }
        
        // Load index.html from WebApp bundle
        if let webAppURL = Bundle.main.url(forResource: "index", withExtension: "html", subdirectory: "WebApp") {
            let webAppDirectory = webAppURL.deletingLastPathComponent()
            webView.loadFileURL(webAppURL, allowingReadAccessTo: webAppDirectory)
        } else {
            // Fallback: load a simple message
            let html = """
            <!DOCTYPE html>
            <html>
            <head>
                <title>SpotifyWPF</title>
            </head>
            <body>
                <h1>WebApp not found</h1>
                <p>Please ensure WebApp folder is included in the bundle.</p>
            </body>
            </html>
            """
            webView.loadHTMLString(html, baseURL: nil)
        }
        
        return webView
    }
    
    func updateNSView(_ nsView: WKWebView, context: Context) {
        // Update the coordinator with the new callback URL
        context.coordinator.callbackURL = callbackURL
        // If we have a callback URL and haven't injected yet, inject it
        if callbackURL != nil && !context.coordinator.hasInjected {
            context.coordinator.injectCallback(into: nsView)
        }
    }
    
    func makeCoordinator() -> Coordinator {
        Coordinator(callbackURL: callbackURL)
    }
    
    class Coordinator: NSObject, WKNavigationDelegate {
        var callbackURL: URL?
        var hasInjected = false
        
        init(callbackURL: URL?) {
            self.callbackURL = callbackURL
        }
        
        func injectCallback(into webView: WKWebView) {
            guard let callbackURL = self.callbackURL, !hasInjected else { return }
            print("🔗 Injecting callback URL into WebApp: \(callbackURL)")
            let script = """
            if (window.handleCallback) {
                window.handleCallback('\(callbackURL.absoluteString)');
            }
            """
            webView.evaluateJavaScript(script) { result, error in
                if let error = error {
                    print("❌ Error injecting callback URL: \(error)")
                } else {
                    print("✅ Callback URL injected successfully")
                    self.hasInjected = true
                }
            }
        }
        
        func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
            // Inject callback if available and not already injected
            injectCallback(into: webView)
        }
        
        func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
            if let url = navigationAction.request.url {
                // Check if this is a Spotify authorization URL that should open in browser
                if url.scheme == "https" && url.host == "accounts.spotify.com" && url.path.hasPrefix("/authorize") {
                    // Open Spotify authorization in the default browser
                    NSWorkspace.shared.open(url)
                    decisionHandler(.cancel)
                    return
                }
                
                // Check if this is a redirect back to our app (callback URL)
                if url.scheme == "spofifywpf" || (url.scheme == "http" && url.host == "localhost") {
                    // Handle callback URL - this should be processed by the WebApp
                    decisionHandler(.allow)
                    return
                }
            }
            // Allow all other navigation (including script loading, local files, etc.)
            decisionHandler(.allow)
        }
    }
}