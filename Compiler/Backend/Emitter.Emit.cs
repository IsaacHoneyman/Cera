using Cera.Compiler.Parser;
using Cera.Compiler.Lexer;
using Cera.Compiler.Analyzer;
using static Cera.Compiler.Logging.Diagnostics;

namespace Cera.Compiler.Backend;

public partial class Emitter
{
    private void EmitExpression(IExprAST expr, bool isTail = false)
    {
        switch (expr)
        {
            case ExprBlock block: EmitExpressionBlock(block, isTail); break;
            case TernaryExpr tern: EmitTernaryExpression(tern, isTail); break;
            case SwitchExpr sw: EmitSwitchExpression(sw, isTail); break;
            case CallExpr call: EmitCall(call, isTail); break;
            case IfExpr ifExpr: EmitIfExpr(ifExpr, isTail); break;

            case IdentifierExpr id: EmitIdentifier(id); break;
            case LiteralExpr lit: EmitLiteral(lit); break;
            case UnaryExpr unary: EmitUnaryExpression(unary); break;
            case BinaryExpr bin: EmitBinaryExpression(bin); break;
            case LambdaExpr lambda: EmitLambda(lambda); break;
            case TupleLitExpr tuple: EmitTupleLiteral(tuple); break;
            case ArrLitExpr arr: EmitArrayLiteral(arr); break;
            case ListLitExpr list: EmitListLiteral(list); break;
            case ConExpr con: EmitConstructor(con); break;
            default: FatalError($"Emittion not implemented for '{expr.GetType().Name}'", GetLeadToken(expr)); break;

        }
    }

    private void EmitFailureCleanup(int baseScopeDepth, List<int> failureJumps, int line)
    {
        int varsToPop = Locals.Count - baseScopeDepth;
        for (int i = 0; i < varsToPop; i++)
        {
            CurrentChunk.WriteByte(OpCode.POP, line);
        }

        failureJumps.Add(CurrentChunk.EmitJump(OpCode.JUMP, line));
    }

