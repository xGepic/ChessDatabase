using ChessDatabase.Api;

// The backend on http://localhost:5080 (launchSettings.json); `ng serve` in ui/ serves the UI and proxies /api and /hubs here.
var app = ChessDatabaseApp.Build(ChessDatabaseApp.CreateBuilder(args));
app.Run();
