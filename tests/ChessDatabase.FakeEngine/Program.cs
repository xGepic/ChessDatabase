using System.Text.Json;
using ChessDatabase.Rules;

// A scripted UCI engine for tests: it answers with canned output per position, so analysis
// results are exact and repeatable.
//
// Usage: FakeEngine <script.json> [--log <file>]
//
// Script:
// {
//   "name": "Fake Engine 1.0",
//   "lineDelayMs": 0,                      // pause before each info line
//   "options": ["Threads", "Hash", "MultiPV"],   // the UCI options it announces (this is the default)
//   "quitOnIsReady": false,                // exit instead of answering "isready" (like Lc0 with a bad weights file)
//   "positions": {
//     "<FEN, first four fields>": {
//       "infos": ["depth 1 multipv 1 score cp 20 pv e2e4 e7e5", ...],   // text after "info "
//       "bestmove": "e2e4"
//     }
//   }
// }
//
// A position missing from the script gets "depth 1 multipv 1 score cp 0 pv <first legal move>".
// "go infinite" sends the infos and waits for "stop"; any other "go" answers at once.
// With --log, every command received is appended to the file.

var scriptPath = args.FirstOrDefault(a => !a.StartsWith("--"));
var logIndex = Array.IndexOf(args, "--log");
var logPath = logIndex >= 0 && logIndex + 1 < args.Length ? args[logIndex + 1] : null;
var script = scriptPath is null
    ? new Script()
    : JsonSerializer.Deserialize<Script>(File.ReadAllText(scriptPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

var output = Console.Out;
var outputLock = new object();
void Send(string line)
{
    lock (outputLock)
    {
        output.WriteLine(line);
        output.Flush();
    }
}

var position = Position.Initial;
CancellationTokenSource? search = null;
Task searchTask = Task.CompletedTask;

while (Console.ReadLine() is { } command)
{
    if (logPath is not null) File.AppendAllText(logPath, command + Environment.NewLine);
    var words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (words.Length == 0) continue;

    switch (words[0])
    {
        case "uci":
            Send($"id name {script.Name}");
            Send("id author ChessDatabase tests");
            foreach (var option in script.Options) Send($"option name {option} type string default <empty>");
            Send("uciok");
            break;
        case "isready":
            if (script.QuitOnIsReady) return;
            Send("readyok");
            break;
        case "position":
            position = ReadPosition(words);
            break;
        case "go":
            await StopSearchAsync();
            var infinite = words.Contains("infinite");
            search = new CancellationTokenSource();
            var answer = script.AnswerFor(position);
            var token = search.Token;
            searchTask = Task.Run(() => SearchAsync(answer, infinite, token));
            if (!infinite) await searchTask;
            break;
        case "stop":
            await StopSearchAsync();
            break;
        case "quit":
            await StopSearchAsync();
            return;
    }
}
await StopSearchAsync();

async Task StopSearchAsync()
{
    search?.Cancel();
    await searchTask;
    search = null;
}

async Task SearchAsync(Answer answer, bool infinite, CancellationToken stop)
{
    foreach (var info in answer.Infos)
    {
        if (stop.IsCancellationRequested) break;
        if (script.LineDelayMs > 0)
            await Task.Delay(script.LineDelayMs, stop).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (stop.IsCancellationRequested) break;
        Send("info " + info);
    }
    if (infinite) await Task.Delay(Timeout.Infinite, stop).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    Send("bestmove " + answer.Bestmove);
}

static Position ReadPosition(string[] words)
{
    var movesAt = Array.IndexOf(words, "moves");
    var setup = movesAt < 0 ? words[1..] : words[1..movesAt];
    var position = setup[0] == "fen" ? Position.FromFen(string.Join(' ', setup[1..])) : Position.Initial;
    if (movesAt >= 0)
        foreach (var uci in words[(movesAt + 1)..])
            position = position.Play(Move.TryParseUci(uci, out var move) ? move : throw new InvalidDataException(uci));
    return position;
}

sealed class Script
{
    public string Name { get; init; } = "Fake Engine 1.0";
    public int LineDelayMs { get; init; }
    public List<string> Options { get; init; } = ["Threads", "Hash", "MultiPV"];
    public bool QuitOnIsReady { get; init; }
    public Dictionary<string, Answer> Positions { get; init; } = [];

    public Answer AnswerFor(Position position)
    {
        var key = string.Join(' ', position.ToFen().Split(' ').Take(4));
        if (Positions.TryGetValue(key, out var answer)) return answer;
        var moves = position.LegalMoves();
        if (moves.Count == 0)
            return new Answer { Infos = [position.IsCheck ? "depth 0 score mate 0" : "depth 0 score cp 0"], Bestmove = "(none)" };
        var first = moves[0].ToUci();
        return new Answer { Infos = [$"depth 1 multipv 1 score cp 0 pv {first}"], Bestmove = first };
    }
}

sealed class Answer
{
    public List<string> Infos { get; init; } = [];
    public string Bestmove { get; init; } = "";
}
