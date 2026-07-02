namespace Cera.Compiler.Analyzer;

using Cera.Compiler.Parser;

public partial class Analyzer
{
    private readonly Dictionary<string, Token> intrT = [];

    private void InitializeIntrinsics()
    {
        InitializeBaseTokens();
        InitializeIntrinsicTypes();
        InitializeIntrinsicFunctions();
    }

    private void InitializeBaseTokens()
    {
        foreach (string key in new[] {
            "option", "None", "Some", "g", // option
            "result", "Ok", "Error", "v", "e", // result
            // functions / params
            "get", "target", "index", "arrLength", "array",
            "concat", "left", "right", "arrConcat",
            "out", "output", "in", "read", "path", "write", "content",
            "intToFloat", "x", "floatToInt", "charToInt", "c", "intToChar",
            "intToChars", "floatToChars", "boolToChars",
            "charsToInt", "str", "charsToFloat",
            "arrToList", "listToArr", "lst",
            "build", "arrBuild", "size", "f",
        })
        {
            intrT[key] = Token.BuiltIn(char.IsUpper(key[0]) ? TokenType.Constructor : TokenType.Identifier, key);
        }
        ;

        intrT["int"] = Token.BuiltIn(TokenType.IntLiteral, "int");
        intrT["float"] = Token.BuiltIn(TokenType.FloatLiteral, "float");
        intrT["bool"] = Token.BuiltIn(TokenType.Bool, "bool");
        intrT["char"] = Token.BuiltIn(TokenType.Char, "char");
        intrT["unit"] = Token.BuiltIn(TokenType.Unit, "unit");

    }

    private void InitializeIntrinsicTypes()
    {
        // type option<g> = None | Some : g 
        RegisterType(new TypeDeclNode(intrT["option"], new GenericDeclNode([intrT["g"]]),
            [new ConDeclNode(intrT["None"], null), new ConDeclNode(intrT["Some"], new BaseType(intrT["g"]))]
        ));

        // type result<v, e> = Ok : v | Error : e
        RegisterType(new TypeDeclNode(intrT["result"], new GenericDeclNode([intrT["v"], intrT["e"]]),
            [new ConDeclNode(intrT["Ok"], new BaseType(intrT["v"])), new ConDeclNode(intrT["Error"], new BaseType(intrT["e"]))]
        ));

        foreach (string prim in new[] { "int", "float", "char", "bool", "unit" })
        {
            var token = intrT[prim];
            globalEnv.Define(prim, new TypeSymbol(token, new BaseType(token), []));
        }
    }

    private void InitializeIntrinsicFunctions()
    {
        Token gParamToken = intrT["g"];
        ITypeAST gType = new BaseType(gParamToken);

        void DefineIntrinsic(string name, ITypeAST type, int arity, List<Token>? generics = null)
        {
            generics ??= [];
            globalEnv.Define(name, new FuncSymbol(intrT[name], type, arity, generics, true));
        }

        DefineIntrinsic("get",
            new FuncType(new TupleType([new ArrType(gType), new BaseType(intrT["int"])]),
            new GenericType(intrT["option"], [gType])),
            2, [gParamToken]);

        // arrLength<g>(array: g arr) : int
        DefineIntrinsic("arrLength",
            new FuncType(new ArrType(gType), new BaseType(intrT["int"])),
            1, [gParamToken]);

        // build<g>(size: int, f: int -> g) : g list
        DefineIntrinsic("build",
            new FuncType(
                new TupleType([
                    new BaseType(intrT["int"]),
            new FuncType(new BaseType(intrT["int"]), gType)
                ]),
                new ListType(gType)
            ),
            2, [gParamToken]);

        // arrBuild<g>(size: int, f: int -> g) : g arr
        DefineIntrinsic("arrBuild",
            new FuncType(
                new TupleType([
                    new BaseType(intrT["int"]),
            new FuncType(new BaseType(intrT["int"]), gType)
                ]),
                new ArrType(gType)
            ),
            2, [gParamToken]);

        // concat<g>(left: g list, right: g list) : g list
        DefineIntrinsic("concat",
            new FuncType(new TupleType([new ListType(gType), new ListType(gType)]), new ListType(gType)),
            2, [gParamToken]);

        // arrConcat<g>(left: g arr, right: g arr) : g arr
        DefineIntrinsic("arrConcat",
            new FuncType(new TupleType([new ArrType(gType), new ArrType(gType)]), new ArrType(gType)),
            2, [gParamToken]);

        // out(output: char list) : unit
        DefineIntrinsic("out",
            new FuncType(new ListType(new BaseType(intrT["char"])), new BaseType(intrT["unit"])),
            1);

        // in() : char list
        DefineIntrinsic("in",
            new FuncType(new BaseType(intrT["unit"]), new ListType(new BaseType(intrT["char"]))),
            1);

        // read(path: char list) : result<char list, char list>
        DefineIntrinsic("read",
            new FuncType(new ListType(new BaseType(intrT["char"])),
            new GenericType(intrT["result"], [new ListType(new BaseType(intrT["char"])), new ListType(new BaseType(intrT["char"]))])),
            1);

        // write(path: char list, content: char list) : result<unit, char list>
        DefineIntrinsic("write",
            new FuncType(new TupleType([new ListType(new BaseType(intrT["char"])), new ListType(new BaseType(intrT["char"]))]),
            new GenericType(intrT["result"], [new BaseType(intrT["unit"]), new ListType(new BaseType(intrT["char"]))])),
            2);

        // intToFloat(x: int) : float
        DefineIntrinsic("intToFloat",
            new FuncType(new BaseType(intrT["int"]), new BaseType(intrT["float"])),
            1);

        // floatToInt(x : float) : int
        DefineIntrinsic("floatToInt",
            new FuncType(new BaseType(intrT["float"]), new BaseType(intrT["int"])),
            1);

        // charToInt(c : char) : int
        DefineIntrinsic("charToInt",
            new FuncType(new BaseType(intrT["char"]), new BaseType(intrT["int"])),
            1);

        // intToChar(x : int) : char
        DefineIntrinsic("intToChar",
            new FuncType(new BaseType(intrT["int"]), new BaseType(intrT["char"])),
            1);

        // intToChars(x : int) : char list
        DefineIntrinsic("intToChars",
            new FuncType(new BaseType(intrT["int"]), new ListType(new BaseType(intrT["char"]))),
            1);

        // floatToChars(x : float) : char list
        DefineIntrinsic("floatToChars",
            new FuncType(new BaseType(intrT["float"]), new ListType(new BaseType(intrT["char"]))),
            1);

        // boolToChars(x : bool) : char list
        DefineIntrinsic("boolToChars",
            new FuncType(new BaseType(intrT["bool"]), new ListType(new BaseType(intrT["char"]))),
            1);

        // charsToInt(str : char list) : option<int>
        DefineIntrinsic("charsToInt",
            new FuncType(new ListType(new BaseType(intrT["char"])),
            new GenericType(intrT["option"], [new BaseType(intrT["int"])])),
            1);

        // charsToFloat(str : char list) : option<float>
        DefineIntrinsic("charsToFloat",
            new FuncType(new ListType(new BaseType(intrT["char"])),
            new GenericType(intrT["option"], [new BaseType(intrT["float"])])),
            1);

        // arrToList<g>(array: g arr) : g list
        DefineIntrinsic("arrToList",
            new FuncType(new ArrType(gType), new ListType(gType)),
            1, [gParamToken]);

        // listToArr<g>(lst: g list) : g arr
        DefineIntrinsic("listToArr",
            new FuncType(new ListType(gType), new ArrType(gType)),
            1, [gParamToken]);
    }
}