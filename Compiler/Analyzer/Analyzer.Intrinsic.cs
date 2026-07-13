using Cera.Compiler.Parser;
using Cera.Compiler.Lexer;

namespace Cera.Compiler.Analyzer;

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
            "rand", "randInt"
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

        void DefineIntrinsic(string name, IntrinsicId nativeId, ITypeAST type, int arity, List<Token>? generics = null)
        {
            generics ??= [];
            globalEnv.Define(name, new FuncSymbol(intrT[name], type, arity, generics, nativeId));
        }

        DefineIntrinsic("get", IntrinsicId.Get,
            new FuncType(new TupleType([new ArrType(gType), new BaseType(intrT["int"])]),
            new GenericType(intrT["option"], [gType])),
            2, [gParamToken]);

        // arrLength<g>(array: g arr) : int
        DefineIntrinsic("arrLength", IntrinsicId.ArrLength,
            new FuncType(new ArrType(gType), new BaseType(intrT["int"])),
            1, [gParamToken]);

        // concat<g>(left: g list, right: g list) : g list
        DefineIntrinsic("concat", IntrinsicId.Concat,
            new FuncType(new TupleType([new ListType(gType), new ListType(gType)]), new ListType(gType)),
            2, [gParamToken]);

        // arrConcat<g>(left: g arr, right: g arr) : g arr
        DefineIntrinsic("arrConcat", IntrinsicId.ArrConcat,
            new FuncType(new TupleType([new ArrType(gType), new ArrType(gType)]), new ArrType(gType)),
            2, [gParamToken]);

        // out(output: char list) : unit
        DefineIntrinsic("out", IntrinsicId.Out,
            new FuncType(new ListType(new BaseType(intrT["char"])), new BaseType(intrT["unit"])),
            1);

        // in() : char list
        DefineIntrinsic("in", IntrinsicId.In,
            new FuncType(new BaseType(intrT["unit"]), new ListType(new BaseType(intrT["char"]))),
            1);

        // read(path: char list) : result<char list, char list>
        DefineIntrinsic("read", IntrinsicId.Read,
            new FuncType(new ListType(new BaseType(intrT["char"])),
            new GenericType(intrT["result"], [new ListType(new BaseType(intrT["char"])), new ListType(new BaseType(intrT["char"]))])),
            1);

        // write(path: char list, content: char list) : result<unit, char list>
        DefineIntrinsic("write", IntrinsicId.Write,
            new FuncType(new TupleType([new ListType(new BaseType(intrT["char"])), new ListType(new BaseType(intrT["char"]))]),
            new GenericType(intrT["result"], [new BaseType(intrT["unit"]), new ListType(new BaseType(intrT["char"]))])),
            2);

        // intToFloat(x: int) : float
        DefineIntrinsic("intToFloat", IntrinsicId.IntToFloat,
            new FuncType(new BaseType(intrT["int"]), new BaseType(intrT["float"])),
            1);

        // floatToInt(x : float) : int
        DefineIntrinsic("floatToInt", IntrinsicId.FloatToInt,
            new FuncType(new BaseType(intrT["float"]), new BaseType(intrT["int"])),
            1);

        // charToInt(c : char) : int
        DefineIntrinsic("charToInt", IntrinsicId.CharToInt,
            new FuncType(new BaseType(intrT["char"]), new BaseType(intrT["int"])),
            1);

        // intToChar(x : int) : char
        DefineIntrinsic("intToChar", IntrinsicId.IntToChar,
            new FuncType(new BaseType(intrT["int"]), new BaseType(intrT["char"])),
            1);

        // intToChars(x : int) : char list
        DefineIntrinsic("intToChars", IntrinsicId.IntToChars,
            new FuncType(new BaseType(intrT["int"]), new ListType(new BaseType(intrT["char"]))),
            1);

        // floatToChars(x : float) : char list
        DefineIntrinsic("floatToChars", IntrinsicId.FloatToChars,
            new FuncType(new BaseType(intrT["float"]), new ListType(new BaseType(intrT["char"]))),
            1);

        // boolToChars(x : bool) : char list
        DefineIntrinsic("boolToChars", IntrinsicId.BoolToChars,
            new FuncType(new BaseType(intrT["bool"]), new ListType(new BaseType(intrT["char"]))),
            1);

        // charsToInt(str : char list) : option<int>
        DefineIntrinsic("charsToInt", IntrinsicId.CharsToInt,
            new FuncType(new ListType(new BaseType(intrT["char"])),
            new GenericType(intrT["option"], [new BaseType(intrT["int"])])),
            1);

        // charsToFloat(str : char list) : option<float>
        DefineIntrinsic("charsToFloat", IntrinsicId.CharsToFloat,
            new FuncType(new ListType(new BaseType(intrT["char"])),
            new GenericType(intrT["option"], [new BaseType(intrT["float"])])),
            1);

        // arrToList<g>(array: g arr) : g list
        DefineIntrinsic("arrToList", IntrinsicId.ArrToList,
            new FuncType(new ArrType(gType), new ListType(gType)),
            1, [gParamToken]);

        // listToArr<g>(lst: g list) : g arr
        DefineIntrinsic("listToArr", IntrinsicId.ListToArr,
            new FuncType(new ListType(gType), new ArrType(gType)),
            1, [gParamToken]);

        // rand() : float
        DefineIntrinsic("rand", IntrinsicId.Rand,
            new FuncType(new BaseType(intrT["unit"]), new BaseType(intrT["float"])),
            0);

        // randInt() : int
        DefineIntrinsic("randInt", IntrinsicId.RandInt, 
            new FuncType(new BaseType(intrT["unit"]), new BaseType(intrT["int"])), 
            0);

        // sqrt(x : float) : float
        DefineIntrinsic("sqrt", IntrinsicId.Sqrt, 
            new FuncType(new BaseType(intrT["float"]), new BaseType(intrT["float"])),
            1);
    }
}