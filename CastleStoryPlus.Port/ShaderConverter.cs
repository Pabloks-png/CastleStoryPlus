using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace CastleStoryPlus.Port;

// Converts the game's Unity 5.6 shaders to Unity 6's shader format, keeping the compiled programs for the player's
// graphics API: OpenGL Core (Linux) or Direct3D 11 (Windows). Both versions keep a GL program as GLSL source (hlslcc
// output, vertex and fragment in one text) and a Direct3D 11 program as DXBC bytecode, so the programs are taken as
// they are; what changes is how they are packed:
//  - each compiled program ("subprogram") in the blob: a new format version, the vertex channels it reads in Unity 6's
//    order (and for Direct3D which input each channel feeds), and a new header on Direct3D's bytecode;
//  - the blob gets parameter entries: where each uniform, texture and constant buffer of a program lives. 5.6's GL
//    programs list no parameters (the 5.6 engine looked the uniforms up in the linked GLSL), so theirs is the empty
//    one; 5.6's Direct3D programs list theirs after the code, which become the program's own entry;
//  - the parsed form: variants are listed per program as player subprograms with indices into the shader's own keyword
//    list, instead of 5.6's subprograms with their bindings.
// The layout and format numbers come from a shader compiled by the player's own Unity (engine-types.assets).
public sealed class ShaderConverter
{
	public const int GlCorePlatform = 15;
	public const int Direct3D11Platform = 4;

	// Unity 6 lists these keywords first in every shader.
	private static readonly string[] BuiltinKeywords = { "STEREO_INSTANCING_ON", "UNITY_SINGLE_PASS_STEREO", "STEREO_MULTIVIEW_ON", "STEREO_CUBEMAP_RENDER_ON" };

	private static readonly string[] Programs = { "progVertex", "progFragment", "progGeometry", "progHull", "progDomain" };

	// 5.6's Direct3D 11 code starts with a 6-byte header (format 1; texture, constant buffer and sampler counts; two
	// zeros), Unity 6's with a 38-byte one (format 2, the same counts, zeros).
	private const int OldCodeHeader = 6;
	private const int CodeHeader = 38;

	// What a Direct3D 11 Shader Model 5 program requires (Unity 6, from a probe shader with "#pragma target 5.0"); the
	// player's own shader gives the Shader Model 4 value.
	private const long ShaderModel5Requirements = 1101803;

	private readonly AssetTypeTemplateField template;
	private readonly uint programVersion;
	private readonly byte[] emptyParameters;
	private readonly long requirements;
	private readonly int tierCount;
	private readonly int tier;
	private readonly uint stageCount;

	// The shader being converted: its blob entries, the old entries already converted (old index -> program and
	// parameter entry), the parameter entries by content, its keywords.
	private List<byte[]> entries;
	private Dictionary<uint, (uint Program, uint Parameters)> converted;
	private Dictionary<string, uint> parameterEntries;
	private List<string> keywords;

	public ShaderConverter(string engineTypesPath)
	{
		var manager = new AssetsManager();
		AssetsFileInstance types = manager.LoadAssetsFile(engineTypesPath, false);
		TypeTreeType tree = types.file.Metadata.TypeTreeTypes.First(t => t.TypeId == (int)AssetClassID.Shader);
		template = new AssetTypeTemplateField();
		template.FromTypeTree(tree);
		TypeTree = TypeTrees.FromTemplate(template, (int)AssetClassID.Shader);
		TypeHash = tree.TypeHash;

		AssetFileInfo info = types.file.GetAssetsOfType(AssetClassID.Shader).First();
		AssetTypeValueField shader = template.MakeValue(types.file.Reader, info.GetAbsoluteByteOffset(types.file));
		Platform = shader["platforms"]["Array"][0].AsUInt;
		stageCount = shader["stageCounts"]["Array"][0].AsUInt;
		List<byte[]> typeEntries = Entries(PlatformBlob(shader, 0, true), true);
		AssetTypeValueField vertex = shader["m_ParsedForm"]["m_SubShaders"]["Array"][0]["m_Passes"]["Array"][0]["progVertex"];
		List<AssetTypeValueField> tiers = vertex["m_PlayerSubPrograms"]["Array"].Children;
		tierCount = tiers.Count;
		tier = tiers.FindIndex(t => t["Array"].Children.Count > 0);
		AssetTypeValueField sample = tiers[tier]["Array"][0];
		requirements = sample["m_ShaderRequirements"].AsLong;
		programVersion = BitConverter.ToUInt32(typeEntries[(int)sample["m_BlobIndex"].AsUInt], 0);
		emptyParameters = typeEntries[(int)vertex["m_ParameterBlobIndices"]["Array"][tier]["Array"][0].AsUInt];
		types.file.Close();
	}

