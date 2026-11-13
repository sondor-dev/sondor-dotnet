using System.Diagnostics;
using FluentValidation;
using FluentValidation.Results;

namespace Sondor.Cqrs;

/// <summary>
/// Runs every FluentValidation validator of the request before its handler. A valid request goes on; an invalid one never
/// reaches the handler. It is refused through an <see cref="IFailureFactory{TResponse}"/> when the response type has one (a
/// failed result, for example), and by throwing a <see cref="ValidationException"/> when it has not.
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators,
    IEnumerable<IFailureFactory<TResponse>> failureFactories) :
    IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Handle(TRequest request, RequestContinuation<TResponse> continuation, CancellationToken cancellationToken)
    {
        // A request nobody validates costs nothing: the container hands the validators over as an array.
        var all = validators as IValidator<TRequest>[] ?? [.. validators];

        return all.Length == 0
            ? continuation()
            : ValidateThenContinue(all, request, continuation, cancellationToken);
    }

    private async ValueTask<TResponse> ValidateThenContinue(IValidator<TRequest>[] all,
        TRequest request,
        RequestContinuation<TResponse> continuation,
        CancellationToken cancellationToken)
    {
        List<ValidationFailure>? failures = null;

        foreach (var validator in all)
        {
            var result = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);

            if (!result.IsValid)
            {
                (failures ??= []).AddRange(result.Errors);
            }
        }

        if (failures is null)
        {
            return await continuation().ConfigureAwait(false);
        }

        Record(failures);

        var factory = failureFactories.FirstOrDefault();

        if (factory is null)
        {
            throw new ValidationException(failures);
        }

        // The validators' own messages, once each. The rejected values are never added here.
        var description = string.Join(' ', failures.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal));

        return factory.Create(new RequestFailure(CqrsValidation.ErrorCode, description, FailureKind.Validation));
    }

    // On the current span (the request's, when tracing is on): which properties were refused, never their values.
    private static void Record(List<ValidationFailure> failures)
    {
        if (Activity.Current is not { IsAllDataRequested: true } activity)
        {
            return;
        }

        activity.SetTag("cqrs.validation.error_count", failures.Count);
        activity.SetTag("cqrs.validation.properties", string.Join(',', failures.Select(failure => failure.PropertyName).Distinct(StringComparer.Ordinal)));
    }
}
