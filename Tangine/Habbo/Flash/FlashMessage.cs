using Flazzy.ABC;

namespace Tangine.Habbo.Flash;

public readonly record struct FlashMessage(short Id, string Structure, bool IsOutgoing, ASClass MessageClass, ASClass ParserClass, List<FlashMessageReference> References);