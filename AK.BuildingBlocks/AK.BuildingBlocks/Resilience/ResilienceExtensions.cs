using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace AK.BuildingBlocks.Resilience;

public static class ResilienceExtensions
{
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

    public static IServiceCollection AddNpgsqlResilience(this IServiceCollection services)
    {
        services.AddResiliencePipeline("npgsql", pipeline =>
        {
            pipeline.AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Constant
            });

            pipeline.AddTimeout(TimeSpan.FromSeconds(30));
        });

        return services;
    }
}
