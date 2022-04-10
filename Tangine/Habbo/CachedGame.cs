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
            ThrowHelper.ThrowFileNotFoundException("Failed to locate the specified json file.", cachedGameJsonPath);
        }

        var cachedGameNode = JsonNode.Parse(File.ReadAllBytes(cachedGameJsonPath));
        if (cachedGameNode == null)
        {
            ThrowHelper.ThrowArgumentException($"Failed to parse the json file located at: {cachedGameJsonPath}", nameof(cachedGameJsonPath));
        }

        Path = GetNonNullableValue<string>(cachedGameNode, "path");
        IsUnity = GetNonNullableValue<bool>(cachedGameNode, "isUnity");
        IsAir = GetNonNullableValue<bool>(cachedGameNode, "isAir");
        Revision = GetNonNullableValue<string>(cachedGameNode, "revision");

        IsPostShuffle = GetNonNullableValue<bool>(cachedGameNode, "isPostShuffle");
        HasPingInstructions = GetNonNullableValue<bool>(cachedGameNode, "hasPingInstructions");

        var outgoingNode = GetNonNullableValue<JsonArray>(cachedGameNode, "outgoing");
        var incomingNode = GetNonNullableValue<JsonArray>(cachedGameNode, "incoming");
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
                Name = GetNonNullableValue<string>(identifiers[i], "name"),
                Id = GetNonNullableValue<short>(identifiers[i], "id"),
                Hash = GetNonNullableValue<uint>(identifiers[i], "hash"),
                Structure = GetNonNullableValue<string>(identifiers[i], "structure"),
                IsOutgoing = isOutgoing,
                TypeName = GetNonNullableValue<string>(identifiers[i], "typeName"),
                ParserTypeName = GetNonNullableValue<string>(identifiers[i], "parserTypeName"),
                References = GetNonNullableValue<int>(identifiers[i], "references")
            };
            messagesByName.Add(message.Name, message);
            _messagesByHash.Add(message.Hash, message);
        }
    }

    protected override bool? TryPatch(GamePatches patch) => throw new NotSupportedException();

    public override void GenerateMessageHashes() => ThrowHelper.ThrowNotSupportedException();
    public override bool TryResolveMessage(string name, uint hash, bool isOutgoing, out HMessage message)
    {
        if (!_messagesByHash.TryGetValue(hash, out message))
        {
            message = (isOutgoing ? _outMessagesByName : _inMessagesByName).GetValueOrDefault(name);
        }
        return message != default;
    }

    public override byte[] ToArray() => throw new NotSupportedException();
    public override void Disassemble() => ThrowHelper.ThrowNotSupportedException();
    public override void Assemble(string path) => ThrowHelper.ThrowNotSupportedException();

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

    private static T GetNonNullableValue<T>(JsonNode? parentNode, string propertyName)
    {
        if (parentNode == null)
        {
            ThrowHelper.ThrowNullReferenceException("The parent node can not be null.");
        }

        JsonNode? childNode = parentNode[propertyName];
        if (childNode == null)
        {
            ThrowHelper.ThrowNullReferenceException("The child node was not found in the parent node.");
        }

        return childNode.GetValue<T>();
    }
}