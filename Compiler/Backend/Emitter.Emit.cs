using Cera.Compiler.Parser;
using Cera.Compiler.Lexer;

namespace Cera.Compiler.Backend;

public partial class Emitter
{
    private void EmitExpression(IExprAST expr)
    {
        switch (expr)
        {
            case ExprBlock block: EmitExpressionBlock(block); break;
            case IdentifierExpr id: EmitIdentifier(id); break;
            case LiteralExpr lit: EmitLiteral(lit); break;
            default: throw new NotImplementedException($"Emittion not implemented for '{expr.GetType().Name}'");

        }
    }

    private void EmitExpressionBlock(ExprBlock block)
    {
        int scopeDepth = locals.Count;

        foreach (var stmt in block.Statements)
        {
            if (stmt is VarDeclStmt varDecl)
            {
                EmitExpression(varDecl.Initializer);
                locals.Add(varDecl.Identifier.Lexeme);
            }
            else if (stmt is ExprStmt exprStmt)
            {
                EmitExpression(exprStmt.Expression);
                currentChunk.WriteByte(OpCode.POP, 0); // pop result
            }
        }

        EmitExpression(block.ReturnExpression);
        locals.RemoveRange(scopeDepth, locals.Count - scopeDepth);
    }

    private void EmitIdentifier(IdentifierExpr id)
    {
        string name = id.Identifier.Lexeme;
        int index = locals.LastIndexOf(name);

        if (index != -1) // local var
        {
            currentChunk.WriteByte(OpCode.LOAD_LOCAL, id.Identifier.Line);
            if (index > byte.MaxValue)
                FatalError($"Too many local variables in function, Cannot exceed 255, Variable '{name}' exceeds limit");
            currentChunk.WriteByte((byte)index, id.Identifier.Line);
        }
        else FatalError($"Cannot resolve variable '{name}' during emission");
    }

    private void EmitLiteral(LiteralExpr lit)
    {
        int line = lit.Value.Line;

        switch (lit.Value.Tag)
        {
            case TokenType.True: currentChunk.WriteByte((byte)OpCode.PUSH_TRUE, line); break;
            case TokenType.False: currentChunk.WriteByte((byte)OpCode.PUSH_FALSE, line); break;
            case TokenType.Unit: currentChunk.WriteByte((byte)OpCode.PUSH_UNIT, line); break;
            case TokenType.IntLiteral:
                long intVal = long.Parse(lit.Value.Lexeme);
                if (intVal == 0) currentChunk.WriteByte((byte)OpCode.PUSH_0, line);
                else if (intVal == 1) currentChunk.WriteByte((byte)OpCode.PUSH_1, line);
                else if (intVal >= sbyte.MinValue && intVal <= sbyte.MaxValue)
                {
                    currentChunk.WriteByte((byte)OpCode.PUSH_BYTE, line);
                    currentChunk.WriteByte((byte)(sbyte)intVal, line);
                }
                else
                {
                    int idx = currentChunk.AddConstant(CeraValue.Int(intVal));
                    EmitLoadConst(idx, line);
                }
                break;
            case TokenType.FloatLiteral:
                double floatVal = double.Parse(lit.Value.Lexeme);
                int fIdx = currentChunk.AddConstant(CeraValue.Float(floatVal));
                EmitLoadConst(fIdx, line);
                break;

            case TokenType.CharLiteral:
                string rawChar = lit.Value.Lexeme.Trim('\'');
                int codePoint = char.ConvertToUtf32(rawChar, 0);

                currentChunk.WriteByte((byte)OpCode.PUSH_CHAR, line);
                for (int i = 0; i < 4; i++)
                {
                    currentChunk.WriteByte((byte)((codePoint >> (i * 8)) & 0xFF), line);
                }
                break;

            case TokenType.StringLiteral:
                int idx_ = currentChunk.AddConstant(CeraValue.Object(lit.Value.Lexeme.Trim('"')));
                currentChunk.WriteByte((byte)OpCode.LOAD_CONST, line);
                currentChunk.WriteByte((byte)idx_, line);
                break;

            default:
                FatalError($"Unknown literal type for emission '{lit.Value.Tag}'");
                break;
        }
    }

    private void EmitLoadConst(int idx, int line)
    {
        throw new NotImplementedException();
    }
}