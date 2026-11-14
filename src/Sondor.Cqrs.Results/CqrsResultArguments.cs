namespace Sondor.Cqrs;

/// <summary>The names of the arguments that a failed result built from a refused request carries on its error.</summary>
public static class CqrsResultArguments
{
    /// <summary>
    /// The name of the <see cref="Sondor.Results.ErrorArgumentKind.Data"/> argument that holds the per-property messages of the
    /// refusal (<see cref="RequestFailure.Errors"/>): an <c>IReadOnlyDictionary&lt;string, string[]&gt;</c> of property name to
    /// messages. A layer that reports the error reads it with <c>error.Arguments.TryGetValue(CqrsResultArguments.Errors, ...)</c>.
    /// It is data for that layer, never part of the description.
    /// </summary>
    public const string Errors = "Errors";
}
