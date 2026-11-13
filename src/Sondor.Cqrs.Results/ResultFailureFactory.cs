using Sondor.Results;

namespace Sondor.Cqrs;

/// <summary>Builds a failed <c>Result</c> or <c>Result&lt;T&gt;</c> for a behaviour that refuses a request.</summary>
internal sealed class ResultFailureFactory<TResponse> : IFailureFactory<TResponse>
    where TResponse : IResultState
{
    public TResponse Create(RequestFailure failure)
    {
        var create = Builder.Create
            ?? throw new InvalidOperationException(
                $"{typeof(TResponse).FullName} is a result type this package cannot build. Register an IFailureFactory<{typeof(TResponse).Name}> of your own.");

        var type = failure.Kind == FailureKind.Validation ? ErrorType.Validation : ErrorType.Failure;

        return create(new Error(failure.Code, failure.Description, type));
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
