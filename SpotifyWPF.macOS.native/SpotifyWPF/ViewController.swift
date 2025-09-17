//
//  ViewController.swift
//  SpotifyWPF
//
//  Created by GitHub Copilot on 2025-09-15.
//  View controller for the main window with WebView
//

import Cocoa
import WebKit

class ViewController: NSViewController {

    @IBOutlet weak var webView: WKWebView!

    override func viewDidLoad() {
        super.viewDidLoad()

        // Configure WebView
        let configuration = WKWebViewConfiguration()
        configuration.preferences.javaScriptEnabled = true
        configuration.preferences.setValue(true, forKey: "allowFileAccessFromFileURLs")

        // Remove existing webView if it exists
        if let existingWebView = webView {
            existingWebView.removeFromSuperview()
        }

        // Create new WebView with proper configuration
        webView = WKWebView(frame: view.bounds, configuration: configuration)
        webView.autoresizingMask = [.width, .height]
        view.addSubview(webView)

        // Load the HTML file
        loadWebApp()
    }

    private func loadWebApp() {
        guard let webView = webView else { return }

        // Get the path to the WebApp directory
        if let webAppPath = Bundle.main.path(forResource: "index", ofType: "html", inDirectory: "WebApp") {
            let webAppURL = URL(fileURLWithPath: webAppPath)
            webView.loadFileURL(webAppURL, allowingReadAccessTo: webAppURL.deletingLastPathComponent())
        } else {
            // Fallback: try to load from absolute path (for development)
            let fallbackPath = "/Users/runner/work/SpotifyWPF/SpotifyWPF/SpotifyWPF.macOS.native/SpotifyWPF/WebApp/index.html"
            let fallbackURL = URL(fileURLWithPath: fallbackPath)
            if FileManager.default.fileExists(atPath: fallbackPath) {
                webView.loadFileURL(fallbackURL, allowingReadAccessTo: fallbackURL.deletingLastPathComponent())
            } else {
                // Last resort: load a simple HTML string
                let html = """
                <!DOCTYPE html>
                <html>
                <head>
                    <title>SpotifyWPF</title>
                </head>
                <body>
                    <h1>SpotifyWPF</h1>
                    <p>Loading WebApp...</p>
                </body>
                </html>
                """
                webView.loadHTMLString(html, baseURL: nil)
            }
        }
    }
}