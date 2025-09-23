//
//  SpofifyApp.swift
//  SpotifyWPF
//
//  Created by GitHub Copilot on 2025-09-21.
//

import SwiftUI
import Cocoa

extension Notification.Name {
    static let spotifyCallback = Notification.Name("SpotifyCallback")
}

class AppDelegate: NSObject, NSApplicationDelegate {
    func application(_ application: NSApplication, open urls: [URL]) {
        print("📱 AppDelegate received URLs: \(urls)")
        
        for url in urls {
            if url.scheme == "spofifywpf" {
                print("🔗 Processing Spotify callback URL: \(url)")
                // Post notification that can be observed by SwiftUI
                NotificationCenter.default.post(name: .spotifyCallback, object: nil, userInfo: ["url": url])
            }
        }
    }
}

@main
struct SpofifyApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate
    
    var body: some Scene {
        Window("SpotifyWPF", id: "main") {
            ContentView()
        }
        .windowStyle(.hiddenTitleBar)
        .windowResizability(.contentSize)
    }
}