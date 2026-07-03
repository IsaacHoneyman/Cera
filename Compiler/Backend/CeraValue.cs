namespace Cera.Compiler.Backend;

public struct CeraValue
{
    public enum ValueTag : byte
    {
        Int, Float, Bool, Char, Unit, Object
    }

    public ValueTag Tag;
    public long IntValue;
    public double FloatValue;
    public object? ObjectValue; 

    public static CeraValue Int(long val) => new() { Tag = ValueTag.Int, IntValue = val };
    public static CeraValue Float(double val) => new() { Tag = ValueTag.Float, FloatValue = val };
    public static CeraValue Bool(bool val) => new() { Tag = ValueTag.Bool, IntValue = val ? 1 : 0 };
    public static CeraValue Char(int val) => new() { Tag = ValueTag.Char, IntValue = val };
    public static CeraValue Unit() => new() { Tag = ValueTag.Unit, IntValue = 0 };
    public static CeraValue Object(object obj) => new() { Tag = ValueTag.Object, ObjectValue = obj };
}