	// Unity 6's Shader layout, for the files the converted shaders are written to.
	public TypeTreeType TypeTree { get; }

	public Hash128 TypeHash { get; }

	// The player's shader platform: GlCorePlatform or Direct3D11Platform.
	public uint Platform { get; }

	public int Converted { get; private set; }

	public int WithoutPrograms { get; private set; }

	private bool Direct3D => Platform == Direct3D11Platform;

	// The converted shader, from the 5.6 shader's values.
	public AssetTypeValueField Convert(AssetTypeValueField old)
	{
		AssetTypeValueField shader = ValueBuilder.DefaultValueFieldFromTemplate(template);
		FieldCopy.Copy(old, shader);
		AssetTypeValueField form = shader["m_ParsedForm"];
		FixStates(form);

		int platform = old["platforms"]["Array"].Children.FindIndex(p => p.AsUInt == Platform);
		// Debug: CSP_SHADER_SKIP=<regex> leaves the programs out of the shaders it matches.
		string skip = Environment.GetEnvironmentVariable("CSP_SHADER_SKIP");
		if (!string.IsNullOrEmpty(skip) && System.Text.RegularExpressions.Regex.IsMatch(old["m_ParsedForm"]["m_Name"].AsString, skip))
		{
			platform = -1;
		}
		List<byte[]> oldEntries = platform < 0 ? new List<byte[]>() : Entries(PlatformBlob(old, platform, false), false);
		keywords = new List<string>(BuiltinKeywords);
		entries = new List<byte[]> { emptyParameters };
		converted = new Dictionary<uint, (uint, uint)>();
		parameterEntries = new Dictionary<string, uint> { { System.Convert.ToBase64String(emptyParameters), 0 } };

		List<AssetTypeValueField> oldSubShaders = old["m_ParsedForm"]["m_SubShaders"]["Array"].Children;
		List<AssetTypeValueField> subShaders = form["m_SubShaders"]["Array"].Children;
		for (int s = 0; s < subShaders.Count; s++)
		{
			List<AssetTypeValueField> oldPasses = oldSubShaders[s]["m_Passes"]["Array"].Children;
			List<AssetTypeValueField> passes = subShaders[s]["m_Passes"]["Array"].Children;
			for (int p = 0; p < passes.Count; p++)
			{
				foreach (string name in Programs)
				{
					ConvertProgram(oldPasses[p][name], passes[p][name], name == "progVertex", oldEntries);
				}
			}
		}
		AssetTypeValueField keywordNames = form["m_KeywordNames"]["Array"];
		SetArray(keywordNames, keywords.Select(k =>
		{
			AssetTypeValueField item = ValueBuilder.DefaultValueFieldFromArrayTemplate(keywordNames);
			item.AsString = k;
			return item;
		}));
		form["m_KeywordFlags"]["Array"].AsByteArray = new byte[keywords.Count];
		foreach (AssetTypeValueField subShader in subShaders)
		{
			foreach (AssetTypeValueField pass in subShader["m_Passes"]["Array"].Children)
			{
				foreach (string name in Programs.Take(2))
				{
					SetUShorts(pass[name]["m_SerializedKeywordStateMask"]["Array"], Enumerable.Range(0, keywords.Count).Select(i => (ushort)i));
				}
			}
		}

		WriteBlob(shader, Pack(entries));
		if (converted.Count > 0)
		{
			Converted++;
		}
		else
		{
			WithoutPrograms++;
		}
		return shader;
	}

