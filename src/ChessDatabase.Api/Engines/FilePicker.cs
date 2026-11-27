using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ChessDatabase.Api.Engines;

/// <param name="Title">The dialog's title, e.g. "Choose the Stockfish program".</param>
/// <param name="Filter">Windows dialog filter, e.g. "Programs (*.exe)|*.exe".</param>
/// <param name="InitialFolder">Where the dialog opens; null lets Windows choose.</param>
public sealed record FilePickRequest(string Title, string Filter, string? InitialFolder);

/// <summary>Lets the Owner choose a file on this PC.</summary>
public interface IFilePicker
{
    /// <returns>The chosen file's full path, or null when the Owner cancelled.</returns>
    Task<string?> PickFileAsync(FilePickRequest request, CancellationToken cancellationToken);
}

/// <summary>Lets one file dialog open at a time, so a double click doesn't stack two of them.</summary>
public sealed class FileDialogGate
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
}

/// <summary>
/// The Windows "Open" dialog. A browser can't tell the app where a file is on disk, but the backend
/// runs on the Owner's own PC (ADR 0004), so it shows the dialog itself: through Windows PowerShell's
/// Windows Forms, on its own STA thread, kept in front of the browser. The values go in through
/// environment variables, so nothing is ever quoted into the script.
/// </summary>
public sealed class WindowsFilePicker : IFilePicker
{
    private const string Script = """
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        Add-Type -AssemblyName System.Windows.Forms
        $owner = New-Object System.Windows.Forms.Form -Property @{
            TopMost = $true; ShowInTaskbar = $false; FormBorderStyle = 'None'; Opacity = 0; Width = 0; Height = 0; StartPosition = 'CenterScreen' }
        $owner.Show()
        $owner.Activate()
        $dialog = New-Object System.Windows.Forms.OpenFileDialog
        $dialog.Title = $env:CHESSDB_DIALOG_TITLE
        $dialog.Filter = $env:CHESSDB_DIALOG_FILTER
        $dialog.CheckFileExists = $true
        if ($env:CHESSDB_DIALOG_FOLDER -and (Test-Path -LiteralPath $env:CHESSDB_DIALOG_FOLDER)) {
            $dialog.InitialDirectory = $env:CHESSDB_DIALOG_FOLDER }
        $result = $dialog.ShowDialog($owner)
        $owner.Close()
        if ($result -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($dialog.FileName) }
        """;

    public async Task<string?> PickFileAsync(FilePickRequest request, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in (string[])["-NoProfile", "-NonInteractive", "-STA", "-ExecutionPolicy", "Bypass",
                     "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Script))])
            start.ArgumentList.Add(argument);
        start.Environment["CHESSDB_DIALOG_TITLE"] = request.Title;
        start.Environment["CHESSDB_DIALOG_FILTER"] = request.Filter;
        start.Environment["CHESSDB_DIALOG_FOLDER"] = request.InitialFolder ?? "";

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("The file dialog could not be opened.");
        }
        catch (Win32Exception e)
        {
            throw new InvalidOperationException("The file dialog could not be opened: Windows PowerShell is missing.", e);
        }
        using (process)
        {
            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var path = output.Trim();
                return path.Length == 0 ? null : path;
            }
            catch (OperationCanceledException)
            {
                // The page went away: close the dialog with it.
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw;
            }
        }
    }
}
