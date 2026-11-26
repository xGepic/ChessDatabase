using ChessDatabase.Api.Assessment;
using ChessDatabase.Api.Engines;

namespace ChessDatabase.Api.Tests;

/// <summary>The Assessment module on fixed evaluations: no Engine, no I/O.</summary>
public class AssessmentTests
{
    [Theory]
    [InlineData(0, 50.0)]
    [InlineData(100, 59.1)]
    [InlineData(-100, 40.9)]
    [InlineData(300, 75.1)]
    [InlineData(1000, 97.5)]
    [InlineData(5000, 97.5)] // centipawns beyond 1000 count as 1000
    public void Centipawns_become_winning_chance_with_the_Lichess_formula(int cp, double expected)
    {
        Assert.Equal(expected, WinningChances.WhitePercent(new EngineScore(cp, null)), 1);
    }

    [Fact]
    public void A_mate_score_counts_as_the_ceiling_for_the_mating_side()
    {
        Assert.Equal(97.5, WinningChances.WhitePercent(new EngineScore(null, 3)), 1);
        Assert.Equal(2.5, WinningChances.WhitePercent(new EngineScore(null, -12)), 1);
    }

    [Theory]
    [InlineData(0, -60, MoveClassification.Inaccuracy)]
    [InlineData(0, -120, MoveClassification.Mistake)]
    [InlineData(0, -190, MoveClassification.Blunder)]
    [InlineData(0, -50, null)]
    public void White_moves_are_classified_by_the_winning_chance_they_lose(int before, int after, MoveClassification? expected)
    {
        var assessment = Assessor.Assess([Eval(before, "e2e4"), Eval(after, "e7e5", blackToMove: true)], ["d2d4"]);

        Assert.Equal(expected, assessment.Moves[0].Classification);
    }

    [Theory]
    [InlineData(9.9, null)]
    [InlineData(10, MoveClassification.Inaccuracy)]
    [InlineData(19.9, MoveClassification.Inaccuracy)]
    [InlineData(20, MoveClassification.Mistake)]
    [InlineData(29.9, MoveClassification.Mistake)]
    [InlineData(30, MoveClassification.Blunder)]
    public void Each_threshold_starts_its_class(double lossInPoints, MoveClassification? expected)
    {
        Assert.Equal(expected, Assessor.Classify(lossInPoints));
    }

    [Fact]
    public void A_large_swing_inside_an_already_won_position_is_not_a_blunder()
    {
        // +15 pawns to +7 pawns: still completely winning.
        var won = Assessor.Assess([Eval(1500, "d1h5"), Eval(700, "a7a6", blackToMove: true)], ["a2a3"]);
        // Level to three pawns down: the game is thrown away.
        var thrown = Assessor.Assess([Eval(0, "d1h5"), Eval(-300, "a7a6", blackToMove: true)], ["a2a3"]);

        Assert.Null(won.Moves[0].Classification);
        Assert.Equal(MoveClassification.Blunder, thrown.Moves[0].Classification);
    }

    [Fact]
    public void Black_moves_lose_winning_chance_from_Blacks_side()
    {
        // 1.e4 keeps White at +0.30; Black's 1...f6 gives White +1.90.
        var assessment = Assessor.Assess(
            [Eval(30, "e2e4"), Eval(30, "e7e5", blackToMove: true), Eval(190, "d2d4")],
            ["e2e4", "f7f6"]);

        Assert.Null(assessment.Moves[0].Classification);
        Assert.Equal(MoveClassification.Mistake, assessment.Moves[1].Classification);
    }

    [Fact]
    public void Playing_the_Engine_move_is_never_classified()
    {
        var assessment = Assessor.Assess([Eval(0, "e2e4"), Eval(200, "e7e5", blackToMove: true)], ["e2e4"]);

        Assert.Null(assessment.Moves[0].Classification);
    }

    [Fact]
    public void Accuracy_follows_the_Lichess_method()
    {
        Assert.Equal(64.58, Accuracy.OfMove(60, 50), 2);
        Assert.Equal(100, Accuracy.OfMove(50, 55));

        var level = Assessor.Assess(
            [.. Enumerable.Range(0, 9).Select(i => Eval(30, "a2a3", blackToMove: i % 2 == 1))],
            ["e2e4", "e7e5", "g1f3", "b8c6", "f1b5", "a7a6", "b5a4", "g8f6"]);
        Assert.Equal((100.0, 100.0), (level.WhiteAccuracy, level.BlackAccuracy));
    }

    [Fact]
    public void A_checkmate_on_the_board_is_a_win_for_the_side_that_gave_it()
    {
        var mated = new PositionEvaluation(new EngineScore(null, 0), []); // Black to move and mated

        Assert.Equal(97.5, WinningChances.WhitePercent(mated.Score, Rules.Color.Black), 1);
        Assert.Equal(2.5, WinningChances.WhitePercent(mated.Score, Rules.Color.White), 1);
    }

    /// <summary>An evaluation as the Engine reports it: from the side to move.</summary>
    private static PositionEvaluation Eval(int whiteCp, string best, bool blackToMove = false) =>
        new(new EngineScore(blackToMove ? -whiteCp : whiteCp, null), [best]);
}
