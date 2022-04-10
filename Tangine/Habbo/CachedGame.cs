using System.Text.Json.Nodes;

using Sulakore.Habbo;

namespace Tangine.Habbo;

public sealed class CachedGame : HGame
{
    private readonly Dictionary<uint, HMessage> _messagesByHash;
    private readonly Dictionary<string, HMessage> _inMessagesByName;
    private readonly Dictionary<string, HMessage> _outMessagesByName;

    public CachedGame(string cachedGameJsonPath)
        : base(GamePatches.None)
    {
        if (!File.Exists(cachedGameJsonPath))
        {
            throw new FileNotFoundException(null, cachedGameJsonPath);
        }

        var cachedGameNode = JsonNode.Parse(File.ReadAllBytes(cachedGameJsonPath));
        Path = (string)cachedGameNode["path"];
        IsUnity = (bool)cachedGameNode["isUnity"];
        IsAir = (bool)cachedGameNode["isAir"];

        Revision = (string)cachedGameNode["revision"];
        IsPostShuffle = (bool)cachedGameNode["isPostShuffle"];
        HasPingInstructions = (bool)cachedGameNode["hasPingInstructions"];

        JsonArray outgoingNode = cachedGameNode["outgoing"].AsArray();
        JsonArray incomingNode = cachedGameNode["incoming"].AsArray();
        _inMessagesByName = new Dictionary<string, HMessage>(incomingNode.Count);
        _outMessagesByName = new Dictionary<string, HMessage>(outgoingNode.Count);
        _messagesByHash = new Dictionary<uint, HMessage>(outgoingNode.Count + incomingNode.Count);

        CacheMessages(outgoingNode, true);
        CacheMessages(incomingNode, false);
    }

    private void CacheMessages(JsonArray identifiers, bool isOutgoing)
    {
        Dictionary<string, HMessage> messagesByName = isOutgoing ? _outMessagesByName : _inMessagesByName;
        for (int i = 0; i < identifiers.Count; i++)
        {
            var message = new HMessage
            {
                Name = (string)identifiers[i]["name"],
                Id = (short)identifiers[i]["id"],
                Hash = (uint)identifiers[i]["hash"],
                Structure = (string)identifiers[i]["structure"],
                IsOutgoing = isOutgoing,
                TypeName = (string)identifiers[i]["typeName"],
                ParserTypeName = (string)identifiers[i]["parserTypeName"],
                References = (int)identifiers[i]["references"]
            };
            messagesByName.Add(message.Name, message);
            _messagesByHash.Add(message.Hash, message);
        }
    }

    protected override bool? TryPatch(GamePatches patch) => throw new NotSupportedException();

    public override void GenerateMessageHashes() => throw new NotSupportedException();
    public override bool TryResolveMessage(string name, uint hash, bool isOutgoing, out HMessage message)
    {
        if (!_messagesByHash.TryGetValue(hash, out message))
        {
            message = (isOutgoing ? _outMessagesByName : _inMessagesByName).GetValueOrDefault(name);
        }
        return message != default;
    }

    public override byte[] ToArray() => throw new NotSupportedException();
    public override void Disassemble() => throw new NotSupportedException();
    public override void Assemble(string path) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _messagesByHash.Clear();
            _messagesByHash.EnsureCapacity(0);

            _inMessagesByName.Clear();
            _inMessagesByName.EnsureCapacity(0);

            _outMessagesByName.Clear();
            _outMessagesByName.EnsureCapacity(0);
        }
    }
}