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
        // Dynamically calculate how many items are currently polluting the stack
        int varsToPop = Locals.Count - baseScopeDepth;
        for (int i = 0; i < varsToPop; i++)
        {
            CurrentChunk.WriteByte(OpCode.POP, line);
        }

        // Append an unconditional jump to the next switch case
        failureJumps.Add(CurrentChunk.EmitJump(OpCode.JUMP, line));
    }

    private void CompilePatternCase(IPatternAST pattern, string targetVar, List<int> failureJumps, int baseScopeDepth, int line)
    {
        switch (pattern)
        {
            case LiteralPattern lit:
                if (lit.Value.Tag == TokenType.WildCard) break;

                EmitLoadLocal(targetVar, line);
                EmitLiteral(new LiteralExpr(lit.Value));
                CurrentChunk.WriteByte(OpCode.EQ, line);

                // Jump over the cleanup block if the test succeeds
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
                CurrentChunk.WriteByte(OpCode.MATCH_TAG, line);
                CurrentChunk.WriteByte(GetConstructorTagIndex(con.ConstructorName), line);

                int conJumpIfTrue = CurrentChunk.EmitJump(OpCode.JUMP_IF_TRUE, line);
                EmitFailureCleanup(baseScopeDepth, failureJumps, line);
                CurrentChunk.PatchJump(conJumpIfTrue);

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
                CurrentChunk.WriteByte(OpCode.MATCH_TAG, line);
                CurrentChunk.WriteByte(constructorTags["Cons"], line);

                int consJumpIfTrue = CurrentChunk.EmitJump(OpCode.JUMP_IF_TRUE, line);
                EmitFailureCleanup(baseScopeDepth, failureJumps, line);
                CurrentChunk.PatchJump(consJumpIfTrue);

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
                if (list.Patterns.Count == 0)
                {
                    EmitLoadLocal(targetVar, line);
                    CurrentChunk.WriteByte(OpCode.IS_LIST_EMPTY, line);

                    int emptyJumpIfTrue = CurrentChunk.EmitJump(OpCode.JUMP_IF_TRUE, line);
                    EmitFailureCleanup(baseScopeDepth, failureJumps, line);
                    CurrentChunk.PatchJump(emptyJumpIfTrue);
                }
                else
                {
                    string currentListVar = targetVar;
                    for (int i = 0; i < list.Patterns.Count; i++)
                    {
                        EmitLoadLocal(currentListVar, line);
                        CurrentChunk.WriteByte(OpCode.MATCH_TAG, line);
                        CurrentChunk.WriteByte(constructorTags["Cons"], line);

                        int listJumpIfTrue = CurrentChunk.EmitJump(OpCode.JUMP_IF_TRUE, line);
                        EmitFailureCleanup(baseScopeDepth, failureJumps, line);
                        CurrentChunk.PatchJump(listJumpIfTrue);

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

                    EmitLoadLocal(currentListVar, line);
                    CurrentChunk.WriteByte(OpCode.IS_LIST_EMPTY, line);

                    int finalEmptyJumpIfTrue = CurrentChunk.EmitJump(OpCode.JUMP_IF_TRUE, line);
                    EmitFailureCleanup(baseScopeDepth, failureJumps, line);
                    CurrentChunk.PatchJump(finalEmptyJumpIfTrue);
                }
                break;

            case ArrPattern arr:
                EmitLoadLocal(targetVar, line);
                int length = arr.Patterns.Count;

                if (length == 0) CurrentChunk.WriteByte(OpCode.PUSH_0, line);
                else if (length == 1) CurrentChunk.WriteByte(OpCode.PUSH_1, line);
                else if (length <= 127)
                {
                    CurrentChunk.WriteByte(OpCode.PUSH_BYTE, line);
                    CurrentChunk.WriteByte((byte)length, line);
                }
                else
                {
                    int idx = CurrentChunk.AddConstant(CeraValue.Int(length));
                    EmitLoadConst(idx, line);
                }

                CurrentChunk.WriteByte(OpCode.MATCH_ARRAY_LENGTH, line);

                int arrJumpIfTrue = CurrentChunk.EmitJump(OpCode.JUMP_IF_TRUE, line);
                EmitFailureCleanup(baseScopeDepth, failureJumps, line);
                CurrentChunk.PatchJump(arrJumpIfTrue);

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
            Symbol? sym = env?.Resolve(fName);

            if (sym is FuncSymbol { NativeId: not null } funcSym)
            {
                if ((int)funcSym.NativeId > byte.MaxValue)
                    FatalError($"Intrinsic ID '{funcSym.NativeId.Value}' exceeds byte limit.", sym.DeclToken);

                foreach (var arg in call.Arguments) EmitExpression(arg);

                int line = idExpr.Identifier.Line;
                CurrentChunk.WriteByte(OpCode.CALL_INTRINSIC, line);
                CurrentChunk.WriteByte((byte)funcSym.NativeId.Value, line);
                CurrentChunk.WriteByte((byte)call.Arguments.Count, line);
                return;
            }

            if (inlineFunctions.TryGetValue(fName, out FuncDeclNode? inlineFunc) && !curInlining.Contains(fName))
            {
                curInlining.Add(fName);
                EmitInlineCall(inlineFunc, call.Arguments, isTail);
                curInlining.Remove(fName);
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


    private void EmitIfExpr(IfExpr ifExpr, bool isTail)
    {
        List<int> exitJumps = [];

        EmitExpression(ifExpr.Condition);
        int nextBranchJump = CurrentChunk.EmitJump(OpCode.JUMP_IF_FALSE, ifExpr.Operator.Line);

        EmitExpressionBlock(ifExpr.TrueBlock, isTail);
        exitJumps.Add(CurrentChunk.EmitJump(OpCode.JUMP, ifExpr.Operator.Line));

        foreach (var (Condition, Block) in ifExpr.ElseIfs)
        {
            CurrentChunk.PatchJump(nextBranchJump);

            EmitExpression(Condition, false);
            nextBranchJump = CurrentChunk.EmitJump(OpCode.JUMP_IF_FALSE, ifExpr.Operator.Line);

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
        EmitExpression(tern.Condition, false);
        int jumpIfFalse = CurrentChunk.EmitJump(OpCode.JUMP_IF_FALSE, tern.Operator.Line);
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
                int line = varDecl.Identifier.Line;
                CurrentChunk.WriteByte(OpCode.PUSH_UNIT, line);
                Locals.Add(varDecl.Identifier.Lexeme);
                EmitExpression(varDecl.Initializer, false);

                CurrentChunk.WriteByte(OpCode.STORE_LOCAL, line);
                CurrentChunk.WriteByte((byte)(Locals.Count - 1), line);

                // FIX: Pop the ghost variable left behind by the VM's PEEK!
                CurrentChunk.WriteByte(OpCode.POP, line);
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

            EmitExpression(matchCase.ResultExpression, isTail);

            int rootTargetIndex = Locals.IndexOf(rootTarget);
            if (rootTargetIndex > byte.MaxValue) FatalError("Too many local variables in scope. Limit is 255.", sw.Operator);

            // Overwrite the root target with the result of the case
            CurrentChunk.WriteByte(OpCode.STORE_LOCAL, sw.Operator.Line);
            CurrentChunk.WriteByte((byte)rootTargetIndex, sw.Operator.Line);

            // FIX: Pop the ghost result left behind by the VM's PEEK!
            CurrentChunk.WriteByte(OpCode.POP, sw.Operator.Line);

            // Safely clean up the destructured pattern variables
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
            EmitLogicalExpression(binary);
            return;
        }

        if (binary.Operator.Tag == TokenType.ColonColon)
        {
            EmitConsExpression(binary);
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
            int jumpIfFalse = CurrentChunk.EmitJump(OpCode.JUMP_IF_FALSE, binary.Operator.Line);
            EmitExpression(binary.Right);
            int jumpEnd = CurrentChunk.EmitJump(OpCode.JUMP, binary.Operator.Line);
            CurrentChunk.PatchJump(jumpIfFalse);
            CurrentChunk.WriteByte(OpCode.PUSH_FALSE, binary.Operator.Line);
            CurrentChunk.PatchJump(jumpEnd);
        }
        else if (binary.Operator.Tag == TokenType.Or)
        {
            int jumpIfTrue = CurrentChunk.EmitJump(OpCode.JUMP_IF_TRUE, binary.Operator.Line);
            EmitExpression(binary.Right);
            int jumpEnd = CurrentChunk.EmitJump(OpCode.JUMP, binary.Operator.Line);
            CurrentChunk.PatchJump(jumpIfTrue);
            CurrentChunk.WriteByte(OpCode.PUSH_TRUE, binary.Operator.Line);
            CurrentChunk.PatchJump(jumpEnd);
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