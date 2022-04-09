using System.Net;
using System.Text.Json;

using Sulakore.Habbo;

namespace Tangine.Habbo;

public abstract class HGame : IGame, IDisposable
{
    private bool _isDisposed;
    private readonly GamePatches _defaultPatches;

    #region Reserved Names
    private static readonly string[] _reservedNames = new[]
    {
            "break", "case", "catch", "class", "continue",
            "default", "do", "dynamic", "each", "else",
            "extends", "false", "final", "finally", "for",
            "function", "get", "if", "implements", "import",
            "in", "include", "native", "null", "override",
            "package", "return", "set", "static", "super",
            "switch", "throw", "true", "try", "use",
            "var", "while", "with"
        };
    #endregion

    public virtual string Path { get; init; }
    public virtual bool IsUnity { get; init; }
    public virtual bool IsAir { get; protected set; }

    public virtual string Revision { get; protected set; }
    public virtual bool IsPostShuffle { get; protected set; }
    public virtual bool HasPingInstructions { get; protected set; }

    public int KeyShouterId { get; init; }
    public IPEndPoint InjectableEndPoint { get; init; }
    public int EndPointShouterId => IsPostShuffle ? 4000 : 206;

    public HGame(GamePatches defaultPatches) => _defaultPatches = defaultPatches;

    public GamePatches Patch(GamePatches patches)
    {
        GamePatches failedPatches = GamePatches.None;
        foreach (GamePatches patch in Enum.GetValues(typeof(GamePatches)))
        {
            if ((patches & patch) != patch || patch == GamePatches.None) continue;

            bool? result = TryPatch(patch);
            if (result == null)
            {
                throw new NotSupportedException("Patching method not yet supported: " + patch);
            }

            // It's a nullable, so that's why I'm not using the '!' operator...
            if (result == false)
            {
                failedPatches |= patch; // Did this patch fail?; If so, add the flag to 'failedPatches'
            }
        }
        return failedPatches;
    }
    public GamePatches Patch() => Patch(_defaultPatches);
    protected abstract bool? TryPatch(GamePatches patch);

    public abstract void GenerateMessageHashes();
    public abstract bool TryResolveMessage(string name, uint hash, bool isOutgoing, out HMessage message);

    public void SaveAs(string cachedGameJsonPath, Outgoing outgoing, Incoming incoming)
    {
        using FileStream messagesJsonStream = File.Open(cachedGameJsonPath, FileMode.Create);
        using var cachedGameJson = new Utf8JsonWriter(messagesJsonStream, new JsonWriterOptions { Indented = true });

        cachedGameJson.WriteStartObject();
        cachedGameJson.WriteString("path", Path);
        cachedGameJson.WriteBoolean("isUnity", IsUnity);
        cachedGameJson.WriteBoolean("isAir", IsAir);

        cachedGameJson.WriteString("revision", Revision);
        cachedGameJson.WriteBoolean("isPostShuffle", IsPostShuffle);
        cachedGameJson.WriteBoolean("hasPingInstructions", HasPingInstructions);

        SaveAs("outgoing", cachedGameJson, outgoing);
        SaveAs("incoming", cachedGameJson, incoming);

        cachedGameJson.WriteEndObject();
        cachedGameJson.Flush();
    }

    public abstract byte[] ToArray();
    public abstract void Disassemble();
    public abstract void Assemble(string path);

    protected static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.StartsWith("_-")) return false;
        return !_reservedNames.Contains(value.ToLower());
    }
    protected static void SaveAs(string propertyName, Utf8JsonWriter cachedGameJson, Identifiers identifiers)
    {
        cachedGameJson.WriteStartArray(propertyName);
        foreach (HMessage message in identifiers)
        {
            cachedGameJson.WriteStartObject();
            cachedGameJson.WriteString("name", message.Name);
            cachedGameJson.WriteNumber("id", message.Id);
            cachedGameJson.WriteNumber("hash", message.Hash);
            cachedGameJson.WriteString("structure", message.Structure);
            cachedGameJson.WriteNumber("references", message.References);
            cachedGameJson.WriteString("typeName", message.TypeName);
            cachedGameJson.WriteString("parserName", message.ParserTypeName);
            cachedGameJson.WriteEndObject();
        }
        cachedGameJson.WriteEndArray();
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Dispose(true);
            _isDisposed = true;

            GC.Collect();
            GC.SuppressFinalize(this);
        }
    }
    protected abstract void Dispose(bool disposing);
}