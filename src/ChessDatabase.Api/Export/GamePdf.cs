using ChessDatabase.Api.Collection;
using ChessDatabase.Api.Games;
using Side = ChessDatabase.Rules.Color;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ChessDatabase.Api.Export;

/// <summary>
/// An annotated Game as a printable A4 PDF: a header with the Players, Elos ("unrated" when
/// unknown), Event, date and result, then the notation with Variations, comments and symbols,
/// and diagrams from the Owner's side at the moves marked for one. Engine Assessments are never
/// printed (ADR 0003).
/// </summary>
public static class GamePdf
{
    private const float DiagramWidthCm = 7.5f;
    private const float IndentPoints = 18;
    private static readonly string[] Fonts = ["Segoe UI", "Segoe UI Symbol", "Cambria Math"];

    static GamePdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = true; // Windows fonts: Segoe UI, and Cambria Math for ⩲ ⩱
    }

    /// <param name="game">Loaded with Opponent, Event and MoveNodes.</param>
    public static byte[] Write(GameRecord game, string ownerName, bool finalDiagram)
    {
        var (white, black) = PlayerSides.ToWhiteAndBlack(game.OwnerColour, ownerName, game.Opponent.Name);
        var orientation = game.OwnerColour == OwnerColour.White ? Side.White : Side.Black;
        var blocks = NotationLayout.Build(new MoveTree(game.MoveNodes), game.Result.ToPgn(), finalDiagram);

        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(style => style.FontSize(10.5f).FontFamily(Fonts).LineHeight(1.35f));

            page.Header().PaddingBottom(14).Column(header =>
            {
                header.Item().Text(text =>
                {
                    text.Span(white).Bold().FontSize(15);
                    text.Span($" {Elo(game.WhiteElo)}").FontSize(12).FontColor(Colors.Grey.Darken1);
                    text.Span("  –  ").FontSize(15);
                    text.Span(black).Bold().FontSize(15);
                    text.Span($" {Elo(game.BlackElo)}").FontSize(12).FontColor(Colors.Grey.Darken1);
                });
                var details = new[] { game.Event?.Name, game.Site, game.Round is { } round ? $"Round {round}" : null, game.Date }
                    .Where(d => !string.IsNullOrWhiteSpace(d));
                header.Item().Text(string.Join(" · ", details)).FontColor(Colors.Grey.Darken2);
                header.Item().Text(text =>
                {
                    text.Span("Result ").FontColor(Colors.Grey.Darken2);
                    text.Span(game.Result.ToPgn()).Bold();
                    if (game.Eco is { } eco) text.Span($"   {eco} {game.OpeningName}").FontColor(Colors.Grey.Darken2);
                });
                header.Item().PaddingTop(6).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
            });

            page.Content().Column(column =>
            {
                column.Spacing(7);
                foreach (var block in blocks)
                {
                    switch (block)
                    {
                        case NotationParagraph paragraph:
                            column.Item().PaddingLeft(paragraph.Indent * IndentPoints).Text(text => WriteTokens(text, paragraph.Tokens));
                            break;
                        case NotationDiagram diagram:
                            var png = DiagramRenderer.RenderPng(diagram.Position, new DiagramOptions(orientation, Coordinates: true, diagram.LastMove));
                            column.Item().PaddingVertical(4).AlignCenter().Width(DiagramWidthCm, Unit.Centimetre).ShowEntire().Column(d =>
                            {
                                d.Item().Image(png);
                                d.Item().PaddingTop(3).AlignCenter().Text(diagram.Caption).FontSize(9).FontColor(Colors.Grey.Darken2);
                            });
                            break;
                    }
                }
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Darken1));
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        })).GeneratePdf();
    }

    private static string Elo(int? elo) => elo?.ToString() ?? "unrated";

    private static void WriteTokens(TextDescriptor text, IReadOnlyList<NotationToken> tokens)
    {
        string? previous = null;
        foreach (var token in tokens)
        {
            var glue = previous is null || previous == "(" || token.Text == ")" ? "" : " ";
            var span = text.Span(glue + token.Text);
            switch (token.Style)
            {
                case NotationStyle.MainMove: span.Bold(); break;
                case NotationStyle.Comment: span.Italic().FontColor(Colors.Grey.Darken3); break;
            }
            previous = token.Text;
        }
    }
}
