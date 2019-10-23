﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DatTool
{
    class DatFile
    {
        public List<(string Name, string Type)> ValueInfo = new List<(string Name, string Type)>();
        public List<List<string>> StructEntries = new List<List<string>>();

        public void Load(string datPath)
        {
            using BinaryReader reader = new BinaryReader(new FileStream(datPath, FileMode.Open));

            // Read header
            uint structCount = reader.ReadUInt32();
            uint structSize = reader.ReadUInt32();
            uint valuesPerStruct = reader.ReadUInt32();

            // Read value names & types
            for (int v = 0; v < valuesPerStruct; ++v)
            {
                // Read two null-terminated UTF-8 strings (name, type)
                string valueName = string.Empty;
                string valueType = string.Empty;
                for (int s = 0; s < 2; ++s)
                {
                    List<byte> stringData = new List<byte>();
                    while (true)
                    {
                        byte b = reader.ReadByte();

                        if (b == 0)
                            break;

                        stringData.Add(b);
                    }

                    if (s == 0)
                    {
                        valueName = new UTF8Encoding().GetString(stringData.ToArray());
                    }
                    else
                    {
                        valueType = new UTF8Encoding().GetString(stringData.ToArray());
                    }
                }
                ValueInfo.Add((valueName, valueType));

                // Skip value terminator bytes?
                reader.ReadBytes(2);
            }

            // Align to nearest 16-byte boundary
            reader.BaseStream.Seek((0x10 - (reader.BaseStream.Position % 0x10)) % 0x10, SeekOrigin.Current);

            // Save the current position so we can return there after reading the string data blocks,
            // so we can easily dereference values pointing to them
            long structDataPosition = reader.BaseStream.Position;

            // Skip past the struct data and read UTF-8 and UTF-16 strings, if present
            reader.BaseStream.Seek(structCount * structSize, SeekOrigin.Current);

            ushort utf8StringCount = reader.ReadUInt16();
            ushort utf16StringCount = reader.ReadUInt16();

            List<string> utf8Strings = new List<string>();
            for (int s = 0; s < utf8StringCount; ++s)
            {
                List<byte> stringData = new List<byte>();
                while (true)
                {
                    byte b = reader.ReadByte();

                    if (b == 0)
                        break;

                    stringData.Add(b);
                }
                utf8Strings.Add(new UTF8Encoding().GetString(stringData.ToArray()));
            }

            List<string> utf16Strings = new List<string>();
            for (int s = 0; s < utf16StringCount; ++s)
            {
                List<byte> stringData = new List<byte>();
                while (true)
                {
                    byte b1 = reader.ReadByte();
                    byte b2 = reader.ReadByte();

                    if (b1 == 0 && b2 == 0)
                        break;

                    stringData.AddRange(new byte[] { b2, b1 });
                }
                utf16Strings.Add(new UnicodeEncoding(false, false).GetString(stringData.ToArray()));
            }

            // Return to the position we saved earlier
            reader.BaseStream.Seek(structDataPosition, SeekOrigin.Begin);

            // Read struct data according to its type
            for (int structNum = 0; structNum < structCount; ++structNum)
            {
                List<string> structEntry = new List<string>();
                foreach (var value in ValueInfo)
                {
                    switch (value.Type)
                    {
                        case "u8":
                            structEntry.Add(reader.ReadByte().ToString());
                            break;

                        case "u16":
                            structEntry.Add(reader.ReadUInt16().ToString());
                            break;

                        case "u32":
                            structEntry.Add(reader.ReadUInt32().ToString());
                            break;

                        case "u64":
                            structEntry.Add(reader.ReadUInt64().ToString());
                            break;

                        case "s8":
                            structEntry.Add(reader.ReadSByte().ToString());
                            break;

                        case "s16":
                            structEntry.Add(reader.ReadInt16().ToString());
                            break;

                        case "s32":
                            structEntry.Add(reader.ReadInt32().ToString());
                            break;

                        case "s64":
                            structEntry.Add(reader.ReadInt64().ToString());
                            break;

                        case "f32":
                            structEntry.Add(reader.ReadSingle().ToString());
                            break;

                        case "f64":
                            structEntry.Add(reader.ReadDouble().ToString());
                            break;

                        case "LABEL":
                        case "REFER":
                        case "ASCII":
                            ushort utf8Id = reader.ReadUInt16();
                            structEntry.Add(utf8Strings[utf8Id]);
                            break;

                        case "UTF16":
                            ushort utf16Id = reader.ReadUInt16();
                            structEntry.Add(utf16Strings[utf16Id]);
                            break;
                    }
                }
                StructEntries.Add(structEntry);
            }
        }

        public void Save(string datPath)
        {
            using BinaryWriter writer = new BinaryWriter(new FileStream(datPath, FileMode.Create));

            // Write header (some values may need to be filled in later)
            writer.Write(StructEntries.Count);     // structCount

            int structSize = 0;
            foreach (var value in ValueInfo)
            {
                switch (value.Type)
                {
                    case "u8":
                    case "s8":
                        structSize += 1;
                        break;

                    case "u16":
                    case "s16":
                    case "LABEL":
                    case "REFER":
                    case "ASCII":
                    case "UTF16":
                        structSize += 2;
                        break;

                    case "u32":
                    case "s32":
                    case "f32":
                        structSize += 4;
                        break;

                    case "u64":
                    case "s64":
                    case "f64":
                        structSize += 8;
                        break;
                }
            }
            writer.Write(structSize);           // structSize

            writer.Write(ValueInfo.Count);      // valuesPerStruct

            // Write value info
            foreach (var value in ValueInfo)
            {
                writer.Write(new UTF8Encoding().GetBytes(value.Name.Trim()));
                writer.Write((byte)0);
                writer.Write(new UTF8Encoding().GetBytes(value.Type.Trim()));
                writer.Write((byte)0);
                writer.Write((ushort)1);    // value terminator?
            }

            // Write padding to nearest 16-byte boundary
            writer.Write(new byte[(0x10 - (writer.BaseStream.Position % 0x10)) % 0x10]);

            // Process struct values according to their type
            List<byte> structData = new List<byte>();
            List<string> utf8Strings = new List<string>();
            List<string> utf16Strings = new List<string>();
            foreach (var entry in StructEntries)
            {
                for (int valueNum = 0; valueNum < ValueInfo.Count; ++valueNum)
                {
                    switch (ValueInfo[valueNum].Type)
                    {
                        case "u8":
                            if (!byte.TryParse(entry[valueNum], out byte u8))
                            {
                                Console.WriteLine($"ERROR: Value in row {StructEntries.IndexOf(entry)}, column {valueNum} is not an 8-bit unsigned integer.");
                                return;
                            }
                            structData.Add(u8);
                            break;

                        case "u16":
                            if (!ushort.TryParse(entry[valueNum], out ushort u16))
                            {
                                Console.WriteLine($"ERROR: Value in row {StructEntries.IndexOf(entry)}, column {valueNum} is not a 16-bit unsigned integer.");
                                return;
                            }
                            structData.AddRange(BitConverter.GetBytes(u16));
                            break;

                        case "u32":
                            if (!uint.TryParse(entry[valueNum], out uint u32))
                            {
                                Console.WriteLine($"ERROR: Value in row {StructEntries.IndexOf(entry)}, column {valueNum} is not a 32-bit unsigned integer.");
                                return;
                            }
                            structData.AddRange(BitConverter.GetBytes(u32));
                            break;

                        case "u64":
                            if (!ulong.TryParse(entry[valueNum], out ulong u64))
                            {
                                Console.WriteLine($"ERROR: Value in row {StructEntries.IndexOf(entry)}, column {valueNum} is not a 64-bit unsigned integer.");
                                return;
                            }
                            structData.AddRange(BitConverter.GetBytes(u64));
                            break;

                        case "s8":
                            if (!sbyte.TryParse(entry[valueNum], out sbyte s8))
                            {
                                Console.WriteLine($"ERROR: Value in row {StructEntries.IndexOf(entry)}, column {valueNum} is not an 8-bit signed integer.");
                                return;
                            }
                            structData.Add((byte)s8);
                            break;

                        case "s16":
                            if (!short.TryParse(entry[valueNum], out short s16))
                            {
                                Console.WriteLine($"ERROR: Value in row {StructEntries.IndexOf(entry)}, column {valueNum} is not a 16-bit signed integer.");
                                return;
                            }
                            structData.AddRange(BitConverter.GetBytes(s16));
                            break;

                        case "s32":
                            if (!int.TryParse(entry[valueNum], out int s32))
                            {
                                Console.WriteLine($"ERROR: Value in row {StructEntries.IndexOf(entry)}, column {valueNum} is not a 32-bit signed integer.");
                                return;
                            }
                            structData.AddRange(BitConverter.GetBytes(s32));
                            break;

                        case "s64":
                            if (!long.TryParse(entry[valueNum], out long s64))
                            {
                                Console.WriteLine($"ERROR: Value in row {StructEntries.IndexOf(entry)}, column {valueNum} is not a 64-bit signed integer.");
                                return;
                            }
                            structData.AddRange(BitConverter.GetBytes(s64));
                            break;

                        case "f32":
                            if (!float.TryParse(entry[valueNum], out float f32))
                            {
                                Console.WriteLine($"ERROR: Value in row {StructEntries.IndexOf(entry)}, column {valueNum} is not a 32-bit float.");
                                return;
                            }
                            structData.AddRange(BitConverter.GetBytes(f32));
                            break;

                        case "f64":
                            if (!double.TryParse(entry[valueNum], out double f64))
                            {
                                Console.WriteLine($"ERROR: Value in row {StructEntries.IndexOf(entry)}, column {valueNum} is not a 64-bit float.");
                                return;
                            }
                            structData.AddRange(BitConverter.GetBytes(f64));
                            break;

                        case "LABEL":
                        case "REFER":
                        case "ASCII":
                            int found8 = utf8Strings.IndexOf(entry[valueNum]);
                            if (found8 == -1)
                            {
                                found8 = utf8Strings.Count;
                                utf8Strings.Add(entry[valueNum]);
                            }
                            structData.AddRange(BitConverter.GetBytes((ushort)found8));
                            break;

                        case "UTF16":
                            int found16 = utf16Strings.IndexOf(entry[valueNum]);
                            if (found16 == -1)
                            {
                                found16 = utf8Strings.Count;
                                utf8Strings.Add(entry[valueNum]);
                            }
                            structData.AddRange(BitConverter.GetBytes((ushort)found16));
                            break;

                        default:
                            Console.WriteLine($"ERROR: The data type in column {StructEntries.IndexOf(entry)} is invalid.");
                            return;
                    }
                }
            }

            // Write the struct entry data
            writer.Write(structData.ToArray());

            // Write utf8 and utf16 string count
            writer.Write((ushort)utf8Strings.Count);
            writer.Write((ushort)utf16Strings.Count);

            // Write utf8 and utf16 strings, if present
            foreach (string str in utf8Strings)
            {
                writer.Write(new UTF8Encoding().GetBytes(str));
                writer.Write((byte)0);
            }

            foreach (string str in utf16Strings)
            {
                writer.Write(new UnicodeEncoding(false, false).GetBytes(str));
                writer.Write((byte)0);
            }

            writer.Flush(); // Just in case
            writer.Close();
        }
    }
}