	private void ConvertProgram(AssetTypeValueField old, AssetTypeValueField program, bool vertex, List<byte[]> oldEntries)
	{
		SetArray(program["m_SubPrograms"]["Array"], Enumerable.Empty<AssetTypeValueField>());
		AssetTypeValueField tiers = program["m_PlayerSubPrograms"]["Array"];
		AssetTypeValueField blobIndices = program["m_ParameterBlobIndices"]["Array"];
		SetArray(tiers, Enumerable.Empty<AssetTypeValueField>());
		SetArray(blobIndices, Enumerable.Empty<AssetTypeValueField>());
		if (!vertex && !Direct3D)
		{
			// Unity 6 keeps a GL shader's whole program with the vertex stage.
			return;
		}
		var subPrograms = new List<AssetTypeValueField>();
		var parameterIndices = new List<uint>();
		foreach (AssetTypeValueField sub in old["m_SubPrograms"]["Array"].Children)
		{
			int type = sub["m_GpuProgramType"].AsInt;
			uint index = sub["m_BlobIndex"].AsUInt;
			// GLCore32, GLCore41, GLCore43; DX11VertexSM40 to DX11DomainSM50.
			bool wanted = Direct3D ? type >= 15 && type <= 22 : type >= 6 && type <= 8;
			if (!wanted || index >= oldEntries.Count)
			{
				continue;
			}
			if (!converted.TryGetValue(index, out (uint Program, uint Parameters) blob))
			{
				if (Direct3D)
				{
					byte[] code = ConvertDirect3DSubProgram(oldEntries[(int)index], out byte[] parameters);
					blob = (Add(code), AddParameters(parameters));
				}
				else
				{
					blob = (Add(ConvertSubProgram(oldEntries[(int)index])), 0);
				}
				converted[index] = blob;
			}
			ReadSubProgramKeywords(oldEntries[(int)index], out List<string> names);
			AssetTypeValueField player = ValueBuilder.DefaultValueFieldFromArrayTemplate(TierTemplate(tiers));
			player["m_BlobIndex"].AsUInt = blob.Program;
			SetUShorts(player["m_KeywordIndices"]["Array"], names.Select(n => (ushort)KeywordIndex(n)));
			// DX11VertexSM50, DX11PixelSM50 and the geometry, hull and domain programs.
			bool shaderModel5 = Direct3D && (type == 16 || type >= 18);
			player["m_ShaderRequirements"].AsLong = shaderModel5 ? ShaderModel5Requirements : requirements;
			player["m_GpuProgramType"].AsInt = type;
			subPrograms.Add(player);
			parameterIndices.Add(blob.Parameters);
		}
		if (subPrograms.Count == 0)
		{
			return;
		}
		var tierLists = new List<AssetTypeValueField>();
		var indexLists = new List<AssetTypeValueField>();
		for (int t = 0; t < tierCount; t++)
		{
			AssetTypeValueField list = ValueBuilder.DefaultValueFieldFromArrayTemplate(tiers);
			AssetTypeValueField indices = ValueBuilder.DefaultValueFieldFromArrayTemplate(blobIndices);
			if (t == tier)
			{
				SetArray(list["Array"], subPrograms);
				SetArray(indices["Array"], parameterIndices.Select(i =>
				{
					AssetTypeValueField item = ValueBuilder.DefaultValueFieldFromArrayTemplate(indices["Array"]);
					item.AsUInt = i;
					return item;
				}));
			}
			tierLists.Add(list);
			indexLists.Add(indices);
		}
		SetArray(tiers, tierLists);
		SetArray(blobIndices, indexLists);
	}

	private uint Add(byte[] entry)
	{
		entries.Add(entry);
		return (uint)(entries.Count - 1);
	}

	// Programs with the same parameters share their entry.
	private uint AddParameters(byte[] parameters)
	{
		string key = System.Convert.ToBase64String(parameters);
		if (!parameterEntries.TryGetValue(key, out uint index))
		{
			index = Add(parameters);
			parameterEntries[key] = index;
		}
		return index;
	}

	private static AssetTypeValueField TierTemplate(AssetTypeValueField tiers)
	{
		return ValueBuilder.DefaultValueFieldFromArrayTemplate(tiers)["Array"];
	}

	private int KeywordIndex(string name)
	{
		int index = keywords.IndexOf(name);
		if (index < 0)
		{
			keywords.Add(name);
			index = keywords.Count - 1;
		}
		return index;
	}

