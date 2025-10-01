//
//  AboutView.swift
//  SpotifyWPF
//
//

import SwiftUI

struct AboutView: View {
    @Environment(\.dismiss) var dismiss

    var body: some View {
        VStack(spacing: 0) {
            // Custom title bar
            ZStack {
                Color(NSColor.windowBackgroundColor)
                    .frame(height: 50)
                    .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))

                Text("About Spofify")
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundColor(Color(NSColor.labelColor))

                HStack {
                    Spacer()
                    Button(action: { dismiss() }) {
                        Text("✕")
                            .font(.system(size: 14))
                            .foregroundColor(Color(NSColor.secondaryLabelColor))
                            .frame(width: 30, height: 30)
                    }
                    .buttonStyle(PlainButtonStyle())
                    .background(Color.clear)
                    .clipShape(Circle())
                    .padding(.trailing, 15)
                }
            }
            .frame(height: 50)

            // Main content
            ScrollView {
                VStack(spacing: 20) {
                    // Spotify logo placeholder
                    ZStack {
                        Circle()
                            .fill(Color.green)
                            .frame(width: 80, height: 80)

                        Text("♪")
                            .font(.system(size: 40))
                            .foregroundColor(.white)
                    }
                    .padding(.top, 20)

                    // Title
                    Text("Spofify")
                        .font(.system(size: 28, weight: .bold))
                        .foregroundColor(Color.green)

                    // Subtitle
                    Text("Unofficial power tools for Spotify")
                        .font(.system(size: 16))
                        .foregroundColor(Color(NSColor.secondaryLabelColor))
                        .multilineTextAlignment(.center)

                    // Version info
                    Text("Version: 3.0.6")
                        .font(.system(size: 13))
                        .foregroundColor(Color(NSColor.secondaryLabelColor))

                    // Author
                    Text("Author: t0rzz (maintainer)")
                        .font(.system(size: 13))
                        .foregroundColor(Color(NSColor.secondaryLabelColor))

                    // Original project
                    Text("Original project: MrPnut/SpotifyWPF")
                        .font(.system(size: 13))
                        .foregroundColor(Color(NSColor.secondaryLabelColor))

                    // Repository link
                    Button(action: {
                        if let url = URL(string: "https://github.com/t0rzz/SpotifyWPF") {
                            NSWorkspace.shared.open(url)
                        }
                    }) {
                        Text("GitHub Repository")
                            .font(.system(size: 13))
                            .foregroundColor(Color.green)
                            .underline()
                    }
                    .buttonStyle(PlainButtonStyle())

                    // Disclaimer
                    VStack(spacing: 10) {
                        HStack {
                            Text("⚠️")
                            Text("This is a community project and is not affiliated with Spotify.")
                                .font(.system(size: 12))
                                .foregroundColor(Color(NSColor.secondaryLabelColor))
                                .multilineTextAlignment(.center)
                        }
                    }
                    .padding(15)
                    .background(Color(NSColor.controlBackgroundColor))
                    .cornerRadius(8)

                    // Features section
                    VStack(alignment: .leading, spacing: 10) {
                        Text("✨ Features:")
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundColor(Color(NSColor.labelColor))

                        VStack(alignment: .leading, spacing: 5) {
                            Text("• Web Playback SDK integration")
                                .font(.system(size: 12))
                                .foregroundColor(Color(NSColor.secondaryLabelColor))
                            Text("• Device transfer support")
                                .font(.system(size: 12))
                                .foregroundColor(Color(NSColor.secondaryLabelColor))
                            Text("• Enhanced status bar")
                                .font(.system(size: 12))
                                .foregroundColor(Color(NSColor.secondaryLabelColor))
                            Text("• Real-time playback control")
                                .font(.system(size: 12))
                                .foregroundColor(Color(NSColor.secondaryLabelColor))
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(15)
                    .background(Color(NSColor.controlBackgroundColor))
                    .cornerRadius(8)
                }
                .padding(.horizontal, 30)
                .padding(.vertical, 20)
            }

            // Bottom button area
            ZStack {
                Color(NSColor.windowBackgroundColor)
                    .frame(height: 80)

                Button("OK") {
                    dismiss()
                }
                .frame(width: 120, height: 36)
                .background(Color.green)
                .foregroundColor(.white)
                .cornerRadius(6)
                .font(.system(size: 14, weight: .semibold))
            }
            .frame(height: 80)
        }
        .frame(width: 450, height: 500)
        .background(Color(NSColor.windowBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
        .shadow(radius: 10)
    }
}

#Preview {
    AboutView()
}
