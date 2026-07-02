using Cera.Compiler.Lexer;

namespace Cera.Compiler.Parser;

// --- Interfaces ---

public interface INodeAST;
public interface IExprAST : INodeAST;
public interface IStmtAST : INodeAST;
public interface ITypeAST : INodeAST;
public interface IPatternAST : INodeAST; 

// --- Top Level ---

public record ProgramNode(List<FuncDeclNode> Functions, List<TypeDeclNode> Types) : INodeAST;

public record FuncDeclNode(Token Identifier, GenericDeclNode? GenericTypeParams, List<ParamNode> Parameters,
ITypeAST ReturnType, ExprBlock Body) : INodeAST;

public record ParamNode(Token Identifier, ITypeAST DeclaredType) : INodeAST;

public record TypeDeclNode(Token Identifier, GenericDeclNode? GenericTypeParams, 
List<ConDeclNode> Constructors) : INodeAST;

public record GenericDeclNode(List<Token> Identifiers) : INodeAST;

public record ConDeclNode(Token ConstructorName, ITypeAST? PayloadType) : INodeAST;

public record PatternMatchNode(IPatternAST Pattern, IExprAST ResultExpression) : INodeAST;

// --- Statements ---

public record VarDeclStmt(Token Identifier, ITypeAST? DeclaredType, IExprAST Initializer) : IStmtAST;

public record ExprStmt(IExprAST Expression) : IStmtAST;

// --- Expressions --- 

public record ExprBlock(List<IStmtAST> Statements, IExprAST ReturnExpression) : IExprAST;

public record LiteralExpr(Token Value) : IExprAST;

public record ConExpr(Token ConstructorName, List<IExprAST> Payloads) : IExprAST;

public record IdentifierExpr(Token Identifier) : IExprAST;

public record BinaryExpr(IExprAST Left, Token Operator, IExprAST Right) : IExprAST;

public record UnaryExpr(Token Operator, IExprAST Right) : IExprAST;

public record CallExpr(IExprAST Callee, List<IExprAST> Arguments) : IExprAST;

public record TernaryExpr(IExprAST Condition, Token Operator, IExprAST TrueBranch, IExprAST FalseBranch) : IExprAST;

public record IfExpr(Token Operator, IExprAST Condition, ExprBlock TrueBlock,
List<(IExprAST Condition, ExprBlock Block)> ElseIfs, ExprBlock? ElseBlock) : IExprAST;

public record SwitchExpr(Token Operator, IExprAST TargetExpression, List<PatternMatchNode> Cases) : IExprAST;

public record LambdaExpr(List<ParamNode> Parameters, ITypeAST ReturnType, IExprAST Body) : IExprAST;  

public record ListLitExpr(Token Operator, List<IExprAST> Elements) : IExprAST;

public record ArrLitExpr(Token Operator, List<IExprAST> Elements) : IExprAST;

public record TupleLitExpr(Token Operator, List<IExprAST> Elements) : IExprAST; 

// --- Types ---

public record BaseType(Token TypeName) : ITypeAST;

public record TupleType(List<ITypeAST> Types) : ITypeAST;

public record GenericType(Token BaseName, List<ITypeAST> TypeArguments) : ITypeAST;

public record ListType(ITypeAST InnerType) : ITypeAST;
public record ArrType(ITypeAST InnerType) : ITypeAST;

public record FuncType(ITypeAST ParameterType, ITypeAST ReturnType) : ITypeAST;

// --- Pattern ---

public record LiteralPattern(Token Value) : IPatternAST;

public record IdPattern(Token Identifier) : IPatternAST;

public record TuplePattern(List<IPatternAST> Patterns) : IPatternAST;

public record ListPattern(List<IPatternAST> Patterns) : IPatternAST;

public record ArrPattern(List<IPatternAST> Patterns) : IPatternAST;

public record ConsPattern(IPatternAST Head, IPatternAST Tail) : IPatternAST;

public record ConPattern(Token ConstructorName, List<IPatternAST> PayloadPatterns) : IPatternAST;

