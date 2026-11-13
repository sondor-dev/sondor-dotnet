namespace Sondor.Cqrs;

/// <summary>Adds FluentValidation to the <c>Sondor.Cqrs</c> pipeline.</summary>
public static class FluentValidationBuilderExtensions
{
    /// <summary>
    /// Adds the validation behaviour to the pipeline, after those added before it (the first one added is the outermost). It runs
    /// every <c>IValidator&lt;TRequest&gt;</c> registered in the container; register your validators yourself, for example with
    /// <c>AddValidatorsFromAssemblyContaining</c> from <c>FluentValidation.DependencyInjectionExtensions</c>.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The builder.</returns>
    public static CqrsBuilder UseFluentValidation(this CqrsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddBehavior(typeof(ValidationBehavior<,>));
    }
}
