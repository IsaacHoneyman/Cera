using System.Diagnostics.CodeAnalysis;
using Cera.Compiler.Lexer;
using Cera.Compiler.Logging;
using static Cera.Compiler.Backend.CeraValue;

namespace Cera.Compiler.Backend;

public class Chunk(Diagnostics diag)
{
    public List<byte> Code { get; private set; } = [];
    public List<CeraValue> Constants { get; private set; } = [];
    public List<int> Lines { get; private set; } = []; // for error handling

    public void WriteByte(byte b, int line)
    {
        Code.Add(b);
        Lines.Add(line);
    }

    public void WriteByte(OpCode op, int line) { WriteByte((byte)op, line); }

    public int AddConstant(CeraValue value)
    {
        // Search for an existing constant to deduplicate
        for (int i = 0; i < Constants.Count; i++)
        {
            if (Constants[i].Tag == value.Tag)
            {
                if (value.Tag == ValueTag.Int && Constants[i].IntValue == value.IntValue) return i;
                if (value.Tag == ValueTag.Float && Constants[i].FloatValue == value.FloatValue) return i;
                if (value.Tag == ValueTag.String && Constants[i].StringValue!.Equals(value.StringValue)) return i;
            }
        }

        Constants.Add(value);
        return Constants.Count - 1;
    }

    public int EmitJump(OpCode instruction, int line)
    {
        WriteByte(instruction, line);
        WriteByte(0xff, line); // placeholder to be updated later 
        WriteByte(0xff, line);
        return Code.Count - 2;
    }

    public void PatchJump(int offset)
    {
        /// Patches a previously emitted jump with the calculated offset.
        int jump = Code.Count - offset - 2;
        if (jump > ushort.MaxValue)
        {
            FatalError("Too much code to jump over, (Max 65535 bytes)", null);
        }

        Code[offset] = (byte)(jump & 0xff);
        Code[offset + 1] = (byte)((jump >> 8) & 0xff);
    }

    [DoesNotReturn]
    private void FatalError(string message, Token? token)
    {
        EmitterException e = new(message, token ?? Token.None());
        diag.LogError(e.Message);
        throw e;
    }
}