    private void CompilePatternCase(IPatternAST pattern, string targetVar, List<int> failureJumps, int baseScopeDepth, int line)
    {
        switch (pattern)
        {
            case LiteralPattern lit:
                if (lit.Value.Tag == TokenType.WildCard) break;

                int localIdx = Locals.LastIndexOf(targetVar);
                if (localIdx != -1 && localIdx <= byte.MaxValue)
                {
                    int constIdx = -1;
                    if (lit.Value.Tag == TokenType.IntLiteral) constIdx = CurrentChunk.AddConstant(CeraValue.Int(long.Parse(lit.Value.Lexeme)));
                    else if (lit.Value.Tag == TokenType.FloatLiteral) constIdx = CurrentChunk.AddConstant(CeraValue.Float(double.Parse(lit.Value.Lexeme)));
                    else if (lit.Value.Tag == TokenType.StringLiteral) constIdx = CurrentChunk.AddConstant(CeraValue.String(lit.Value.Lexeme.Trim('"')));
                    else if (lit.Value.Tag == TokenType.CharLiteral) constIdx = CurrentChunk.AddConstant(CeraValue.Char(char.ConvertToUtf32(lit.Value.Lexeme.Trim('\''), 0)));
                    else if (lit.Value.Tag == TokenType.True) constIdx = CurrentChunk.AddConstant(CeraValue.Bool(true));
                    else if (lit.Value.Tag == TokenType.False) constIdx = CurrentChunk.AddConstant(CeraValue.Bool(false));
                    else if (lit.Value.Tag == TokenType.Unit) constIdx = CurrentChunk.AddConstant(CeraValue.Unit());

                    if (constIdx != -1 && constIdx <= byte.MaxValue)
                    {
                        CurrentChunk.WriteByte(OpCode.JUMP_IF_LOCAL_NOT_EQ_CONST, line);
                        CurrentChunk.WriteByte((byte)localIdx, line);
                        CurrentChunk.WriteByte((byte)constIdx, line);

                        int jumpIfNotMatchCo = CurrentChunk.Code.Count;
                        CurrentChunk.WriteByte(0xff, line);
                        CurrentChunk.WriteByte(0xff, line);

                        int jumpIfMatchCo = CurrentChunk.EmitJump(OpCode.JUMP, line);
                        CurrentChunk.PatchJump(jumpIfNotMatchCo);
                        EmitFailureCleanup(baseScopeDepth, failureJumps, line);
                        CurrentChunk.PatchJump(jumpIfMatchCo);
                        break;
                    }
                }

                EmitLoadLocal(targetVar, line);
                EmitLiteral(new LiteralExpr(lit.Value));
                CurrentChunk.WriteByte(OpCode.EQ, line);

                int jumpIfTrue = CurrentChunk.EmitJump(OpCode.JUMP_IF_TRUE, line);
                EmitFailureCleanup(baseScopeDepth, failureJumps, line);
                CurrentChunk.PatchJump(jumpIfTrue);
                break;

            case IdPattern id:
                EmitLoadLocal(targetVar, line);
                Locals.Add(id.Identifier.Lexeme);
                break;

            case ConPattern con:
                EmitLoadLocal(targetVar, line);

                CurrentChunk.WriteByte(OpCode.JUMP_IF_NOT_TAG, line);
                CurrentChunk.WriteByte(GetConstructorTagIndex(con.ConstructorName), line);
                int jumpIfNotMatch = CurrentChunk.Code.Count;
                CurrentChunk.WriteByte(0xff, line);
                CurrentChunk.WriteByte(0xff, line);
                int jumpIfMatch = CurrentChunk.EmitJump(OpCode.JUMP, line);
                CurrentChunk.PatchJump(jumpIfNotMatch);
                EmitFailureCleanup(baseScopeDepth, failureJumps, line);
                CurrentChunk.PatchJump(jumpIfMatch);

                if (con.PayloadPatterns.Count > 0)
                {
                    EmitLoadLocal(targetVar, line);
                    CurrentChunk.WriteByte(OpCode.UNPACK_CON, line);

                    if (con.PayloadPatterns.Count > 1)
                    {
                        CurrentChunk.WriteByte(OpCode.UNPACK_TUPLE, line);
                    }

                    List<string> payloadVars = [];
                    foreach (var p in con.PayloadPatterns)
                    {
                        string pVar = p is IdPattern id ? id.Identifier.Lexeme : $"<payload_{Guid.NewGuid().ToString()[..8]}>";
                        Locals.Add(pVar);
                        payloadVars.Add(pVar);
                    }

                    for (int i = 0; i < con.PayloadPatterns.Count; i++)
                    {
                        if (con.PayloadPatterns[i] is not IdPattern)
                            CompilePatternCase(con.PayloadPatterns[i], payloadVars[i], failureJumps, baseScopeDepth, line);
                    }
                }
                break;

            case TuplePattern tuple:
                EmitLoadLocal(targetVar, line);
                CurrentChunk.WriteByte(OpCode.UNPACK_TUPLE, line);
                if (tuple.Patterns.Count > byte.MaxValue) FatalError("Tuple exceeds 255 fields.", GetLeadToken(tuple));

                List<string> tupleVars = [];
                foreach (var p in tuple.Patterns)
                {
                    string pVar = p is IdPattern id ? id.Identifier.Lexeme : $"<tuple_{Guid.NewGuid().ToString()[..8]}>";
                    Locals.Add(pVar);
                    tupleVars.Add(pVar);
                }

                for (int i = 0; i < tuple.Patterns.Count; i++)
                {
                    if (tuple.Patterns[i] is not IdPattern)
                        CompilePatternCase(tuple.Patterns[i], tupleVars[i], failureJumps, baseScopeDepth, line);
                }
                break;

            case ConsPattern cons:
                EmitLoadLocal(targetVar, line);

                CurrentChunk.WriteByte(OpCode.JUMP_IF_NOT_TAG, line);
                CurrentChunk.WriteByte(constructorTags["Cons"], line);
                int jumpIfNotMatchC = CurrentChunk.Code.Count;
                CurrentChunk.WriteByte(0xff, line);
                CurrentChunk.WriteByte(0xff, line);

                int jumpIfMatchC = CurrentChunk.EmitJump(OpCode.JUMP, line);

                CurrentChunk.PatchJump(jumpIfNotMatchC);
                EmitFailureCleanup(baseScopeDepth, failureJumps, line);

                CurrentChunk.PatchJump(jumpIfMatchC);

                EmitLoadLocal(targetVar, line);
                CurrentChunk.WriteByte(OpCode.UNPACK_LIST, line);

                string headVar = cons.Head is IdPattern hid ? hid.Identifier.Lexeme : $"<head_{Guid.NewGuid().ToString()[..8]}>";
                string tailVar = cons.Tail is IdPattern tid ? tid.Identifier.Lexeme : $"<tail_{Guid.NewGuid().ToString()[..8]}>";
                Locals.Add(headVar);
                Locals.Add(tailVar);

                if (cons.Head is not IdPattern) CompilePatternCase(cons.Head, headVar, failureJumps, baseScopeDepth, line);
                if (cons.Tail is not IdPattern) CompilePatternCase(cons.Tail, tailVar, failureJumps, baseScopeDepth, line);
                break;

            case ListPattern list:
                {
                    if (list.Patterns.Count == 0)
                    {
                        EmitLoadLocal(targetVar, line);
                        CurrentChunk.WriteByte(OpCode.JUMP_IF_NOT_LIST_EMPTY, line);

                        int jumpIfNotMatchL = CurrentChunk.Code.Count;
                        CurrentChunk.WriteByte(0xff, line);
                        CurrentChunk.WriteByte(0xff, line);

                        int jumpIfMatchL = CurrentChunk.EmitJump(OpCode.JUMP, line);

                        CurrentChunk.PatchJump(jumpIfNotMatchL);
                        EmitFailureCleanup(baseScopeDepth, failureJumps, line);

                        CurrentChunk.PatchJump(jumpIfMatchL);
                    }
                    else
                    {
                        string currentListVar = targetVar;
                        for (int i = 0; i < list.Patterns.Count; i++)
                        {
                            // ... (Cons pattern unpacking remains unchanged) ...
                            EmitLoadLocal(currentListVar, line);

                            CurrentChunk.WriteByte(OpCode.JUMP_IF_NOT_TAG, line);
                            CurrentChunk.WriteByte(constructorTags["Cons"], line);
                            int jumpIfNotMatchL = CurrentChunk.Code.Count;
                            CurrentChunk.WriteByte(0xff, line);
                            CurrentChunk.WriteByte(0xff, line);

                            int jumpIfMatchL = CurrentChunk.EmitJump(OpCode.JUMP, line);

                            CurrentChunk.PatchJump(jumpIfNotMatchL);
                            EmitFailureCleanup(baseScopeDepth, failureJumps, line);

                            CurrentChunk.PatchJump(jumpIfMatchL);

                            EmitLoadLocal(currentListVar, line);
                            CurrentChunk.WriteByte(OpCode.UNPACK_LIST, line);

                            string hVar = list.Patterns[i] is IdPattern id ? id.Identifier.Lexeme : $"<list_el_{Guid.NewGuid().ToString()[..8]}>";
                            string tVar = $"<list_tail_{Guid.NewGuid().ToString()[..8]}>";
                            Locals.Add(hVar);
                            Locals.Add(tVar);

                            if (list.Patterns[i] is not IdPattern)
                                CompilePatternCase(list.Patterns[i], hVar, failureJumps, baseScopeDepth, line);

                            currentListVar = tVar;
                        }

                        // Tail validation updated to the fused instruction
                        EmitLoadLocal(currentListVar, line);
                        CurrentChunk.WriteByte(OpCode.JUMP_IF_NOT_LIST_EMPTY, line);

                        int finalJumpIfNotMatch = CurrentChunk.Code.Count;
                        CurrentChunk.WriteByte(0xff, line);
                        CurrentChunk.WriteByte(0xff, line);

                        int finalJumpIfMatch = CurrentChunk.EmitJump(OpCode.JUMP, line);

                        CurrentChunk.PatchJump(finalJumpIfNotMatch);
                        EmitFailureCleanup(baseScopeDepth, failureJumps, line);

                        CurrentChunk.PatchJump(finalJumpIfMatch);
                    }
                    break;
                }

            case ArrPattern arr:
                EmitLoadLocal(targetVar, line);
                int length = arr.Patterns.Count;

                if (length > ushort.MaxValue)
                    FatalError($"Array pattern exceeds maximum size of {ushort.MaxValue} elements.", GetLeadToken(arr));

                CurrentChunk.WriteByte(OpCode.JUMP_IF_NOT_ARRAY_LENGTH, line);

                // Write the 2-byte expected length payload (Little-Endian)
                CurrentChunk.WriteByte((byte)(length & 0xFF), line);
                CurrentChunk.WriteByte((byte)((length >> 8) & 0xFF), line);

                // Write the 2-byte jump offset placeholders
                int arrJumpIfNotMatch = CurrentChunk.Code.Count;
                CurrentChunk.WriteByte(0xff, line);
                CurrentChunk.WriteByte(0xff, line);

                int arrJumpIfMatch = CurrentChunk.EmitJump(OpCode.JUMP, line);

                CurrentChunk.PatchJump(arrJumpIfNotMatch);
                EmitFailureCleanup(baseScopeDepth, failureJumps, line);

                CurrentChunk.PatchJump(arrJumpIfMatch);

                EmitLoadLocal(targetVar, line);
                CurrentChunk.WriteByte(OpCode.UNPACK_ARRAY, line);

                List<string> arrVars = [];
                foreach (var p in arr.Patterns)
                {
                    string pVar = p is IdPattern id ? id.Identifier.Lexeme : $"<arr_{Guid.NewGuid().ToString()[..8]}>";
                    Locals.Add(pVar);
                    arrVars.Add(pVar);
                }

                for (int i = 0; i < arr.Patterns.Count; i++)
                {
                    if (arr.Patterns[i] is not IdPattern)
                        CompilePatternCase(arr.Patterns[i], arrVars[i], failureJumps, baseScopeDepth, line);
                }
                break;

            default:
                FatalError($"Deep pattern compilation not implemented for {pattern.GetType().Name}", GetLeadToken(pattern));
                break;
        }
    }

