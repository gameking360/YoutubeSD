package br.furb.youtubesd.download.messaging;

public record ParsedMessage<T>(MessageEnvelope envelope, T payload) {
}
