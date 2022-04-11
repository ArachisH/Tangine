using System.Net;
using System.Text.Json;

using Sulakore.Habbo;

namespace Tangine.Habbo;

public abstract class HGame : IGame, IDisposable
{
    private readonly GamePatches _defaultPatches;

    public virtual string? Path { get; init; }
    public virtual GameKind Kind { get; protected set; }

    public virtual string? Revision { get; protected set; }
    public virtual bool IsPostShuffle { get; protected set; }
    public virtual bool HasPingInstructions { get; protected set; }

    public int KeyShouterId { get; init; }
    public IPEndPoint? InjectableEndPoint { get; init; }
    public int EndPointShouterId => IsPostShuffle ? 4000 : 206;

    protected bool IsDisposed { get; private set; }

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
                ThrowHelper.ThrowNotSupportedException($"Patch not supported: {patch}");
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
        cachedGameJson.WriteString("kind", Kind.ToString());

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
        if (!IsDisposed)
        {
            Dispose(true);
            IsDisposed = true;

            GC.Collect();
            GC.SuppressFinalize(this);
        }
    }
    protected abstract void Dispose(bool disposing);
}