    private void EmitLoadLocal(string name, int line)
    {
        int index = Locals.LastIndexOf(name);
        if (index == -1) FatalError($"Hidden local '{name}' not found.", null);
        if (index > byte.MaxValue) FatalError("Too many local variables in scope, limit is 255.", null);

        CurrentChunk.WriteByte(OpCode.LOAD_LOCAL, line);
        CurrentChunk.WriteByte((byte)index, line);
    }


    private void EmitConstructor(ConExpr con)
    {
        if (con.Payloads.Count == 1)
        {
            EmitExpression(con.Payloads[0]);
        }
        else if (con.Payloads.Count > 1)
        {
            int trackedPayloads = 0;
            foreach (var p in con.Payloads)
            {
                EmitExpression(p);
                Locals.Add("<temp_con_payload>");
                trackedPayloads++;
            }
            CurrentChunk.WriteByte(OpCode.ALLOC_TUPLE, con.ConstructorName.Line);
            CurrentChunk.WriteByte((byte)con.Payloads.Count, con.ConstructorName.Line);
            Locals.RemoveRange(Locals.Count - trackedPayloads, trackedPayloads);
        }
        else
        {
            CurrentChunk.WriteByte(OpCode.PUSH_UNIT, con.ConstructorName.Line);
        }

        CurrentChunk.WriteByte(OpCode.ALLOC_CON, con.ConstructorName.Line);
        CurrentChunk.WriteByte(GetConstructorTagIndex(con.ConstructorName), con.ConstructorName.Line);
    }

    private void EmitListLiteral(ListLitExpr list)
    {
        int trackedElements = 0;
        foreach (var expr in list.Elements)
        {
            EmitExpression(expr);
            Locals.Add("<temp_list_el>");
            trackedElements++;
        }

        CurrentChunk.WriteByte(OpCode.LIST_EMPTY, list.Operator.Line);
        for (int i = 0; i < list.Elements.Count; i++)
            CurrentChunk.WriteByte(OpCode.LIST_CONS, list.Operator.Line);

        Locals.RemoveRange(Locals.Count - trackedElements, trackedElements);
    }

    private void EmitArrayLiteral(ArrLitExpr arr)
    {
        int count = arr.Elements.Count;
        if (count > ushort.MaxValue)
            FatalError($"Array literal exceeds maximum size of {ushort.MaxValue} elements.", GetLeadToken(arr));

        int trackedElements = 0;
        foreach (var expr in arr.Elements)
        {
            EmitExpression(expr);
            Locals.Add("<temp_arr_el>");
            trackedElements++;
        }

        if (count <= byte.MaxValue)
        {
            CurrentChunk.WriteByte(OpCode.ALLOC_ARRAY, arr.Operator.Line);
            CurrentChunk.WriteByte((byte)count, arr.Operator.Line);
        }
        else
        {
            CurrentChunk.WriteByte(OpCode.ALLOC_ARRAY_LONG, arr.Operator.Line);
            CurrentChunk.WriteByte((byte)(count & 0xFF), arr.Operator.Line);
            CurrentChunk.WriteByte((byte)((count >> 8) & 0xFF), arr.Operator.Line);
        }

        Locals.RemoveRange(Locals.Count - trackedElements, trackedElements);
    }

