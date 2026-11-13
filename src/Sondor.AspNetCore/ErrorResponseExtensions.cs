using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sondor.AspNetCore.Internal;

namespace Sondor.AspNetCore;

/// <summary>Registers and uses the error handling of this package.</summary>
public static class ErrorResponseExtensions
{
    /// <summary>
    /// Registers one consistent problem response for the application: the framework's <c>AddProblemDetails()</c>, an
    /// <see cref="IExceptionHandler"/> that reports unhandled exceptions, and the factory that turns an <see cref="Error"/> into a
    /// problem. Nothing is added to the pipeline until <see cref="UseSondorProblemDetails"/> (or the framework's own
    /// <c>UseExceptionHandler()</c> and <c>UseStatusCodePages()</c>) is called.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configure">Changes the <see cref="ErrorResponseOptions"/>.</param>
    /// <returns>The services.</returns>
    /// <remarks>
    /// The options are validated when the host starts. Calling this twice keeps one of everything and both configurations. Register
    /// an <see cref="IProblemTextProvider"/> or any number of <see cref="IProblemContributor"/> yourself, or through an adapter package.
    /// </remarks>
    public static IServiceCollection AddSondorProblemDetails(
        this IServiceCollection services,
        Action<ErrorResponseOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = services.AddOptions<ErrorResponseOptions>();

        if (configure is not null)
        {
            builder.Configure(configure);
        }

        builder.ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ErrorResponseOptions>, OptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<ProblemDetailsOptions>, ProblemDetailsOptionsSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionHandler, ErrorExceptionHandler>());
        services.TryAddScoped<IErrorProblemFactory, ErrorProblemFactory>();

        return services.AddProblemDetails();
    }

    /// <summary>
    /// Adds the framework's exception handler (when <see cref="ErrorResponseOptions.HandleExceptions"/> is on) and status code
    /// pages (when <see cref="ErrorResponseOptions.WriteStatusCodeBodies"/> is on) to the pipeline, so an exception, a bare 401 or a
    /// bare 404 gets the same problem response as a failed <c>Result</c>. Call it first, before routing and authentication.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The application.</returns>
    /// <remarks>
    /// An application that also renders Razor Pages or views keeps its own error pages: requests
    /// <see cref="ErrorResponseOptions.AppliesTo"/> declines are left alone, so register those pages yourself
    /// (<c>UseExceptionHandler("/Error")</c> works next to this package's handler).
    /// </remarks>
    public static IApplicationBuilder UseSondorProblemDetails(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var settings = app.ApplicationServices.GetRequiredService<IOptions<ErrorResponseOptions>>().Value;

        if (settings.HandleExceptions)
        {
            app.UseExceptionHandler();
        }

        if (settings.WriteStatusCodeBodies)
        {
            app.UseStatusCodePages(async context =>
            {
                var http = context.HttpContext;

                if (settings.AppliesTo(http))
                {
                    await http.RequestServices.GetRequiredService<IProblemDetailsService>()
                        .TryWriteAsync(new ProblemDetailsContext { HttpContext = http });
                }
            });
        }

        return app;
    }
}
