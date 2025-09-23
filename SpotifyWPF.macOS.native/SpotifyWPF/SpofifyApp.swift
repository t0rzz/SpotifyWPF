//
//  SpofifyApp.swift
//  SpotifyWPF
//
//

import SwiftUI
import Cocoa

extension Notification.Name {
    static let spotifyCallback = Notification.Name("SpotifyCallback")
    static let showAbout = Notification.Name("ShowAbout")
}

class AppDelegate: NSObject, NSApplicationDelegate {
    var aboutWindow: NSWindow?

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

    func applicationDidFinishLaunching(_ notification: Notification) {
        setupMenuBar()
    }

    func setupMenuBar() {
        let mainMenu = NSMenu()

        // App menu
        let appMenu = NSMenu()
        let appMenuItem = NSMenuItem()
        appMenuItem.submenu = appMenu

        let aboutMenuItem = NSMenuItem(title: "About Spofify", action: #selector(showAbout(_:)), keyEquivalent: "")
        aboutMenuItem.target = self
        appMenu.addItem(aboutMenuItem)

        appMenu.addItem(NSMenuItem.separator())

        appMenu.addItem(withTitle: "Quit Spofify", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")

        mainMenu.addItem(appMenuItem)

        NSApplication.shared.mainMenu = mainMenu
    }

    @objc func showAbout(_ sender: Any?) {
        if aboutWindow == nil {
            let aboutView = AboutView()
            let hostingController = NSHostingController(rootView: aboutView)

            aboutWindow = NSWindow(contentViewController: hostingController)
            aboutWindow?.styleMask = [.titled, .closable]
            aboutWindow?.title = "About Spofify"
            aboutWindow?.isReleasedWhenClosed = false
            aboutWindow?.center()
        }

        aboutWindow?.makeKeyAndOrderFront(nil)
        NSApplication.shared.activate(ignoringOtherApps: true)
    }
}

@main
struct SpofifyApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate
    
    var body: some Scene {
        Window("Spofify", id: "main") {
            ContentView()
        }
        .windowStyle(.hiddenTitleBar)
        .windowResizability(.contentSize)
    }
}