    private void EmitTupleLiteral(TupleLitExpr tuple)
    {
        int trackedElements = 0;
        foreach (var expr in tuple.Elements)
        {
            EmitExpression(expr);
            Locals.Add("<temp_tuple_el>");
            trackedElements++;
        }

        CurrentChunk.WriteByte(OpCode.ALLOC_TUPLE, tuple.Operator.Line);
        if (tuple.Elements.Count > byte.MaxValue)
            FatalError($"Too many fields in tuple {tuple.Elements.Count}, max 255", GetLeadToken(tuple));
        CurrentChunk.WriteByte((byte)tuple.Elements.Count, tuple.Operator.Line);

        Locals.RemoveRange(Locals.Count - trackedElements, trackedElements);
    }

    private void EmitCall(CallExpr call, bool isTail)
    {
        if (call.Arguments.Count > byte.MaxValue)
            FatalError($"Too many arguments in function call {call.Arguments.Count}, max 255.", GetLeadToken(call));

        if (call.Callee is IdentifierExpr idExpr)
        {
            string fName = res.TryGetValue(idExpr, out string? r) ? r : idExpr.Identifier.Lexeme;
            Symbol? sym = currentEnv?.Resolve(fName);

            if (sym is FuncSymbol { NativeId: not null } funcSym)
            {
                if ((int)funcSym.NativeId > byte.MaxValue)
                    FatalError($"Intrinsic ID '{funcSym.NativeId.Value}' exceeds byte limit.", sym.DeclToken);

                int natTrackedArgs = 0;
                foreach (var arg in call.Arguments)
                {
                    EmitExpression(arg);
                    Locals.Add("<temp_intrinsic_arg>");
                    natTrackedArgs++;
                }

                int line = idExpr.Identifier.Line;
                CurrentChunk.WriteByte(OpCode.CALL_INTRINSIC, line);
                CurrentChunk.WriteByte((byte)funcSym.NativeId.Value, line);
                CurrentChunk.WriteByte((byte)call.Arguments.Count, line);

                Locals.RemoveRange(Locals.Count - natTrackedArgs, natTrackedArgs);
                return;
            }
            else if (sym is ExternSymbol extSym)
            {
                int extTrackedArgs = 0;
                foreach (var arg in call.Arguments)
                {
                    EmitExpression(arg);
                    Locals.Add("<temp_ffi_arg>");
                    extTrackedArgs++;
                }

                int line = idExpr.Identifier.Line;
                int libPathIdx = CurrentChunk.AddConstant(CeraValue.String(extSym.LibraryPath));
                int funcNameIdx = CurrentChunk.AddConstant(CeraValue.String(idExpr.Identifier.Lexeme));

                if (libPathIdx > ushort.MaxValue || funcNameIdx > ushort.MaxValue)
                    FatalError("Constant pool limit exceeded for FFI strings.", idExpr.Identifier);

                CurrentChunk.WriteByte(OpCode.CALL_FFI, line);
                CurrentChunk.WriteByte((byte)call.Arguments.Count, line);
                CurrentChunk.WriteByte((byte)(libPathIdx & 0xFF), line);
                CurrentChunk.WriteByte((byte)((libPathIdx >> 8) & 0xFF), line);                
                CurrentChunk.WriteByte((byte)(funcNameIdx & 0xFF), line);
                CurrentChunk.WriteByte((byte)((funcNameIdx >> 8) & 0xFF), line);
                Locals.RemoveRange(Locals.Count - extTrackedArgs, extTrackedArgs);
                return;
            }

            if (inlineFunctions.TryGetValue(fName, out FuncDeclNode? inlineFunc) && !curInlining.Contains(fName))
            {
                curInlining.Add(fName);
                EmitInlineCall(inlineFunc, call.Arguments, isTail);
                curInlining.Remove(fName);
                return;
            }

            int localIndex = Locals.LastIndexOf(fName);
            int upvalueIndex = ResolveUpvalue(state, fName);

            if (localIndex == -1 && upvalueIndex == -1 && !globalVariables.ContainsKey(fName))
            {
                int funcIndex = GetGlobalFunctionIndex(fName, idExpr.Identifier);
                int line = idExpr.Identifier.Line;

                CurrentChunk.WriteByte(OpCode.PUSH_UNIT, line);
                Locals.Add("<temp_callee_padding>");

                int globTrackedArgs = 0;
                foreach (var arg in call.Arguments)
                {
                    EmitExpression(arg);
                    Locals.Add("<temp_arg>");
                    globTrackedArgs++;
                }

                if (funcIndex <= byte.MaxValue)
                {
                    CurrentChunk.WriteByte(isTail ? OpCode.TAIL_CALL_GLOBAL : OpCode.CALL_GLOBAL, line);
                    CurrentChunk.WriteByte((byte)funcIndex, line);
                }
                else
                {
                    CurrentChunk.WriteByte(isTail ? OpCode.TAIL_CALL_GLOBAL_LONG : OpCode.CALL_GLOBAL_LONG, line);
                    CurrentChunk.WriteByte((byte)(funcIndex & 0xFF), line);
                    CurrentChunk.WriteByte((byte)((funcIndex >> 8) & 0xFF), line);
                }

                CurrentChunk.WriteByte((byte)call.Arguments.Count, line);
                Locals.RemoveRange(Locals.Count - (globTrackedArgs + 1), globTrackedArgs + 1);
                return;
            }
        }

        EmitExpression(call.Callee);
        Locals.Add("<temp_callee>");
        int trackedArgs = 0;
        foreach (var arg in call.Arguments)
        {
            EmitExpression(arg);
            Locals.Add("<temp_arg>");
            trackedArgs++;
        }
        int callLine = (call.Callee as IdentifierExpr)?.Identifier.Line ?? 0;
        if (!isTail) CurrentChunk.WriteByte(OpCode.CALL, callLine);
        else CurrentChunk.WriteByte(OpCode.TAIL_CALL, callLine);
        CurrentChunk.WriteByte((byte)call.Arguments.Count, callLine);

        Locals.RemoveRange(Locals.Count - (trackedArgs + 1), trackedArgs + 1);
    }

