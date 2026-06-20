using Kendo.UserService.Models;

namespace Kendo.UserService.Domain.Users;

public class UserEmbedding
{
    public Guid UserId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public float[] Embedding { get; set; } = [];
    public DateTimeOffset EmbeddedAt { get; set; }

    public User User { get; set; } = null!;
}
