using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.Cqrs.Internal;

/// <summary>Finds the handlers in assemblies and registers them, and checks that every request has exactly one.</summary>
internal static class HandlerScanner
{
    public static void Register(IServiceCollection services,
        IEnumerable<Assembly> assemblies,
        ServiceLifetime lifetime,
        bool validateHandlers) =>
        Register(services,
            assemblies.Distinct()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsClass && !type.IsAbstract && !type.IsGenericTypeDefinition),
            lifetime,
            validateHandlers);

    /// <summary>Registers the handlers among these types, which are classes that can be built (no abstract or open generic ones).</summary>
    public static void Register(IServiceCollection services,
        IEnumerable<Type> types,
        ServiceLifetime lifetime,
        bool validateHandlers)
    {
        var requestHandlers = new Dictionary<Type, List<Type>>();
        var notificationHandlers = new List<(Type Contract, Type Implementation)>();
        var requests = new List<(Type Request, Type Contract)>();

        foreach (var type in types)
        {
            foreach (var contract in type.GetInterfaces().Where(contract => contract.IsGenericType))
            {
                var definition = contract.GetGenericTypeDefinition();

                if (definition == typeof(IRequestHandler<,>))
                {
                    if (!requestHandlers.TryGetValue(contract, out var implementations))
                    {
                        requestHandlers[contract] = implementations = [];
                    }

                    implementations.Add(type);
                }
                else if (definition == typeof(INotificationHandler<>))
                {
                    notificationHandlers.Add((contract, type));
                }
                else if (definition == typeof(IRequest<>))
                {
                    requests.Add((type, contract));
                }
            }
        }

        var duplicates = requestHandlers.Where(entry => entry.Value.Count > 1).ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                "A request has more than one handler, and a request has exactly one: " +
                string.Join("; ", duplicates.Select(entry =>
                    $"{Describe(entry.Key)} is handled by {string.Join(" and ", entry.Value.Select(handler => handler.FullName))}")) + ".");
        }

        if (validateHandlers)
        {
            var missing = requests
                .Where(entry => !requestHandlers.ContainsKey(HandlerOf(entry.Request, entry.Contract))
                    && !services.Any(descriptor => descriptor.ServiceType == HandlerOf(entry.Request, entry.Contract)))
                .Select(entry => entry.Request.FullName)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"These requests have no handler: {string.Join(", ", missing)}. Implement IRequestHandler for each, or set " +
                    $"{nameof(CqrsBuilder.ValidateHandlers)} to false if a handler is registered elsewhere.");
            }
        }

        foreach (var (contract, handlers) in requestHandlers)
        {
            services.Add(new ServiceDescriptor(contract, handlers[0], lifetime));
        }

        foreach (var (contract, implementation) in notificationHandlers)
        {
            services.Add(new ServiceDescriptor(contract, implementation, lifetime));
        }
    }

    private static Type HandlerOf(Type request, Type requestContract) =>
        typeof(IRequestHandler<,>).MakeGenericType(request, requestContract.GetGenericArguments()[0]);

    private static string Describe(Type handlerContract) =>
        handlerContract.GetGenericArguments()[0].FullName ?? handlerContract.Name;
}