    private void EmitInlineCall(FuncDeclNode func, List<IExprAST> arguments, bool isTail)
    {
        int scopeDepth = Locals.Count;
        int line = func.Identifier.Line;

        for (int i = 0; i < arguments.Count; i++)
        {
            CurrentChunk.WriteByte(OpCode.PUSH_UNIT, line);

            Locals.Add("<inline_arg_temp>");

            EmitExpression(arguments[i], false);

            Locals[^1] = func.Parameters[i].Identifier.Lexeme;

            CurrentChunk.WriteByte(OpCode.STORE_LOCAL, line);
            CurrentChunk.WriteByte((byte)(Locals.Count - 1), line);
            CurrentChunk.WriteByte(OpCode.POP, line);
        }

        EmitExpressionBlock(func.Body, isTail);

        int varsToPop = Locals.Count - scopeDepth;
        if (varsToPop > 0)
        {
            int returnValLine = (func.Body.ReturnExpression as IdentifierExpr)?.Identifier.Line ?? line;

            CurrentChunk.WriteByte(OpCode.STORE_LOCAL, returnValLine);
            CurrentChunk.WriteByte((byte)scopeDepth, returnValLine);

            for (int i = 0; i < varsToPop; i++)
            {
                CurrentChunk.WriteByte(OpCode.POP, returnValLine);
            }
        }

        Locals.RemoveRange(scopeDepth, varsToPop);
    }

    private void EmitLambda(LambdaExpr lambda)
    {
        int line = lambda.Parameters.FirstOrDefault()?.Identifier.Line ?? 0;
        var (functionIndex, captures) = CompileAnonymousFunction(lambda);
        if (functionIndex <= byte.MaxValue)
        {
            CurrentChunk.WriteByte(OpCode.LOAD_FUNCTION, line);
            CurrentChunk.WriteByte((byte)functionIndex, line);
        }
        else if (functionIndex <= ushort.MaxValue)
        {
            CurrentChunk.WriteByte(OpCode.LOAD_FUNCTION_LONG, line);
            CurrentChunk.WriteByte((byte)(functionIndex & 0xFF), line);
            CurrentChunk.WriteByte((byte)((functionIndex >> 8) & 0xFF), line);
        }
        else
        {
            FatalError($"Cannot load lambda. Total module functions exceed 65,535 limit.", GetLeadToken(lambda));
        }

        if (captures.Count > byte.MaxValue)
            FatalError($"Lambda captures too many variables ({captures.Count}). Limit is 255.", GetLeadToken(lambda));

        CurrentChunk.WriteByte(OpCode.MAKE_CLOSURE, line);
        CurrentChunk.WriteByte((byte)captures.Count, line); // Tell the VM how many bytes follow
        foreach (var capture in captures)
        {
            // isLocal == 1 means "grab from the current stack frame"
            // isLocal == 0 means "grab from the current closure's upvalues"
            CurrentChunk.WriteByte(capture.IsLocal ? (byte)1 : (byte)0, line);
            CurrentChunk.WriteByte((byte)capture.Index, line);
        }
    }

    private (int functionIndex, List<(string Name, bool IsLocal, int Index)> captures)
        CompileAnonymousFunction(LambdaExpr lambda)
    {
        FuncState previousState = state;
        state = new FuncState(diag) { Enclosing = previousState };

        Locals.Add("<closure_reserved>"); // padding for VM

        foreach (var param in lambda.Parameters)
            Locals.Add(param.Identifier.Lexeme);

        EmitExpression(lambda.Body, true);
        CurrentChunk.WriteByte(OpCode.RETURN, 0);

        int myIndex = nextFunctionIndex++;
        string lambdaName = $"<lambda_{myIndex}>";

        module.DefineFunction(new CompiledFunction(lambdaName, lambda.Parameters.Count, CurrentChunk, myIndex));

        var requiredCaptures = state.Upvalues.ToList();
        state = previousState;

        return (myIndex, requiredCaptures);
    }

    private int EmitConditionAndJump(IExprAST condition, int line)
    {
        if (condition is BinaryExpr bin)
        {
            OpCode? jumpOp = bin.Operator.Tag switch
            {
                TokenType.EqualEqual => OpCode.JUMP_IF_FALSE_EQ,
                TokenType.NotEqual => OpCode.JUMP_IF_FALSE_NEQ,
                TokenType.Lesser => OpCode.JUMP_IF_FALSE_LT,
                TokenType.LesserEqual => OpCode.JUMP_IF_FALSE_LTE,
                TokenType.Greater => OpCode.JUMP_IF_FALSE_GT,
                TokenType.GreaterEqual => OpCode.JUMP_IF_FALSE_GTE,
                _ => null
            };

            if (jumpOp.HasValue)
            {
                EmitExpression(bin.Left);
                Locals.Add("<temp_bin_left>");
                EmitExpression(bin.Right);
                Locals.RemoveAt(Locals.Count - 1);
                return CurrentChunk.EmitJump(jumpOp.Value, line);
            }
        }

        EmitExpression(condition);
        return CurrentChunk.EmitJump(OpCode.JUMP_IF_FALSE, line);
    }

