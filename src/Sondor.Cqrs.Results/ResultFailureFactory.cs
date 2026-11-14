using Sondor.Results;

namespace Sondor.Cqrs;

/// <summary>Builds a failed <c>Result</c> or <c>Result&lt;T&gt;</c> for a behaviour that refuses a request.</summary>
internal sealed class ResultFailureFactory<TResponse> : IFailureFactory<TResponse>
    where TResponse : IResultState
{
    private const string DescriptionArgument = "Description";

    private static readonly ErrorTemplate Invalid = new("Request.Invalid", ErrorType.Validation, "{" + DescriptionArgument + "}");

    private static readonly ErrorTemplate Refused = new("Request.Refused", ErrorType.Failure, "{" + DescriptionArgument + "}");

    public TResponse Create(RequestFailure failure)
    {
        var create = Builder.Create
            ?? throw new InvalidOperationException(
                $"{typeof(TResponse).FullName} is a result type this package cannot build. Register an IFailureFactory<{typeof(TResponse).Name}> of your own.");

        var type = failure.Kind == FailureKind.Validation ? ErrorType.Validation : ErrorType.Failure;

        return create(ErrorOf(failure, type));
    }

    // A refusal without a breakdown is a plain error, as it always was. One with a breakdown keeps it as a Data argument, which a
    // layer that reports the error can read; the description is still the joined text, which the template shows as it is.
    private static Error ErrorOf(RequestFailure failure, ErrorType type)
    {
        if (failure.Errors is not { Count: > 0 } errors || string.IsNullOrWhiteSpace(failure.Description))
        {
            return new Error(failure.Code, failure.Description, type);
        }

        var template = type == ErrorType.Validation ? Invalid : Refused;

        return template.WithCode(failure.Code).Create(
            ErrorArgument.Term(DescriptionArgument, failure.Description),
            ErrorArgument.Data(CqrsResultArguments.Errors, errors));
    }

    // Worked out once for each response type. Refusing a request is the rare path, so reflection here costs nothing that matters.
    private static class Builder
    {
        public static readonly Func<Error, TResponse>? Create = Build();

        private static Func<Error, TResponse>? Build()
        {
            var type = typeof(TResponse);

            if (type == typeof(Result))
            {
                return static error => (TResponse)(object)Result.Failure(error);
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>))
            {
                var method = typeof(Result).GetMethod(nameof(Result.Failure), 1, [typeof(Error)])!
                    .MakeGenericMethod(type.GetGenericArguments());

                return method.CreateDelegate<Func<Error, TResponse>>();
            }

            return null;
        }
    }
}
