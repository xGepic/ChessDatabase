using ChessDatabase.Api;

// Browser mode: run the backend as a localhost web app (development, or `ng serve` against it).
var app = ChessDatabaseApp.Build(ChessDatabaseApp.CreateBuilder(args));
app.Run();