    private void EmitIfExpr(IfExpr ifExpr, bool isTail)
    {
        List<int> exitJumps = [];

        int nextBranchJump = EmitConditionAndJump(ifExpr.Condition, ifExpr.Operator.Line);

        EmitExpressionBlock(ifExpr.TrueBlock, isTail);
        exitJumps.Add(CurrentChunk.EmitJump(OpCode.JUMP, ifExpr.Operator.Line));

        foreach (var (Condition, Block) in ifExpr.ElseIfs)
        {
            CurrentChunk.PatchJump(nextBranchJump);

            EmitExpression(Condition, false);
            nextBranchJump = EmitConditionAndJump(Condition, ifExpr.Operator.Line);

            EmitExpressionBlock(Block, isTail);
            exitJumps.Add(CurrentChunk.EmitJump(OpCode.JUMP, ifExpr.Operator.Line));
        }

        CurrentChunk.PatchJump(nextBranchJump); // final false condition

        if (ifExpr.ElseBlock != null)
            EmitExpressionBlock(ifExpr.ElseBlock, isTail);
        else
            CurrentChunk.WriteByte(OpCode.PUSH_UNIT, ifExpr.Operator.Line);
        foreach (int jump in exitJumps)
            CurrentChunk.PatchJump(jump);
    }

    private void EmitTernaryExpression(TernaryExpr tern, bool isTail)
    {
        int jumpIfFalse = EmitConditionAndJump(tern.Condition, tern.Operator.Line);
        EmitExpression(tern.TrueBranch, isTail);
        int jumpEnd = CurrentChunk.EmitJump(OpCode.JUMP, tern.Operator.Line);
        CurrentChunk.PatchJump(jumpIfFalse);
        EmitExpression(tern.FalseBranch, isTail);
        CurrentChunk.PatchJump(jumpEnd);
    }

    private void EmitExpressionBlock(ExprBlock block, bool isTail)
    {
        int scopeDepth = Locals.Count;

        foreach (var stmt in block.Statements)
        {
            if (stmt is VarDeclStmt varDecl)
            {
                int line = varDecl.Operator.Line;

                if (varDecl.Pattern is IdPattern idPat)
                {
                    // --- Branch 1: Traditional Stack Push (Preserves Lambda Recursion) ---
                    CurrentChunk.WriteByte(OpCode.PUSH_UNIT, line);
                    Locals.Add(idPat.Identifier.Lexeme);
                    EmitExpression(varDecl.Initializer, false);

                    CurrentChunk.WriteByte(OpCode.STORE_LOCAL, line);
                    CurrentChunk.WriteByte((byte)(Locals.Count - 1), line);
                    CurrentChunk.WriteByte(OpCode.POP, line);
                }
                else
                {
                    // --- Branch 2: Structural Stack Unpacking ---
                    CurrentChunk.WriteByte(OpCode.PUSH_UNIT, line);
                    string targetVar = $"<var_target_{Guid.NewGuid().ToString()[..8]}>";
                    Locals.Add(targetVar);

                    EmitExpression(varDecl.Initializer, false);

                    CurrentChunk.WriteByte(OpCode.STORE_LOCAL, line);
                    CurrentChunk.WriteByte((byte)(Locals.Count - 1), line);
                    CurrentChunk.WriteByte(OpCode.POP, line);

                    int scopeDepthBefore = Locals.Count;
                    List<int> failureJumps = [];

                    CompilePatternCase(varDecl.Pattern, targetVar, failureJumps, scopeDepthBefore, line);

                    int successJump = CurrentChunk.EmitJump(OpCode.JUMP, line);

                    foreach (var jump in failureJumps)
                    {
                        CurrentChunk.PatchJump(jump);
                    }
                    CurrentChunk.WriteByte(OpCode.MATCH_FAIL, line);

                    CurrentChunk.PatchJump(successJump);
                }
            }
            else if (stmt is ExprStmt exprStmt)
            {
                EmitExpression(exprStmt.Expression, false);
                CurrentChunk.WriteByte(OpCode.POP, 0); // pop statement result
            }
        }

        EmitExpression(block.ReturnExpression, isTail);

        int varsToPop = Locals.Count - scopeDepth;
        if (varsToPop > 0)
        {
            int line = (block.ReturnExpression as IdentifierExpr)?.Identifier.Line ?? 0;

            CurrentChunk.WriteByte(OpCode.STORE_LOCAL, line);
            CurrentChunk.WriteByte((byte)scopeDepth, line);

            for (int i = 0; i < varsToPop; i++)
            {
                CurrentChunk.WriteByte(OpCode.POP, line);
            }
        }

        Locals.RemoveRange(scopeDepth, varsToPop);
    }

