using System.Collections.Concurrent;

namespace WebVibeTest.Infrastructure.Games;

public sealed record GameChatMessage(string SenderUserId, string SenderName, string Message, DateTime SentAtUtc);

public sealed class InMemoryGameChat
{
    private const int MaximumMessagesPerGame = 100;
    private readonly ConcurrentDictionary<Guid, Queue<GameChatMessage>> messages = new();

    public IReadOnlyList<GameChatMessage> Get(Guid gameId)
    {
        var queue = messages.GetOrAdd(gameId, _ => new Queue<GameChatMessage>());
        lock (queue) return queue.ToList();
    }

    public GameChatMessage Add(Guid gameId, string userId, string senderName, string message)
    {
        var entry = new GameChatMessage(userId, senderName, message, DateTime.UtcNow);
        var queue = messages.GetOrAdd(gameId, _ => new Queue<GameChatMessage>());
        lock (queue)
        {
            queue.Enqueue(entry);
            while (queue.Count > MaximumMessagesPerGame) queue.Dequeue();
        }
        return entry;
    }
}
