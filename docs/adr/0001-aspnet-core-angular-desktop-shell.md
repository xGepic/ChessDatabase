# ASP.NET Core backend with an Angular UI in a desktop window

**Superseded in part by [ADR 0004](0004-web-app-on-localhost.md):** the desktop window is gone; the app is used in a browser on localhost. The backend/UI split and SignalR still stand.

The app is a personal, Windows-only desktop tool. It is built as an ASP.NET Core backend that hosts an Angular UI inside a desktop window (Photino.NET or WebView2), and engine output streams to the UI over SignalR. This plays to the Owner's C#/TypeScript/Angular background, runs Stockfish natively at full CPU strength, and gets mature web chess components such as chessground for the board. During development, the same app runs as a localhost web app in a browser.

## Considered Options

- **Blazor Hybrid (.NET MAUI)**: all C#, but chess board components barely exist, so the board would need JavaScript interop anyway.
- **Electron + Node**: no C#, and it gives up the Owner's strongest language.
- **WPF**: native C#, but the board, notation and graphs would all have to be built from scratch.
- **Browser-only with Stockfish WebAssembly**: slower, weak multithreading, and compiling Stockfish in would pull the app under GPL.
