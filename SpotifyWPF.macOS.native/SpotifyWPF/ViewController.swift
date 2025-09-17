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

        // Create WebView if outlet is not connected
        if webView == nil {
            webView = WKWebView(frame: view.bounds, configuration: configuration)
            webView.autoresizingMask = [.width, .height]
            view.addSubview(webView)
        } else {
            // Configure existing WebView
            webView.configuration.preferences.javaScriptEnabled = true
            webView.configuration.preferences.setValue(true, forKey: "allowFileAccessFromFileURLs")
        }

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