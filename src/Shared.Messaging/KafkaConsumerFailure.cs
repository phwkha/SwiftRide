namespace SwiftRide.Messaging;

public sealed record KafkaConsumerFailure(
    string Topic,
    string ConsumerGroup,
    string MessageType,
    byte[] Body,
    string ExceptionType,
    string Error,
    DateTime ExceptionTimestamp);
