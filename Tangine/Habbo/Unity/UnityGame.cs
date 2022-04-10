using Sulakore.Habbo;

using Wazzy;
using Wazzy.Types;
using Wazzy.Bytecode;
using Wazzy.Sections.Subsections;
using Wazzy.Bytecode.Instructions.Control;
using Wazzy.Bytecode.Instructions.Numeric;
using Wazzy.Bytecode.Instructions.Variable;
using Wazzy.Bytecode.Instructions.Parametric;

namespace Tangine.Habbo.Unity;

public class UnityGame : HGame
{
    private WASMModule _wasm;

    public override bool IsUnity => true;
    public override bool IsPostShuffle => true;
    public override bool HasPingInstructions => false;

    public UnityGame(string path, string revision, ReadOnlyMemory<byte> region = default)
        : base(GamePatches.InjectKeyShouter)
    {
        if (!region.IsEmpty)
        {
            _wasm = new WASMModule(region);
        }

        Path = path;
        Revision = revision;
    }

    #region Patching Methods
    protected override bool? TryPatch(GamePatches patch) => patch switch
    {
        GamePatches.InjectKeyShouter => InjectKeyShouter(),
        _ => null
    };

    private bool InjectKeyShouter()
    {
        for (int i = 0; i < _wasm.CodeSec.Count; i++)
        {
            // Begin searching for the ChaChaEngine.SetKey method.
            var funcTypeIndex = (int)_wasm.FunctionSec[i];
            FuncType functionType = _wasm.TypeSec[funcTypeIndex];
            CodeSubsection codeSubSec = _wasm.CodeSec[i];

            if (codeSubSec.Locals.Count != 1) continue;
            if (functionType.ParameterTypes.Count != 4) continue;

            bool hasValidParamTypes = true;
            for (int j = 0; j < functionType.ParameterTypes.Count; j++)
            {
                if (functionType.ParameterTypes[j] == typeof(int)) continue;
                hasValidParamTypes = false;
                break;
            }
            if (!hasValidParamTypes) continue; // If all of the parameters are not of type int.

            List<WASMInstruction> expression = codeSubSec.Parse();
            if (expression[0].OP != OPCode.ConstantI32) continue;
            if (expression[1].OP != OPCode.LoadI32_8S) continue;
            if (expression[2].OP != OPCode.EqualZeroI32) continue;
            if (expression[3].OP != OPCode.If) continue;

            // Dig through the block/branching expressions
            var expandedInstructions = WASMInstruction.ConcatNestedExpressions(expression).ToArray();
            for (int j = 0, k = expandedInstructions.Length - 2; j < expandedInstructions.Length; j++)
            {
                WASMInstruction instruction = expandedInstructions[j];
                if (instruction.OP != OPCode.ConstantI32) continue;

                var constanti32Ins = (ConstantI32Ins)instruction;
                if (constanti32Ins.Constant != 12) continue;

                if (expandedInstructions[++j].OP != OPCode.AddI32) continue;
                if (expandedInstructions[++j].OP != OPCode.TeeLocal) continue;
                if (expandedInstructions[++j].OP != OPCode.LoadI32) continue;
                if (expandedInstructions[++j].OP != OPCode.ConstantI32) continue;
                if (expandedInstructions[++j].OP != OPCode.SubtractI32) continue;

                if (expandedInstructions[k--].OP != OPCode.Call) continue;
                if (expandedInstructions[k--].OP != OPCode.ConstantI32) continue;
                if (expandedInstructions[k--].OP != OPCode.ConstantI32) continue;
                if (expandedInstructions[k--].OP != OPCode.ConstantI32) continue;

                expression.InsertRange(0, new WASMInstruction[]
                {
                        new ConstantI32Ins(0),  // WebSocket Instance Id
                        new GetLocalIns(1),     // Key Pointer
                        new ConstantI32Ins(48), // Key Length
                        new CallIns(126),       // _WebSocketSend
                        new DropIns(),
                });
                codeSubSec.Bytecode = WASMInstruction.ToArray(expression);
                return true;
            }
        }
        return false;
    }
    #endregion

    public override void GenerateMessageHashes() => throw new NotSupportedException();
    public override bool TryResolveMessage(string name, uint hash, bool isOutgoing, out HMessage message) => throw new NotSupportedException();

    public override byte[] ToArray() => _wasm.ToArray();
    public override void Disassemble() => _wasm.Disassemble();
    public override void Assemble(string path) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _wasm = null;
    }
}