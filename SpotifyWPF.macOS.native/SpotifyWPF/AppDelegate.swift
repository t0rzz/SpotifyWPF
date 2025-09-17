//
//  AppDelegate.swift
//  SpotifyWPF
//
//  Created by GitHub Copilot on 2025-09-15.
//  Simplified to just show HTML page in WebView
//

import Cocoa
import WebKit

@main
class AppDelegate: NSObject, NSApplicationDelegate, WKNavigationDelegate, WKUIDelegate {

    var window: NSWindow!
    var webView: WKWebView!

    func applicationDidFinishLaunching(_ aNotification: Notification) {
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

        // Create WebView configuration
        let configuration = WKWebViewConfiguration()
        configuration.preferences.javaScriptEnabled = true
        configuration.preferences.setValue(true, forKey: "allowFileAccessFromFileURLs")

        // Create WebView
        webView = WKWebView(frame: window.contentView!.bounds, configuration: configuration)
        webView.autoresizingMask = [.width, .height]
        webView.navigationDelegate = self
        webView.uiDelegate = self

        // Add WebView to window
        window.contentView?.addSubview(webView)

        // Load the HTML file
        loadWebApp()

        // Show the window
        window.makeKeyAndOrderFront(nil)
    }

    func applicationWillTerminate(_ aNotification: Notification) {
        // Insert code here to tear down your application
    }

    func applicationSupportsSecureRestorableState(_ app: NSApplication) -> Bool {
        return true
    }

    // MARK: - URL Scheme Handling

    func application(_ application: NSApplication, open urls: [URL]) {
        for url in urls {
            handleIncomingURL(url)
        }
    }

    func application(_ app: NSApplication, open url: URL) -> Bool {
        handleIncomingURL(url)
        return true
    }

    private func handleIncomingURL(_ url: URL) {
        print("Received URL: \(url.absoluteString)")

        // Check if this is our OAuth callback
        if url.scheme == "spofifywpf" && url.host == "callback" {
            // Extract query parameters
            let components = URLComponents(url: url, resolvingAgainstBaseURL: false)
            var authCode: String?
            var state: String?
            var error: String?

            if let queryItems = components?.queryItems {
                for item in queryItems {
                    switch item.name {
                    case "code":
                        authCode = item.value
                    case "state":
                        state = item.value
                    case "error":
                        error = item.value
                    default:
                        break
                    }
                }
            }

            // Send the OAuth result to the web app
            sendOAuthResultToWebApp(authCode: authCode, state: state, error: error)
        }
    }

    private func sendOAuthResultToWebApp(authCode: String?, state: String?, error: String?) {
        var script = "window.oauthCallback({"

        if let authCode = authCode {
            script += "code: '\(authCode)'"
        }

        if let state = state {
            if authCode != nil { script += ", " }
            script += "state: '\(state)'"
        }

        if let error = error {
            if authCode != nil || state != nil { script += ", " }
            script += "error: '\(error)'"
        }

        script += "});"

        // Execute JavaScript in the web view
        DispatchQueue.main.async {
            self.webView.evaluateJavaScript(script) { (result, error) in
                if let error = error {
                    print("JavaScript execution error: \(error.localizedDescription)")
                } else {
                    print("OAuth callback sent to web app successfully")
                }
            }
        }
    }

    private func loadWebApp() {
        // Get the path to the web app files
        guard let webAppPath = Bundle.main.path(forResource: "index", ofType: "html", inDirectory: "WebApp") else {
            print("Error: Could not find web app files")
            return
        }

        let webAppURL = URL(fileURLWithPath: webAppPath)
        webView.loadFileURL(webAppURL, allowingReadAccessTo: webAppURL.deletingLastPathComponent())
    }

    // MARK: - WKNavigationDelegate

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        if let url = navigationAction.request.url {
            // Handle OAuth URLs by opening in external browser
            if url.scheme == "https" && (url.host?.contains("accounts.spotify.com") == true || url.host?.contains("spotify.com") == true) {
                NSWorkspace.shared.open(url)
                decisionHandler(.cancel)
                return
            }
        }

        decisionHandler(.allow)
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        print("Web app loaded successfully")
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        print("Web app failed to load: \(error.localizedDescription)")
    }

    // MARK: - WKUIDelegate

    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        // Handle popup windows (like OAuth) by opening in external browser
        if let url = navigationAction.request.url {
            NSWorkspace.shared.open(url)
        }
        return nil
    }
}