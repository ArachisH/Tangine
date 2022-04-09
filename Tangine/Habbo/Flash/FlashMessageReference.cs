using Flazzy.ABC;

namespace Tangine.Habbo.Flash;

public readonly record struct FlashMessageReference(ASMethod Method, ASMethod Callback, int OrderInMethod, int ArgumentsUsed);