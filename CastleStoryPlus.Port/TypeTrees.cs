using System.Collections.Generic;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace CastleStoryPlus.Port;

// Builds type trees (the field layout Unity writes into files that carry them). AssetsTools.NET's own
// ClassDatabaseToTypeTree leaves the type blob unset, so this does the same with a blob.
public static class TypeTrees
{
	public static TypeTreeType FromClass(ClassDatabaseFile classes, int classId)
	{
		ClassDatabaseType type = classes.FindAssetClassByID(classId);
		if (type == null)
		{
			return null;
		}
		var builder = new Builder();
		builder.Add(classes, type.GetPreferredNode(false), 0);
		return builder.Finish(classId);
	}

	// A MonoBehaviour's layout, from a template that includes the script's fields.
	public static TypeTreeType FromTemplate(AssetTypeTemplateField template, int classId)
	{
		var builder = new Builder();
		builder.Add(template, 0);
		return builder.Finish(classId);
	}

	private sealed class Builder
	{
		private readonly List<TypeTreeNode> nodes = new List<TypeTreeNode>();
		private readonly Dictionary<string, uint> offsets = new Dictionary<string, uint>();
		private readonly StringBuilder strings = new StringBuilder();
		private uint length;

		public void Add(ClassDatabaseFile classes, ClassDatabaseTypeNode node, int depth)
		{
			nodes.Add(new TypeTreeNode
			{
				Version = node.Version,
				Level = (byte)depth,
				TypeFlags = (TypeTreeNodeFlags)node.TypeFlags,
				TypeStrOffset = Offset(classes.GetString(node.TypeName)),
				NameStrOffset = Offset(classes.GetString(node.FieldName)),
				ByteSize = node.ByteSize,
				Index = (uint)nodes.Count,
				MetaFlags = node.MetaFlag,
			});
			foreach (ClassDatabaseTypeNode child in node.Children)
			{
				Add(classes, child, depth + 1);
			}
		}

		public void Add(AssetTypeTemplateField field, int depth)
		{
			// A script's Matrix4x4 field: the template generator lays it out as a plain struct of the C# fields
			// (m00, m10, ...), but Unity writes it as Matrix4x4f, sixteen floats e00, e01, ... e33 row by row. With the
			// C# names Unity 6 matched no field and left the script's default (the bricktrons' grip and socket matrices
			// stayed identity: what they carry sat at the bone instead of on the back or in the hand). The floats are in
			// the file in Unity's order, so they only get Unity's names.
			bool matrix = field.Type == "Matrix4x4" && field.Children != null && field.Children.Count == 16;
			if (matrix)
			{
				for (int i = 0; i < 16; i++)
				{
					field.Children[i].Name = "e" + (i / 4) + (i % 4);
				}
			}
			nodes.Add(new TypeTreeNode
			{
				Version = field.Version,
				Level = (byte)depth,
				TypeFlags = field.IsArray ? TypeTreeNodeFlags.Array : TypeTreeNodeFlags.None,
				TypeStrOffset = Offset(matrix ? "Matrix4x4f" : field.Type),
				NameStrOffset = Offset(field.Name),
				ByteSize = ByteSize(field),
				Index = (uint)nodes.Count,
				MetaFlags = field.IsAligned ? 0x4000u : 0u,
			});
			if (field.Children != null)
			{
				foreach (AssetTypeTemplateField child in field.Children)
				{
					Add(child, depth + 1);
				}
			}
		}

		public TypeTreeType Finish(int classId)
		{
			return new TypeTreeType
			{
				TypeId = classId,
				ScriptTypeIndex = ushort.MaxValue,
				ScriptIdHash = Hash128.NewBlankHash(),
				TypeHash = Hash128.NewBlankHash(),
				ExtTypeHash = Hash128.NewBlankHash(),
				TypeBlobIsDefinition = true,
				TypeDependencies = new int[0],
				TypeBlob = new TypeTreeBlob
				{
					Nodes = nodes,
					StringBufferBytes = Encoding.UTF8.GetBytes(strings.ToString()),
				},
			};
		}

		private uint Offset(string text)
		{
			if (!offsets.TryGetValue(text, out uint offset))
			{
				offset = length;
				offsets[text] = offset;
				strings.Append(text).Append('\0');
				length += (uint)Encoding.UTF8.GetByteCount(text) + 1;
			}
			return offset;
		}

		private static int ByteSize(AssetTypeTemplateField field)
		{
			if (field.IsArray || (field.Children != null && field.Children.Count > 0))
			{
				return -1;
			}
			switch (field.ValueType)
			{
				case AssetValueType.Bool:
				case AssetValueType.Int8:
				case AssetValueType.UInt8:
					return 1;
				case AssetValueType.Int16:
				case AssetValueType.UInt16:
					return 2;
				case AssetValueType.Int32:
				case AssetValueType.UInt32:
				case AssetValueType.Float:
					return 4;
				case AssetValueType.Int64:
				case AssetValueType.UInt64:
				case AssetValueType.Double:
					return 8;
				default:
					return -1;
			}
		}
	}
}
