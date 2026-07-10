using Cera.Compiler.Lexer;
using Cera.Compiler.Logging;

namespace Cera.Compiler.Backend;

public static class BinaryExporter
{
    public static void Export(Module module, string filePath)
    {
        using BinaryWriter writer = new(File.Open(filePath, FileMode.Create));

        // header
        writer.Write(['C', 'E', 'R', 'A']);
        writer.Write((uint)1); // version 1.0.0
        writer.Write(module.EntryPoint?.Index ?? -1);

        var functions = module.GetSortedFunctions();
        writer.Write(functions.Count);

        foreach (var func in functions)
        {
            writer.Write(func.Index);
            writer.Write((byte)func.Arity);
            
            writer.Write((ushort)func.Body.Constants.Count);
            
            foreach (var constant in func.Body.Constants)
            {
                writer.Write((byte)constant.Tag); 
                
                switch (constant.Tag)
                {
                    case CeraValue.ValueTag.Int: 
                        writer.Write(constant.IntValue); 
                        break;
                        
                    case CeraValue.ValueTag.Float: 
                        writer.Write(constant.FloatValue); 
                        break;
                        
                    case CeraValue.ValueTag.Bool:
                        writer.Write((byte)constant.IntValue); 
                        break;
                        
                    case CeraValue.ValueTag.Char:
                        writer.Write((int)constant.IntValue); 
                        break;
                        
                    case CeraValue.ValueTag.Unit:
                        // ZERO payload bytes. The tag itself is the entire value.
                        break;
                        
                    case CeraValue.ValueTag.String: 
                        byte[] strBytes = System.Text.Encoding.UTF8.GetBytes(constant.StringValue!);
                        writer.Write(strBytes.Length); 
                        writer.Write(strBytes);        
                        break;
                        
                    default:
                        throw new EmitterException($"Fatal Compiler Error: Unhandled constant tag '{constant.Tag}' during binary export.", Token.None());
                }
            }

            writer.Write(func.Body.Code.Count);
            writer.Write(func.Body.Code.ToArray());
        }
    }
}