	// 5.6 and Unity 6 lay a subprogram out the same way up to its code: format version, program type, four statistics,
	// keywords, code. After the code 5.6 has the vertex channels the program reads and six more values; Unity 6 has the
	// channels (in its own order) and the inputs they feed, none for GL.
	private byte[] ConvertSubProgram(byte[] old)
	{
		int position = ReadSubProgramKeywords(old, out _);
		int codeLength = BitConverter.ToInt32(old, position);
		int codeEnd = Align(position + 4 + codeLength);
		uint channels = BitConverter.ToUInt32(old, codeEnd);

		var result = new MemoryStream();
		var writer = new BinaryWriter(result);
		writer.Write(programVersion);
		writer.Write(old, 4, codeEnd - 4);
		writer.Write(VertexChannels(channels));
		writer.Write(0);
		return result.ToArray();
	}

	// A Direct3D 11 subprogram: after the code 5.6 has the vertex channels, the inputs they feed, and the parameters
	// (groups of uniforms: one without a constant buffer, then one per constant buffer; then the textures, buffers and
	// constant buffers with their slots). The parameters go to their own entry.
	private byte[] ConvertDirect3DSubProgram(byte[] old, out byte[] parameters)
	{
		int position = ReadSubProgramKeywords(old, out _);
		int codeLength = BitConverter.ToInt32(old, position);
		int codeStart = position + 4;
		var reader = new BinaryReader(new MemoryStream(old));
		reader.BaseStream.Position = Align(codeStart + codeLength);

		var result = new MemoryStream();
		var writer = new BinaryWriter(result);
		writer.Write(programVersion);
		writer.Write(old, 4, position - 4);
		var code = new byte[codeLength - OldCodeHeader + CodeHeader];
		code[0] = 2;
		Buffer.BlockCopy(old, codeStart + 1, code, 1, 3);
		Buffer.BlockCopy(old, codeStart + OldCodeHeader, code, CodeHeader, codeLength - OldCodeHeader);
		writer.Write(code.Length);
		writer.Write(code);
		Pad(writer);

		uint channels = reader.ReadUInt32();
		int bindCount = reader.ReadInt32();
		writer.Write(VertexChannels(channels));
		writer.Write(bindCount);
		for (int i = 0; i < bindCount; i++)
		{
			int channel = VertexChannel(reader.ReadInt32());
			reader.ReadInt32();
			writer.Write(channel);
			writer.Write(VertexInput(channel));
		}
		parameters = ConvertParameters(reader);
		return result.ToArray();
	}

	// 5.6: groups (name, size, uniforms: name, type, rows, columns, is matrix, array size, offset), then bindings (name,
	// kind, slot, extra: for a texture its sampler slot << 8 | dimension). Unity 6: format version, the same groups with
	// vectors (name, columns | type << 16, array size, offset), matrices (name, rows | type << 16, array size, offset)
	// and structs apart, then five lists: textures (name, slot, sampler slot, dimension << 1), one the game does not
	// use, constant buffers (name, slot), buffers (name, slot, array size, resource type), one more the game does not
	// use; each slot with its encoding (0 here).
	private byte[] ConvertParameters(BinaryReader reader)
	{
		var result = new MemoryStream();
		var writer = new BinaryWriter(result);
		writer.Write(programVersion);
		int groups = reader.ReadInt32();
		writer.Write(groups);
		for (int g = 0; g < groups; g++)
		{
			WriteString(writer, ReadString(reader));
			writer.Write(reader.ReadInt32());
			int count = reader.ReadInt32();
			var vectors = new List<(string Name, int Size, int ArraySize, int Offset)>();
			var matrices = new List<(string Name, int Size, int ArraySize, int Offset)>();
			for (int i = 0; i < count; i++)
			{
				string name = ReadString(reader);
				int type = reader.ReadInt32();
				int rows = reader.ReadInt32();
				int columns = reader.ReadInt32();
				bool matrix = reader.ReadInt32() != 0;
				int arraySize = reader.ReadInt32();
				int offset = reader.ReadInt32();
				if (matrix)
				{
					matrices.Add((name, rows | type << 16, arraySize, offset));
				}
				else
				{
					vectors.Add((name, columns | type << 16, arraySize, offset));
				}
			}
			foreach (var list in new[] { vectors, matrices })
			{
				writer.Write(list.Count);
				foreach (var parameter in list)
				{
					WriteString(writer, parameter.Name);
					writer.Write(parameter.Size);
					writer.Write(parameter.ArraySize);
					writer.Write(parameter.Offset);
				}
			}
			// Structs.
			writer.Write(0);
		}

		var textures = new List<(string Name, int Slot, int Sampler, int Dimension)>();
		var buffers = new List<(string Name, int Slot)>();
		var constantBuffers = new List<(string Name, int Slot)>();
		int bindings = reader.ReadInt32();
		for (int i = 0; i < bindings; i++)
		{
			string name = ReadString(reader);
			int kind = reader.ReadInt32();
			int slot = reader.ReadInt32();
			int extra = reader.ReadInt32();
			switch (kind)
			{
				case 0:
					textures.Add((name, slot, extra >> 8, extra & 0xff));
					break;
				case 1:
					constantBuffers.Add((name, slot));
					break;
				case 2:
					buffers.Add((name, slot));
					break;
			}
		}
		writer.Write(textures.Count);
		foreach (var texture in textures)
		{
			WriteString(writer, texture.Name);
			writer.Write(0);
			writer.Write(texture.Slot);
			writer.Write(0);
			writer.Write(texture.Sampler);
			writer.Write(texture.Dimension << 1);
		}
		// (Not used by the game's programs.)
		writer.Write(0);
		writer.Write(constantBuffers.Count);
		foreach (var binding in constantBuffers)
		{
			WriteString(writer, binding.Name);
			writer.Write(0);
			writer.Write(binding.Slot);
		}
		// Buffers: also their array size and resource type (1, a structured buffer).
		writer.Write(buffers.Count);
		foreach (var binding in buffers)
		{
			WriteString(writer, binding.Name);
			writer.Write(0);
			writer.Write(binding.Slot);
			writer.Write(1);
			writer.Write(1);
		}
		// (Not used by the game's programs.)
		writer.Write(0);
		return result.ToArray();
	}

