# A web app on localhost instead of a desktop window

The Owner uses the app in a normal browser, started like their other applications: `ng serve` in `ui/` for the UI and `dotnet run` for the backend, in two terminals. The Photino desktop shell of ADR 0001 is removed. Everything else from ADR 0001 stands: an ASP.NET Core backend, an Angular UI, Engine output over SignalR, Stockfish as a native process, Windows only.

A browser tab is not a private window: any website open in the same browser can send requests to `localhost:5080`. With no logins, the backend therefore answers only to the host names `localhost` and `127.0.0.1` (against DNS rebinding) and refuses requests that change data when their `Origin` is another site (against cross-site form posts). Port 5080 must not be exposed to the network.

## Considered Options

- **Keep the desktop window**: one click to start, but unlike the Owner's other applications, and the browser's developer tools and extensions are more awkward to reach.
- **Backend serves the built UI on one port**: one process instead of two, but the Owner prefers `ng serve` with live reload. The backend still serves a built UI when there is one, which the smoke tests use.
