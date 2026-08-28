using System.Text.Json.Nodes;

namespace LX.Core.Persistence;

public interface ISaveMigration
{
    int FromVersion { get; }

    int ToVersion { get; }

    JsonNode Migrate(JsonNode payload);
}
