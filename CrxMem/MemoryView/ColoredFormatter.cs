using System;
using System.Drawing;
using System.Collections.Generic;
using Iced.Intel;

namespace CrxMem.MemoryView
{
    /// <summary>
    /// Represents a colored part of an instruction (e.g., register, number, etc.)
    /// </summary>
    public class ColoredInstructionPart
    {
        public string Text { get; set; } = "";
        public Color Color { get; set; }
    }

    /// <summary>
    /// FormatterOutput that produces colored instruction parts with theme support
    /// </summary>
    public class ColoredFormatterOutput : FormatterOutput
    {
        private List<ColoredInstructionPart> _parts = new List<ColoredInstructionPart>();

        public List<ColoredInstructionPart> GetParts() => _parts;

        public override void Write(string text, FormatterTextKind kind)
        {
            if (string.IsNullOrEmpty(text))
                return;

            Color color = GetColorForTextKind(kind);

            // Merge consecutive parts with same color for performance
            if (_parts.Count > 0 && _parts[_parts.Count - 1].Color == color)
            {
                _parts[_parts.Count - 1].Text += text;
            }
            else
            {
                _parts.Add(new ColoredInstructionPart
                {
                    Text = text,
                    Color = color
                });
            }
        }

        /// <summary>
        /// Get color for text kind - uses ThemeManager for dark/light theme support
        /// </summary>
        private Color GetColorForTextKind(FormatterTextKind kind)
        {
            return kind switch
            {
                // Opcodes/Instructions (mov, add, call, etc.)
                FormatterTextKind.Directive => ThemeManager.SyntaxMnemonic,
                FormatterTextKind.Keyword => ThemeManager.SyntaxMnemonic,
                FormatterTextKind.Mnemonic => ThemeManager.SyntaxMnemonic,

                // Registers (eax, ebx, esp, rax, etc.)
                FormatterTextKind.Register => ThemeManager.SyntaxRegister,

                // Numbers (immediate values, offsets)
                FormatterTextKind.Number => ThemeManager.SyntaxNumber,

                // Memory addresses in brackets [...]
                FormatterTextKind.LabelAddress => ThemeManager.SyntaxAddress,
                FormatterTextKind.FunctionAddress => ThemeManager.SyntaxCall,

                // Punctuation (brackets, commas, +, -, etc.)
                FormatterTextKind.Punctuation => ThemeManager.Foreground,
                FormatterTextKind.Operator => ThemeManager.Foreground,

                // Data/text
                FormatterTextKind.Data => ThemeManager.ForegroundDim,
                FormatterTextKind.Text => ThemeManager.Foreground,

                // Prefix (lock, rep, etc.)
                FormatterTextKind.Prefix => ThemeManager.SyntaxPrefix,

                // Default
                _ => ThemeManager.Foreground
            };
        }

        public void Clear()
        {
            _parts.Clear();
        }
    }
}
