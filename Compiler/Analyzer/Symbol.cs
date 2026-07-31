using Cera.Compiler.Lexer;
using Cera.Compiler.Parser;

namespace Cera.Compiler.Analyzer;

/// <summary> The abstract base record for all named entities in the Cera environment. </summary>
public abstract record Symbol(Token DeclToken, ITypeAST? Type);

/// <summary> Represents standard variable bindings, function parameters. </summary>
public record VarSymbol(Token DeclToken, ITypeAST? Type) 
    : Symbol(DeclToken, Type);

/// <summary>
/// Represents named functions declared via the 'def' keyword.
public record FuncSymbol(Token DeclToken, ITypeAST Type, int Arity, List<Token> GenericParams, List<ParamNode>? Parameters, IntrinsicId? NativeId = null) 
    : Symbol(DeclToken, Type);


/// <summary> Represents user-defined Algebraic Data Types (e.g., type stream<g>). </summary>
public record TypeSymbol(Token DeclToken, ITypeAST Type, List<Token> GenericParams) 
    : Symbol(DeclToken, Type);

/// <summary> Represents the data constructors of an ADT (e.g., 'Nil' or 'Cons'). </summary>
public record ConstructorSymbol(Token DeclToken, ITypeAST ParentType, ITypeAST? PayloadType) 
    : Symbol(DeclToken, ParentType);

/// <summary> Represents a generic type parameter (like 'g') currently in scope. </summary>
public record GenericParamSymbol(Token DeclToken) 
    : Symbol(DeclToken, null);