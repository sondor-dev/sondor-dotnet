namespace Sondor.Cqrs;

/// <summary>
/// A request and the type of response it produces. Implement <see cref="ICommand{TResponse}"/> or
/// <see cref="IQuery{TResponse}"/> to say what the request is for; implement this interface directly for anything else.
/// </summary>
/// <typeparam name="TResponse">The response the request's handler returns.</typeparam>
#pragma warning disable CA1040 // Avoid empty interfaces: the interface exists to carry TResponse, so the compiler can infer it in Send.
#pragma warning disable S2326 // TResponse is used by the dispatcher through the interface's type argument.
public interface IRequest<out TResponse>;
#pragma warning restore S2326
#pragma warning restore CA1040

/// <summary>A request with nothing to return. Its handler returns <see cref="Unit"/>.</summary>
public interface IRequest : IRequest<Unit>;

/// <summary>A request that changes something. The distinction is a label: tracing, metrics and logging report it.</summary>
/// <typeparam name="TResponse">The response the request's handler returns.</typeparam>
public interface ICommand<out TResponse> : IRequest<TResponse>;

/// <summary>A command with nothing to return.</summary>
public interface ICommand : ICommand<Unit>;

/// <summary>A request that only reads. The distinction is a label: tracing, metrics and logging report it.</summary>
/// <typeparam name="TResponse">The response the request's handler returns.</typeparam>
public interface IQuery<out TResponse> : IRequest<TResponse>;