	private static string ReadString(BinaryReader reader)
	{
		int length = reader.ReadInt32();
		string value = Encoding.UTF8.GetString(reader.ReadBytes(length));
		reader.BaseStream.Position = Align((int)reader.BaseStream.Position);
		return value;
	}

	private static void WriteString(BinaryWriter writer, string value)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		writer.Write(bytes.Length);
		writer.Write(bytes);
		Pad(writer);
	}

	private static void Pad(BinaryWriter writer)
	{
		while (writer.BaseStream.Position % 4 != 0)
		{
			writer.Write((byte)0);
		}
	}

	// The subprogram's keywords; returns where its code length is.
	private static int ReadSubProgramKeywords(byte[] data, out List<string> keywords)
	{
		int position = 24;
		int count = BitConverter.ToInt32(data, position);
		position += 4;
		keywords = new List<string>(count);
		for (int i = 0; i < count; i++)
		{
			int length = BitConverter.ToInt32(data, position);
			keywords.Add(Encoding.UTF8.GetString(data, position + 4, length));
			position = Align(position + 4 + length);
		}
		return position;
	}

	// 5.6's vertex channels: position, normal, color, uv0-uv3, tangent. Unity 6's: position, normal, tangent, color,
	// uv0-uv7.
	private static readonly int[] ChannelMap = { 0, 1, 3, 4, 5, 6, 7, 2 };

	private static uint VertexChannels(uint old)
	{
		uint result = 0;
		for (int bit = 0; bit < ChannelMap.Length; bit++)
		{
			if ((old & (1u << bit)) != 0)
			{
				result |= 1u << ChannelMap[bit];
			}
		}
		return result;
	}

	private static int VertexChannel(int old)
	{
		return old >= 0 && old < ChannelMap.Length ? ChannelMap[old] : old;
	}

	// The vertex input a Unity 6 channel feeds: position, normal, tangent and color their own, uvN TEXCOORDN (input 4 is
	// unused).
	private static int VertexInput(int channel)
	{
		return channel < 4 ? channel : channel + 1;
	}

	private static int Align(int position)
	{
		return (position + 3) & ~3;
	}

	// Render state values Unity 6 added: depth clip on (5.6 always clipped); a value not driven by a property is named
	// "<noninit>".
	private static void FixStates(AssetTypeValueField form)
	{
		foreach (AssetTypeValueField subShader in form["m_SubShaders"]["Array"].Children)
		{
			foreach (AssetTypeValueField pass in subShader["m_Passes"]["Array"].Children)
			{
				AssetTypeValueField state = pass["m_State"];
				FixValueNames(state);
				if (!state["zClip"].IsDummy)
				{
					state["zClip"]["val"].AsFloat = 1;
				}
			}
		}
	}

	private static void FixValueNames(AssetTypeValueField field)
	{
		if ((field.TypeName == "SerializedShaderFloatValue" || field.TypeName == "SerializedShaderVectorValue") && field["name"].AsString.Length == 0)
		{
			field["name"].AsString = "<noninit>";
		}
		if (field.Value != null)
		{
			return;
		}
		foreach (AssetTypeValueField child in field.Children)
		{
			FixValueNames(child);
		}
	}

	// A platform's blob: 5.6 has one part per platform, Unity 6 a list of parts (one here).
	private static byte[] PlatformBlob(AssetTypeValueField shader, int platform, bool parts)
	{
		AssetTypeValueField offset = shader["offsets"]["Array"][platform];
		AssetTypeValueField compressed = shader["compressedLengths"]["Array"][platform];
		AssetTypeValueField decompressed = shader["decompressedLengths"]["Array"][platform];
		if (parts)
		{
			offset = offset["Array"][0];
			compressed = compressed["Array"][0];
			decompressed = decompressed["Array"][0];
		}
		return LZ4ps.LZ4Codec.Decode32(shader["compressedBlob"]["Array"].AsByteArray, (int)offset.AsUInt, (int)compressed.AsUInt, (int)decompressed.AsUInt);
	}

	// The entries of a platform's blob: 5.6 lists offset and length of each, Unity 6 also a segment.
	private static List<byte[]> Entries(byte[] blob, bool segments)
	{
		int count = BitConverter.ToInt32(blob, 0);
		int stride = segments ? 12 : 8;
		var result = new List<byte[]>(count);
		for (int i = 0; i < count; i++)
		{
			int offset = BitConverter.ToInt32(blob, 4 + i * stride);
			int length = BitConverter.ToInt32(blob, 8 + i * stride);
			var entry = new byte[length];
			Buffer.BlockCopy(blob, offset, entry, 0, length);
			result.Add(entry);
		}
		return result;
	}

	private static byte[] Pack(List<byte[]> entries)
	{
		var result = new MemoryStream();
		var writer = new BinaryWriter(result);
		writer.Write(entries.Count);
		int offset = 4 + entries.Count * 12;
		foreach (byte[] entry in entries)
		{
			writer.Write(offset);
			writer.Write(entry.Length);
			writer.Write(0);
			offset += entry.Length;
		}
		foreach (byte[] entry in entries)
		{
			writer.Write(entry);
		}
		return result.ToArray();
	}

	private void WriteBlob(AssetTypeValueField shader, byte[] blob)
	{
		byte[] compressed = LZ4ps.LZ4Codec.Encode32HC(blob, 0, blob.Length);
		SetUInts(shader["platforms"]["Array"], new[] { Platform });
		SetParts(shader["offsets"]["Array"], 0);
		SetParts(shader["compressedLengths"]["Array"], (uint)compressed.Length);
		SetParts(shader["decompressedLengths"]["Array"], (uint)blob.Length);
		shader["compressedBlob"]["Array"].AsByteArray = compressed;
		SetUInts(shader["stageCounts"]["Array"], new[] { stageCount });
		shader["m_ShaderIsBaked"].AsBool = true;
	}

	private static void SetParts(AssetTypeValueField array, uint value)
	{
		AssetTypeValueField parts = ValueBuilder.DefaultValueFieldFromArrayTemplate(array);
		SetUInts(parts["Array"], new[] { value });
		SetArray(array, new[] { parts });
	}

	private static void SetUInts(AssetTypeValueField array, IEnumerable<uint> values)
	{
		SetArray(array, values.Select(v =>
		{
			AssetTypeValueField item = ValueBuilder.DefaultValueFieldFromArrayTemplate(array);
			item.AsUInt = v;
			return item;
		}));
	}

	private static void SetUShorts(AssetTypeValueField array, IEnumerable<ushort> values)
	{
		SetArray(array, values.Select(v =>
		{
			AssetTypeValueField item = ValueBuilder.DefaultValueFieldFromArrayTemplate(array);
			item.AsUShort = v;
			return item;
		}));
	}

	private static void SetArray(AssetTypeValueField array, IEnumerable<AssetTypeValueField> items)
	{
		array.Children = items.ToList();
		array.AsArray = new AssetTypeArrayInfo(array.Children.Count);
	}
}
