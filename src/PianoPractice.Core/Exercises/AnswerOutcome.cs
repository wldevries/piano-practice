namespace PianoPractice.Core.Exercises;

public enum CardState
{
    /// <summary>Waiting for the first answer.</summary>
    Waiting,
    /// <summary>Answered correctly (first try, or after a retry).</summary>
    Correct,
    /// <summary>First answer was wrong; waiting for the right answer before moving on.</summary>
    Wrong,
}

/// <summary>
/// Result of an answer. Exercises score only the first answer to a question; after a wrong
/// answer the player has to give the right one before moving on (unscored), which reinforces it.
/// </summary>
public enum AnswerOutcome { Ignored, Correct, Wrong, RetryCorrect, RetryWrong }