    private void EmitSwitchExpression(SwitchExpr sw, bool isTail)
    {
        EmitExpression(sw.TargetExpression);

        string rootTarget = $"<switch_target_{Guid.NewGuid().ToString()[..8]}>";
        Locals.Add(rootTarget);

        List<int> endJumps = [];

        foreach (var matchCase in sw.Cases)
        {
            int scopeDepthBeforeCase = Locals.Count;
            List<int> failureJumps = [];

            CompilePatternCase(matchCase.Pattern, rootTarget, failureJumps, scopeDepthBeforeCase, sw.Operator.Line);

            if (matchCase.Guard != null)
            {
                EmitExpression(matchCase.Guard, false);
                int guardJumpIfTrue = CurrentChunk.EmitJump(OpCode.JUMP_IF_TRUE, sw.Operator.Line);
                EmitFailureCleanup(scopeDepthBeforeCase, failureJumps, sw.Operator.Line);
                CurrentChunk.PatchJump(guardJumpIfTrue);
            }

            EmitExpression(matchCase.ResultExpression, isTail);

            int rootTargetIndex = Locals.IndexOf(rootTarget);
            if (rootTargetIndex > byte.MaxValue) FatalError("Too many local variables in scope. Limit is 255.", sw.Operator);

            CurrentChunk.WriteByte(OpCode.STORE_LOCAL, sw.Operator.Line);
            CurrentChunk.WriteByte((byte)rootTargetIndex, sw.Operator.Line);

            CurrentChunk.WriteByte(OpCode.POP, sw.Operator.Line);

            int variablesPushed = Locals.Count - scopeDepthBeforeCase;
            for (int i = 0; i < variablesPushed; i++)
            {
                CurrentChunk.WriteByte(OpCode.POP, sw.Operator.Line);
            }

            endJumps.Add(CurrentChunk.EmitJump(OpCode.JUMP, sw.Operator.Line));

            foreach (var jump in failureJumps)
            {
                CurrentChunk.PatchJump(jump);
            }

            Locals.RemoveRange(scopeDepthBeforeCase, variablesPushed);
        }

        CurrentChunk.WriteByte(OpCode.MATCH_FAIL, sw.Operator.Line);

        foreach (var jump in endJumps)
        {
            CurrentChunk.PatchJump(jump);
        }

        Locals.RemoveAt(Locals.Count - 1);
    }


    private void EmitUnaryExpression(UnaryExpr unary)
    {
        EmitExpression(unary.Right);
        int line = unary.Operator.Line;
        switch (unary.Operator.Tag)
        {
            case TokenType.Minus: CurrentChunk.WriteByte(OpCode.NEGATE, line); break;
            case TokenType.Not: CurrentChunk.WriteByte(OpCode.NOT, line); break;
            case TokenType.BitNot: CurrentChunk.WriteByte(OpCode.BIT_NOT, line); break;
            default: FatalError($"Invalid unary operator for emission: '{unary.Operator.Lexeme}'", GetLeadToken(unary)); break;
        }
    }

    private void EmitBinaryExpression(BinaryExpr binary)
    {
        if (binary.Operator.Tag == TokenType.And || binary.Operator.Tag == TokenType.Or)
        {
            EmitLogicalExpression(binary); return;
        }
        if (binary.Operator.Tag == TokenType.ColonColon)
        {
            EmitConsExpression(binary); return;
        }

        if (binary.Operator.Tag == TokenType.Plus && binary.Right is LiteralExpr rLitP && rLitP.Value.Lexeme == "1")
        {
            EmitExpression(binary.Left);
            CurrentChunk.WriteByte(OpCode.ADD_1, binary.Operator.Line);
            return;
        }
        if (binary.Operator.Tag == TokenType.Minus && binary.Right is LiteralExpr rLitM && rLitM.Value.Lexeme == "1")
        {
            EmitExpression(binary.Left);
            CurrentChunk.WriteByte(OpCode.SUB_1, binary.Operator.Line);
            return;
        }

        EmitExpression(binary.Left);
        Locals.Add("<temp_bin_left>");
        EmitExpression(binary.Right);
        Locals.RemoveAt(Locals.Count - 1);

        int line = binary.Operator.Line;
        switch (binary.Operator.Tag)
        {
            // Mathematical ALU
            case TokenType.Plus: CurrentChunk.WriteByte(OpCode.ADD, line); break;
            case TokenType.Minus: CurrentChunk.WriteByte(OpCode.SUB, line); break;
            case TokenType.Star: CurrentChunk.WriteByte(OpCode.MUL, line); break;
            case TokenType.Slash: CurrentChunk.WriteByte(OpCode.DIV, line); break;
            case TokenType.Mod: CurrentChunk.WriteByte(OpCode.MOD, line); break;

            // Bitwise ALU
            case TokenType.BitAnd: CurrentChunk.WriteByte(OpCode.BIT_AND, line); break;
            case TokenType.Pipe: CurrentChunk.WriteByte(OpCode.BIT_OR, line); break;
            case TokenType.BitXor: CurrentChunk.WriteByte(OpCode.BIT_XOR, line); break;
            case TokenType.LShift: CurrentChunk.WriteByte(OpCode.SHL, line); break;
            case TokenType.RShift: CurrentChunk.WriteByte(OpCode.SHR, line); break;

            // Relational ALU
            case TokenType.EqualEqual: CurrentChunk.WriteByte(OpCode.EQ, line); break;
            case TokenType.NotEqual: CurrentChunk.WriteByte(OpCode.NEQ, line); break;
            case TokenType.Lesser: CurrentChunk.WriteByte(OpCode.LT, line); break;
            case TokenType.LesserEqual: CurrentChunk.WriteByte(OpCode.LTE, line); break;
            case TokenType.Greater: CurrentChunk.WriteByte(OpCode.GT, line); break;
            case TokenType.GreaterEqual: CurrentChunk.WriteByte(OpCode.GTE, line); break;

            default: FatalError($"Invalid binary operator for emission: '{binary.Operator.Lexeme}'", GetLeadToken(binary)); break;
        }
    }

    private void EmitLogicalExpression(BinaryExpr binary)
    {
        EmitExpression(binary.Left);

        if (binary.Operator.Tag == TokenType.And)
        {
            int jumpIfFalse = CurrentChunk.EmitJump(OpCode.JUMP_IF_FALSE_PEEK, binary.Operator.Line);
            CurrentChunk.WriteByte(OpCode.POP, binary.Operator.Line); // Pop left value
            EmitExpression(binary.Right);
            CurrentChunk.PatchJump(jumpIfFalse);
        }
        else if (binary.Operator.Tag == TokenType.Or)
        {
            int jumpIfTrue = CurrentChunk.EmitJump(OpCode.JUMP_IF_TRUE_PEEK, binary.Operator.Line);
            CurrentChunk.WriteByte(OpCode.POP, binary.Operator.Line); // Pop left value
            EmitExpression(binary.Right);
            CurrentChunk.PatchJump(jumpIfTrue);
        }
    }

