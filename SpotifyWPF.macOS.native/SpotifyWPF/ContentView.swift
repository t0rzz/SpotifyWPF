//
//  ContentView.swift
//  SpotifyWPF
//
//  Created by GitHub Copilot on 2025-09-21.
//

import SwiftUI
import WebKit

struct ContentView: View {
    var body: some View {
        WebView()
            .frame(minWidth: 1200, minHeight: 800)
    }
}

struct WebView: NSViewRepresentable {
    func makeNSView(context: Context) -> WKWebView {
        let configuration = WKWebViewConfiguration()
        configuration.preferences.setValue(true, forKey: "allowFileAccessFromFileURLs")
        
        let webView = WKWebView(frame: .zero, configuration: configuration)
        webView.navigationDelegate = context.coordinator
        
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
        // No updates needed
    }
    
    func makeCoordinator() -> Coordinator {
        Coordinator()
    }
    
    class Coordinator: NSObject, WKNavigationDelegate {
        func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
            if let url = navigationAction.request.url {
                // Check if this is an external URL (not file://)
                if url.scheme == "https" || url.scheme == "http" {
                    // Open external URLs in the default browser
                    NSWorkspace.shared.open(url)
                    decisionHandler(.cancel)
                    return
                }
            }
            // Allow file:// URLs and other internal navigation
            decisionHandler(.allow)
        }
    }
}