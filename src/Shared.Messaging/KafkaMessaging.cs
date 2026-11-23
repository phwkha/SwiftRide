using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SwiftRide.Messaging;

public static class KafkaMessaging
{
    public static IServiceCollection AddSwiftRideKafka(
        this IServiceCollection services,
        Action<IRiderRegistrationConfigurator> configure)
    {
        return services.AddMassTransit(bus =>
        {
            bus.UsingInMemory();
            bus.AddRider(rider =>
            {
                rider.AddKafkaProducer<KafkaConsumerFailure>(KafkaTopics.ConsumerFailures);
                configure(rider);
            });
        });
    }

    public static string GetBootstrapServers(this IConfiguration configuration)
    {
        var bootstrapServers = configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
            throw new InvalidOperationException("Missing configuration: Kafka:BootstrapServers");

        return bootstrapServers;
    }

    public static async Task EnsureTopicsAsync(string bootstrapServers)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = bootstrapServers
        }).Build();

        var topics = KafkaTopics.All.Select(name => new TopicSpecification
        {
            Name = name,
            NumPartitions = 3,
            ReplicationFactor = 1
        });

        try
        {
            await admin.CreateTopicsAsync(topics, new CreateTopicsOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(15)
            });
        }
        catch (CreateTopicsException exception) when (exception.Results.All(result =>
            result.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
            // Another service may provision the same topics during startup.
        }
    }

    public static void AddKafkaProducer<TEvent>(this IRiderRegistrationConfigurator rider, string topic)
        where TEvent : class
    {
        rider.AddProducer<string, TEvent>(topic, new ProducerConfig
        {
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000
        });
    }

    public static void AddKafkaConsumerEndpoint<TEvent, TConsumer>(
        this IKafkaFactoryConfigurator kafka,
        IRiderRegistrationContext context,
        string topic,
        string group,
        Action<IRetryConfigurator>? retry = null)
        where TEvent : class
        where TConsumer : class, IConsumer<TEvent>
    {
        kafka.TopicEndpoint<string, TEvent>(topic, group, endpoint =>
        {
            endpoint.AutoOffsetReset = AutoOffsetReset.Earliest;
            endpoint.CheckpointInterval = TimeSpan.FromSeconds(5);
            if (retry is not null)
                endpoint.UseMessageRetry(retry);
            endpoint.ConfigureConsumer<TConsumer>(context);
            endpoint.ConfigureError(pipe => pipe.UseExecuteAsync(async failure =>
            {
                using var scope = context.CreateScope();
                var producer = scope.ServiceProvider.GetRequiredService<ITopicProducer<string, KafkaConsumerFailure>>();
                await producer.Produce(group, new KafkaConsumerFailure(
                    topic,
                    group,
                    typeof(TEvent).Name,
                    failure.Body.GetBytes(),
                    failure.Exception.GetType().Name,
                    failure.Exception.Message,
                    failure.ExceptionTimestamp), failure.CancellationToken);
            }));
        });
    }

    private static Task ProduceOutboxEvent<TEvent>(
        this IServiceProvider services,
        TEvent message,
        Guid tripId,
        Guid messageId,
        CancellationToken cancellationToken)
        where TEvent : class
    {
        var producer = services.GetRequiredService<ITopicProducer<string, TEvent>>();
        return producer.Produce(tripId.ToString("D"), message,
            Pipe.Execute<KafkaSendContext<string, TEvent>>(context => context.MessageId = messageId),
            cancellationToken);
    }

    public static async Task<TEvent> PublishOutboxEvent<TEvent>(
        this IServiceProvider services,
        string payload,
        JsonSerializerOptions? jsonOptions,
        Func<TEvent, Guid> getTripId,
        Guid messageId,
        CancellationToken cancellationToken)
        where TEvent : class
    {
        var message = JsonSerializer.Deserialize<TEvent>(payload, jsonOptions)
            ?? throw new JsonException($"Outbox payload for '{typeof(TEvent).Name}' was null.");
        await services.ProduceOutboxEvent(message, getTripId(message), messageId, cancellationToken);
        return message;
    }
}
