using System.Buffers.Binary;

namespace Quarp.CartKit;

/// <summary>
/// Walks a CIL method body looking for the opcodes that can only exist because floating
/// point exists — <c>ldc.r4/r8</c>, the <c>conv.r*</c> family, the <c>r4/r8</c> variants
/// of indirect and array access, and <c>ckfinite</c>. Signatures alone are not enough:
/// <c>(int)(x / 3)</c> written over a <c>System.Double</c> temporary can be optimized
/// down to values that never touch a named field or local, but the arithmetic itself is
/// still float opcodes in the body.
///
/// Instruction lengths come from ECMA-335 Partition VI: a walk that guesses lengths
/// desynchronizes and starts reading operand bytes as opcodes, which is how naive
/// scanners produce both false positives and blind spots. Hence the full operand-size
/// table for the single-byte and 0xFE-prefixed opcode maps, plus the one variable-length
/// instruction (<c>switch</c>).
/// </summary>
internal static class IlFloatScan
{
    private const byte Invalid = 255;
    private const int TwoBytePrefix = 0xFE;
    private const int SwitchOpcode = 0x45;

    /// <summary>
    /// Operand size in bytes for every single-byte opcode; <see cref="Invalid"/> marks the
    /// gaps in the ECMA-335 map (and 0xFE, the two-byte prefix, handled separately).
    /// <c>switch</c> (0x45) is listed as 0 and its variable length is computed by the walk.
    /// </summary>
    private static readonly byte[] OneByteOperandSize =
    {
        // 0x00 nop..ldloca.s
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1,
        // 0x10 starg.s..ldc.i4.s
        1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1,
        // 0x20 ldc.i4, ldc.i8, ldc.r4, ldc.r8, -, dup..bge.s
        4, 8, 4, 8, Invalid, 0, 0, 4, 4, 4, 0, 1, 1, 1, 1, 1,
        // 0x30 bgt.s..blt
        1, 1, 1, 1, 1, 1, 1, 1, 4, 4, 4, 4, 4, 4, 4, 4,
        // 0x40 bne.un..blt.un, switch, ldind.*
        4, 4, 4, 4, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        // 0x50 ldind.ref, stind.*, add..not
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        // 0x60 or..conv.u8, callvirt
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4,
        // 0x70 cpobj..isinst, conv.r.un, -, -, unbox, throw, ldfld..ldsflda
        4, 4, 4, 4, 4, 4, 0, Invalid, Invalid, 4, 0, 4, 4, 4, 4, 4,
        // 0x80 stsfld, stobj, conv.ovf.*.un, box, newarr, ldlen, ldelema
        4, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 4, 0, 4,
        // 0x90 ldelem.*, stelem.i..stelem.i8
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        // 0xA0 stelem.r4, stelem.r8, stelem.ref, ldelem, stelem, unbox.any, -
        0, 0, 0, 4, 4, 4, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid,
        // 0xB0 -, conv.ovf.*, -
        Invalid, Invalid, Invalid, 0, 0, 0, 0, 0, 0, 0, 0, Invalid, Invalid, Invalid, Invalid, Invalid,
        // 0xC0 -, refanyval, ckfinite, -, mkrefany, -
        Invalid, Invalid, 4, 0, Invalid, Invalid, 4, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid,
        // 0xD0 ldtoken, conv.u2..endfinally, leave, leave.s, stind.i
        4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 1, 0,
        // 0xE0 conv.u, -
        0, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid,
        // 0xF0 -, 0xFE prefix, -
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid,
    };

    /// <summary>Operand size for the 0xFE-prefixed opcodes, indexed by the second byte.</summary>
    private static readonly byte[] TwoByteOperandSize =
    {
        // 0xFE00 arglist, ceq, cgt, cgt.un, clt, clt.un, ldftn, ldvirtftn, -
        0, 0, 0, 0, 0, 0, 4, 4, Invalid,
        // 0xFE09 ldarg, ldarga, starg, ldloc, ldloca, stloc, localloc, -
        2, 2, 2, 2, 2, 2, 0, Invalid,
        // 0xFE11 endfilter, unaligned., volatile., tail., initobj, constrained., cpblk, initblk
        0, 1, 0, 0, 4, 4, 0, 0,
        // 0xFE19 no., rethrow, -, sizeof, refanytype, readonly.
        1, 0, Invalid, 4, 0, 0,
    };

    /// <summary>
    /// Finds the first floating-point instruction in <paramref name="il"/>. Returns false
    /// for a body with none — and also for the (Roslyn-impossible) case of an opcode
    /// outside the ECMA-335 map, where the walk stops rather than desynchronize.
    /// </summary>
    public static bool TryFindFloatOpcode(ReadOnlySpan<byte> il, out string opcodeName, out int offset)
    {
        int position = 0;
        while (position < il.Length)
        {
            int instructionStart = position;
            int opcode = il[position++];
            if (opcode == TwoBytePrefix)
            {
                if (position >= il.Length)
                {
                    break;
                }
                int second = il[position++];
                if (second >= TwoByteOperandSize.Length || TwoByteOperandSize[second] == Invalid)
                {
                    break;
                }
                position += TwoByteOperandSize[second];
                continue;   // No 0xFE-prefixed opcode is float-specific.
            }

            string? name = FloatOpcodeName(opcode);
            if (name is not null)
            {
                opcodeName = name;
                offset = instructionStart;
                return true;
            }

            if (opcode == SwitchOpcode)
            {
                if (position + 4 > il.Length)
                {
                    break;
                }
                uint targets = BinaryPrimitives.ReadUInt32LittleEndian(il[position..]);
                position += 4;
                if (targets > (uint)((il.Length - position) / 4))
                {
                    break;
                }
                position += (int)targets * 4;
                continue;
            }

            byte operandSize = OneByteOperandSize[opcode];
            if (operandSize == Invalid)
            {
                break;
            }
            position += operandSize;
        }

        opcodeName = string.Empty;
        offset = 0;
        return false;
    }

    private static string? FloatOpcodeName(int opcode) => opcode switch
    {
        0x22 => "ldc.r4",
        0x23 => "ldc.r8",
        0x4E => "ldind.r4",
        0x4F => "ldind.r8",
        0x56 => "stind.r4",
        0x57 => "stind.r8",
        0x6B => "conv.r4",
        0x6C => "conv.r8",
        0x76 => "conv.r.un",
        0x98 => "ldelem.r4",
        0x99 => "ldelem.r8",
        0xA0 => "stelem.r4",
        0xA1 => "stelem.r8",
        0xC3 => "ckfinite",
        _ => null,
    };
}
