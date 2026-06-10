using Azure.Identity;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AK.BuildingBlocks.Messaging;

// Shared MassTransit + Azure Service Bus setup. Every service that needs the event bus calls
// AddAzureServiceBusMassTransit() from its Infrastructure ServiceCollectionExtensions.
//
// MassTransit abstracts the transport: consumers, the saga, and publish/send calls are written
// against MassTransit's API and are UNCHANGED by the change of underlying transport to Azure
// Service Bus — only this bus configuration changes.
public static class MassTransitExtensions
{
    // Registers MassTransit with Azure Service Bus as the transport, authenticated with
    // Microsoft Entra — no SAS key or connection string.
    //
    // The `configure` callback is where each service registers its own consumers / saga:
    //   AddAzureServiceBusMassTransit(config, "order", x => {
    //       x.AddSagaStateMachine<OrderSaga, OrderSagaState>()...;
    //       x.AddConsumer<PaymentSucceededConsumer>();
    //   });
    //
    // ── Authentication (Entra, secret-less) ─────────────────────────────────────────────────
    // The namespace is addressed by its FULLY-QUALIFIED NAMESPACE (e.g.
    // sb-antkart-dev.servicebus.windows.net — a NON-SECRET value, committed in config), and the
    // connection is authorized with DefaultAzureCredential: the developer's Azure CLI sign-in
    // locally, and the resource's managed identity in the cloud. The same code, no secrets — the
    // environment decides the identity. This matches the platform's secret-less posture; there is
    // no connection string anywhere.
    //
    // ── Topology is owned by infrastructure-as-code ─────────────────────────────────────────
    // The Service Bus topology — the order-commands queue, the integration-events topic, and its
    // per-service subscriptions — is provisioned by INFRASTRUCTURE-AS-CODE (the platform's single
    // source of truth), NOT by the application at runtime. The application's managed identity is
    // granted only Azure Service Bus Data Sender / Data Receiver — never Manage — so it cannot
    // create or alter entities. MassTransit is therefore configured to send to and receive from
    // the existing, provisioned entities and must not deploy or manage topology. (Full end-to-end
    // binding over Service Bus is verified during the test-enablement step.)
    public static IServiceCollection AddAzureServiceBusMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        string servicePrefix,
        Action<IBusRegistrationConfigurator> configure)
    {
        var fullyQualifiedNamespace = configuration["ServiceBus:FullyQualifiedNamespace"]
            ?? throw new InvalidOperationException(
                "ServiceBus:FullyQualifiedNamespace is missing from configuration.");

        services.AddMassTransit(x =>
        {
            // Prefix + kebab-case keeps each service's receive endpoints uniquely named and
            // aligned with the provisioned entity-naming convention. Unchanged from before —
            // only the transport changed (e.g. "notification" + PaymentFailedConsumer →
            // "notification-payment-failed").
            x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(servicePrefix, false));

            // Service-specific consumers / sagas registered by the caller.
            configure(x);

            x.UsingAzureServiceBus((context, cfg) =>
            {
                // Entra (token) auth against the namespace — no connection string / SAS key.
                cfg.Host(new Uri($"sb://{fullyQualifiedNamespace}"), host =>
                {
                    host.TokenCredential = new DefaultAzureCredential();
                });

                // Global message retry: on a transient consumer failure, retry 3 times with
                // incremental delays before the message dead-letters. Transport-agnostic; unchanged.
                cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

                // Bind receive endpoints for the registered consumers to the IaC-provisioned
                // entities. The identity is Send/Receive-only, so topology is never created here.
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