    private void EmitConsExpression(BinaryExpr binary)
    {
        EmitExpression(binary.Left);
        Locals.Add("<temp_cons_left>");

        EmitExpression(binary.Right);
        Locals.RemoveAt(Locals.Count - 1);

        CurrentChunk.WriteByte(OpCode.LIST_CONS, binary.Operator.Line);
    }

    private void EmitIdentifier(IdentifierExpr id)
    {
        string name = res.TryGetValue(id, out string? r) ? r : id.Identifier.Lexeme;
        int line = id.Identifier.Line;

        int index = Locals.LastIndexOf(name);
        if (index != -1)
        {
            if (index > byte.MaxValue) FatalError("Too many local variables in scope. Limit is 255.", id.Identifier);
            CurrentChunk.WriteByte(OpCode.LOAD_LOCAL, line);
            CurrentChunk.WriteByte((byte)index, line);
            return;
        }

        int upvalueIndex = ResolveUpvalue(state, name);
        if (upvalueIndex != -1)
        {
            if (upvalueIndex > byte.MaxValue) FatalError("Too many captured variables. Limit is 255.", id.Identifier);
            CurrentChunk.WriteByte(OpCode.LOAD_UPVALUE, line);
            CurrentChunk.WriteByte((byte)upvalueIndex, line);
            return;
        }

        if (globalVariables.TryGetValue(name, out IExprAST? globalInit))
        {
            EmitExpression(globalInit);
            return;
        }

        // If it's not local or upvalue, it MUST be a global function. 
        int funcIndex = GetGlobalFunctionIndex(name, id.Identifier);

        if (funcIndex <= byte.MaxValue)
        {
            CurrentChunk.WriteByte(OpCode.LOAD_FUNCTION, line);
            CurrentChunk.WriteByte((byte)funcIndex, line);
        }
        else if (funcIndex <= ushort.MaxValue)
        {
            CurrentChunk.WriteByte(OpCode.LOAD_FUNCTION_LONG, line);
            CurrentChunk.WriteByte((byte)(funcIndex & 0xFF), line);
            CurrentChunk.WriteByte((byte)((funcIndex >> 8) & 0xFF), line);
        }
        else
        {
            FatalError($"Cannot load function '{name}'. Total module functions exceed 65,535 limit.", id.Identifier);
        }

        CurrentChunk.WriteByte(OpCode.MAKE_CLOSURE, line);
        CurrentChunk.WriteByte((byte)0, line);
    }

    private void EmitLiteral(LiteralExpr lit)
    {
        int line = lit.Value.Line;

        switch (lit.Value.Tag)
        {
            case TokenType.True: CurrentChunk.WriteByte((byte)OpCode.PUSH_TRUE, line); break;
            case TokenType.False: CurrentChunk.WriteByte((byte)OpCode.PUSH_FALSE, line); break;
            case TokenType.Unit: CurrentChunk.WriteByte((byte)OpCode.PUSH_UNIT, line); break;
            case TokenType.IntLiteral:
                long intVal = long.Parse(lit.Value.Lexeme);
                if (intVal == 0) CurrentChunk.WriteByte((byte)OpCode.PUSH_0, line);
                else if (intVal == 1) CurrentChunk.WriteByte((byte)OpCode.PUSH_1, line);
                else if (intVal >= sbyte.MinValue && intVal <= sbyte.MaxValue)
                {
                    CurrentChunk.WriteByte((byte)OpCode.PUSH_BYTE, line);
                    CurrentChunk.WriteByte((byte)(sbyte)intVal, line);
                }
                else
                {
                    int idx = CurrentChunk.AddConstant(CeraValue.Int(intVal));
                    EmitLoadConst(idx, line);
                }
                break;
            case TokenType.FloatLiteral:
                double floatVal = double.Parse(lit.Value.Lexeme);
                int fIdx = CurrentChunk.AddConstant(CeraValue.Float(floatVal));
                EmitLoadConst(fIdx, line);
                break;

            case TokenType.CharLiteral:
                string rawChar = lit.Value.Lexeme.Trim('\'');
                int codePoint = char.ConvertToUtf32(rawChar, 0);

                CurrentChunk.WriteByte((byte)OpCode.PUSH_CHAR, line);
                for (int i = 0; i < 4; i++)
                {
                    CurrentChunk.WriteByte((byte)((codePoint >> (i * 8)) & 0xFF), line);
                }
                break;

            case TokenType.StringLiteral:
                string rawStr = lit.Value.Lexeme.Trim('"');
                int strIdx = CurrentChunk.AddConstant(CeraValue.String(rawStr));
                EmitLoadConst(strIdx, line);
                break;
            default:
                FatalError($"Unknown literal type for emission '{lit.Value.Tag}'", GetLeadToken(lit));
                break;
        }
    }

    private void EmitLoadConst(int idx, int line)
    {
        if (idx < byte.MaxValue)
        {
            CurrentChunk.WriteByte(OpCode.LOAD_CONST, line);
            CurrentChunk.WriteByte((byte)idx, line);
        }
        else if (idx < ushort.MaxValue)
        {
            CurrentChunk.WriteByte(OpCode.LOAD_CONST_LONG, line);
            CurrentChunk.WriteByte((byte)(idx & 0xFF), line);        // Low byte
            CurrentChunk.WriteByte((byte)((idx >> 8) & 0xFF), line); // High byte
        }
        else FatalError($"Too many constants in one chunk, limit is 65,535.", null);
    }
}