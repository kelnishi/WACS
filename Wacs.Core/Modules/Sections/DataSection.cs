using System.IO;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.8. Data Segments
        /// </summary>
        public Data[] Datas { get; internal set; } = null!;

        public uint DataCount { get; internal set; }
        
        /// <summary>
        /// @Spec 2.5.8. Data Segments
        /// </summary>
        public class Data
        {
            public DataMode Mode { get; set; }
            public uint Size { get; set; }
            public byte[] Init { get; set; }

            private Data(DataMode mode, (uint size, byte[] bytes) data) =>
                (Mode, Size, Init) = (mode, data.size, data.bytes);

            private static (uint size, byte[] bytes) ParseByteVector(BinaryReader reader)
            {
                var size = reader.ReadLeb128_u32();
                var bytes = reader.ReadBytes((int)size);
                return (size, bytes);
            }
            
            public static Data Parse(BinaryReader reader) =>
                (DataFlags)reader.ReadLeb128_u32() switch
                {
                    DataFlags.ActiveDefault => 
                        new Data(new DataMode.ActiveMode(0, Expression.Parse(reader)), ParseByteVector(reader)),
                    DataFlags.ActiveExplicit => 
                        new Data(new DataMode.ActiveMode(reader.ReadLeb128_u32(), Expression.Parse(reader)), ParseByteVector(reader)),
                    DataFlags.Passive => 
                        new Data(DataMode.Passive, ParseByteVector(reader)),
                    _ => throw new InvalidDataException($"Malformed Data section at {reader.BaseStream.Position}")
                };
                
        }

        public abstract class DataMode
        {
            public class PassiveMode : DataMode {}
            public static PassiveMode Passive = new PassiveMode();

            public class ActiveMode : DataMode
            {
                public uint MemoryIdx { get; set; } = 0;
                public Expression Offset { get; set; }

                public ActiveMode(uint index, Expression offset) => (MemoryIdx, Offset) = (index, offset);
            }
        }

        public enum DataFlags
        {
            ActiveDefault = 0,
            Passive = 1,
            ActiveExplicit = 2
        }
    }

    public static partial class ModuleParser
    {
        /// <summary>
        /// @Spec 5.5.14 Data Section
        /// </summary>
        private static Module.Data[] ParseDataSection(BinaryReader reader) =>
            reader.ParseVector(Module.Data.Parse);
        
        /// <summary>
        /// @Spec 2.5.15. Data Count Section
        /// </summary>
        private static uint ParseDataCountSection(BinaryReader reader) =>
            reader.ReadLeb128_u32();
    }
}