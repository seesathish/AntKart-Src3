using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace AK.BuildingBlocks.Resilience;

// Polly v8 resilience pipelines shared across all services.
// These wrap outbound calls (HTTP, Redis, PostgreSQL) with retry + circuit breaker logic
// so a temporary infrastructure hiccup doesn't immediately fail a user request.
public static class ResilienceExtensions
{
    // Attaches a Retry → Circuit Breaker → Timeout pipeline to an HttpClient.
    // Used for: Keycloak HTTP calls, Razorpay REST calls, Discount gRPC client.
    //
    // How the pipeline works (layers execute in the order listed):
    //   1. Retry:          on transient failure, wait 300ms × 2^n + jitter and try again (up to 3 times)
    //   2. Circuit Breaker: if >50% of the last 5+ requests fail within 60s, stop trying for 30s
    //                       (half-open after break: lets one test request through to check recovery)
    //   3. Timeout:        if a single attempt takes >15s, cancel it (counted as a failure for CB)
    //
    // UseJitter adds a random offset to retry delays so many instances don't all retry simultaneously
    // (thundering herd prevention).
    public static IHttpClientBuilder AddHttpResilienceWithCircuitBreaker(
        this IHttpClientBuilder builder,
        int maxRetryAttempts = 3,
        double failureRatio = 0.5,
        int minimumThroughput = 5,
        int breakDurationSeconds = 30)
    {
        builder.AddResilienceHandler("circuit-breaker", pipeline =>
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(300),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            });

            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = failureRatio,
                MinimumThroughput = minimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(60),
                BreakDuration = TimeSpan.FromSeconds(breakDurationSeconds)
            });

            pipeline.AddTimeout(TimeSpan.FromSeconds(15));
        });
        return builder;
    }

    // Retry pipeline for Redis operations (cart reads/writes).
    // Short timeout (5s) because a slow Redis response usually means connection issues.
    public static IServiceCollection AddRedisResilience(this IServiceCollection services)
    {
        services.AddResiliencePipeline("redis", pipeline =>
        {
            pipeline.AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            });

            pipeline.AddTimeout(TimeSpan.FromSeconds(5));
        });

        return services;
    }

    // Retry pipeline for PostgreSQL (Npgsql) operations (orders, payments, notifications).
    // Longer timeout (30s) because DB queries can legitimately take a few seconds under load.
    // Exponential backoff + jitter prevents all services from hammering the DB at once
    // after it recovers from a brief outage.
    public static IServiceCollection AddNpgsqlResilience(this IServiceCollection services)
    {
        services.AddResiliencePipeline("npgsql", pipeline =>
        {
            pipeline.AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            });

            pipeline.AddTimeout(TimeSpan.FromSeconds(30));
        });

        return services;
    }
}
