namespace Kendo.UserService.Domain.Events;

public class EventEmbedding
{
    public Guid EventId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public float[] Embedding { get; set; } = [];
    public DateTimeOffset EmbeddedAt { get; set; }

    public Event Event { get; set; } = null!;
}
