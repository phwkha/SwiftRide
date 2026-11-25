using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SwiftRide.Messaging;

public static class KafkaMessaging
{
    private static readonly TimeSpan AdminRequestTimeout = TimeSpan.FromSeconds(15);

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
        const string fileName = "kafka-topics.json";
        await using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, fileName));
        var specifications = await JsonSerializer.DeserializeAsync<TopicSpecification[]>(
            file, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"No topic definitions found in {fileName}.");

        foreach (var topic in specifications)
        {
            if (string.IsNullOrWhiteSpace(topic.Name) || topic.NumPartitions < 1 ||
                topic.ReplicationFactor < 1 || topic.Configs is not { Count: > 0 })
                throw new InvalidOperationException($"Invalid Kafka topic definition for '{topic.Name}' in {fileName}.");
        }

        var topics = specifications.ToDictionary(topic => topic.Name, StringComparer.Ordinal);
        var missing = KafkaTopics.All.Where(name => !topics.ContainsKey(name)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Add topic definitions for {string.Join(", ", missing)} to {fileName}.");

        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = bootstrapServers
        }).Build();

        try
        {
            await admin.CreateTopicsAsync(topics.Values, new CreateTopicsOptions
            {
                RequestTimeout = AdminRequestTimeout
            });
        }
        catch (CreateTopicsException exception) when (exception.Results.All(result =>
            result.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
            var existing = exception.Results
                .Where(result => result.Error.Code == ErrorCode.TopicAlreadyExists)
                .Select(result => new ConfigResource { Type = ResourceType.Topic, Name = result.Topic })
                .ToArray();
            if (existing.Length > 0)
                await ApplyTopicConfigsAsync(admin, topics, existing);
        }
    }

    private static async Task ApplyTopicConfigsAsync(
        IAdminClient admin,
        IReadOnlyDictionary<string, TopicSpecification> topics,
        IReadOnlyCollection<ConfigResource> existing)
    {
        var current = await admin.DescribeConfigsAsync(existing, new DescribeConfigsOptions
        {
            RequestTimeout = AdminRequestTimeout
        });

        var changes = new Dictionary<ConfigResource, List<ConfigEntry>>();
        foreach (var config in current)
        {
            var settings = topics[config.ConfigResource.Name].Configs;
            var updates = settings
                .Where(setting => !config.Entries.TryGetValue(setting.Key, out var effective) ||
                    effective.Source != ConfigSource.DynamicTopicConfig || effective.Value != setting.Value)
                .Select(setting => new ConfigEntry
                {
                    Name = setting.Key,
                    Value = setting.Value,
                    IncrementalOperation = AlterConfigOpType.Set
                })
                .ToList();
            if (updates.Count > 0)
                changes.Add(config.ConfigResource, updates);
        }

        if (changes.Count > 0)
        {
            await admin.IncrementalAlterConfigsAsync(changes, new IncrementalAlterConfigsOptions
            {
                RequestTimeout = AdminRequestTimeout
            });
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
